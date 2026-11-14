using System;using System.IO;using System.IO.Pipes;using System.Threading;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Capabilities;
// Trusted ordinary-Play adapter. Always uses preview persistence; contains no author code.
static class PlayBroker {
 static bool Exact(Stream stream,byte[] bytes,int count){int at=0;while(at<count){int n=stream.Read(bytes,at,count-at);if(n==0){if(at==0)return false;throw new EndOfStreamException();}at+=n;}return true;}
 static int Main(string[] args){try{
  if(args.Length<6||args.Length>8)throw new ArgumentException("play-broker-arguments");
  var receipt=new AttemptReceipt(Guid.NewGuid(),Guid.ParseExact(args[2],"N"),Guid.ParseExact(args[4],"N"),new string('0',64),Guid.ParseExact(args[3],"N"),1);
  var identity=new WorkerIdentity(receipt.Attempt,receipt.Instance,1);var session=new BrokerSession(receipt,true,new[]{"private-storage","llm-proxy","audio-features","sound-playback"});
  LlmConfiguration configuration=null;if(args.Length>=7&&!string.IsNullOrEmpty(args[6])){var file=new FileInfo(args[6]);if(file.Length>16384)throw new ArgumentException("play-settings-size");configuration=LlmConfiguration.Parse(Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(args[6])));}
  AudioConfiguration audioConfiguration=null;if(args.Length==8){var file=new FileInfo(args[7]);if(file.Length>1024)throw new ArgumentException("play-audio-settings-size");audioConfiguration=AudioConfiguration.Parse(Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(args[7])));}
  using(var simulation=configuration==null?new SpatialWallpaper.Supervision.Preview.DeterministicLlmServer():null)
  using(var station=new SpatialWallpaper.Supervision.Windows.PrivateStation())using(var audio=audioConfiguration?.Open(station))using(var llm=new LlmBroker(configuration??LlmConfiguration.Parse(simulation.Configuration)))using(var sound=new SoundOutput()){
  sound.Configure(.25f,.5f);var storage=new StorageBroker(args[5]);storage.Initialized.GetAwaiter().GetResult();var endpoint=new CapabilityEndpoint(session,storage,llm,sound);int closed=0,fault=0;
  using(var input=new AnonymousPipeClientStream(PipeDirection.In,args[0]))using(var output=new AnonymousPipeClientStream(PipeDirection.Out,args[1])){
   var writer=new Thread(()=>{try{long sequence=0,lastAudio=0;bool pushed=false;var clock=System.Diagnostics.Stopwatch.StartNew();var header=new byte[64];while(Volatile.Read(ref closed)==0){
    bool wrote=false;if(endpoint.TryTake(out var reply)){var body=reply.Payload;CapabilityWire.Encode(header,identity,reply.Operation,true,reply.Error,reply.RequestId,++sequence,body.Length);output.Write(header,0,64);output.Write(body,0,body.Length);wrote=true;}
    if(audio!=null&&session.Allows("audio-features")&&(!pushed||clock.ElapsedMilliseconds-lastAudio>=200)){var body=audio.Snapshot();CapabilityWire.Encode(header,identity,CapabilityOperation.AudioSnapshot,true,false,0,++sequence,body.Length);output.Write(header,0,64);output.Write(body,0,body.Length);lastAudio=clock.ElapsedMilliseconds;pushed=true;wrote=true;}
    if(!wrote)Thread.Sleep(2);
   }}catch{Interlocked.Exchange(ref fault,1);input.Dispose();}}){IsBackground=true};writer.Start();
   try{var header=new byte[64];var body=new byte[CapabilityWire.MaxPayloadBytes];long sequence=0;while(Exact(input,header,64)){var message=CapabilityWire.Validate(header,identity,false,sequence);if(!Exact(input,body,message.PayloadBytes)||!endpoint.Receive(header,body,message.PayloadBytes))throw new InvalidDataException("play-capability-rejected");sequence=message.Sequence;}}
   finally{endpoint.Close();Interlocked.Exchange(ref closed,1);storage.Close();llm.Dispose();if(!writer.Join(1000)||!llm.Completion.Wait(1000))throw new IOException("play-reply-stop-timeout");storage.Completion.GetAwaiter().GetResult();}
  }
  return fault==0?0:2;}
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
