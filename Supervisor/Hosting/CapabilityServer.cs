using System;using System.IO;using System.Threading;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Capabilities;using SpatialWallpaper.Supervision.Broker;
namespace SpatialWallpaper.Supervision.Windows {
 // Dedicated bounded data lane. It never runs disk work or native writes on the monitor/reader thread.
 public sealed class CapabilityServer:IDisposable {
  public const int ReceiveMilliseconds=1000;readonly WorkerWritePipe requests;readonly WorkerReadPipe replies;readonly CapabilityEndpoint endpoint;readonly WorkerIdentity identity;readonly Func<long> now;
  readonly AutoResetEvent wake=new AutoResetEvent(false);Thread reader,writer;long receiving,sent;int closed,fault;bool disposed;
  readonly AudioBroker audio;readonly BrokerSession session;
  public SoundOutput Sound{get;}
  readonly InputTarget input;long sentInputs;public long SentInputs=>Interlocked.Read(ref sentInputs);
  public IntPtr ChildRequestWriter=>requests.ChildWriter;public IntPtr ChildReplyReader=>replies.ChildReader;public long SentReplies=>Interlocked.Read(ref sent);
  public bool ProtocolFault{get{long phase=Interlocked.Read(ref receiving);return Volatile.Read(ref fault)!=0||endpoint.Faulted||Volatile.Read(ref closed)==0&&phase>0&&now()-(phase-1)>ReceiveMilliseconds;}}
  public CapabilityServer(string packageSid,BrokerSession session,StorageBroker storage,Func<long> clock,LlmBroker llm=null,AudioBroker audio=null,InputTarget input=null){
   this.audio=audio;this.session=session;
   now=clock??throw new ArgumentNullException(nameof(clock));identity=new WorkerIdentity(session.Attempt,session.Instance,session.Generation);
   if(input!=null&&!input.Matches(identity))throw new ArgumentException("input-target-session-mismatch");
   requests=new WorkerWritePipe(packageSid);try{replies=new WorkerReadPipe(packageSid);Sound=session.Allows("sound-playback")?new SoundOutput():null;endpoint=new CapabilityEndpoint(session,storage,llm,Sound);}catch{Sound?.Dispose();requests.Dispose();replies?.Dispose();throw;}this.input=input;
  }
  public void CloseChildCopies(){requests.CloseChildCopy();replies.CloseChildCopy();}
  bool Exact(byte[] bytes,int length,bool emptyEof){
   int at=0;while(at<length){int count=requests.Read(bytes,at,length-at);if(count==0){if(at==0&&emptyEof)return false;throw new EndOfStreamException("partial-capability-message");}if(Interlocked.Read(ref receiving)==0)Interlocked.Exchange(ref receiving,checked(now()+1));if(now()-(Interlocked.Read(ref receiving)-1)>ReceiveMilliseconds)throw new InvalidDataException("capability-receive-deadline");at+=count;}return true;
  }
  public void Start(){if(reader!=null)throw new InvalidOperationException("capability-server-already-started");
   reader=new Thread(()=>{try{
    var header=new byte[64];var body=new byte[CapabilityWire.MaxPayloadBytes];long sequence=0;
    while(Volatile.Read(ref closed)==0&&Exact(header,header.Length,true)){
     var message=CapabilityWire.Validate(header,identity,false,sequence);Exact(body,message.PayloadBytes,false);
     if(!endpoint.Receive(header,body,message.PayloadBytes))throw new InvalidDataException("capability-request-rejected");sequence=message.Sequence;Interlocked.Exchange(ref receiving,0);wake.Set();
    }
   }catch{if(Volatile.Read(ref closed)==0){Interlocked.Exchange(ref fault,1);Close();}}finally{Interlocked.Exchange(ref receiving,0);}}){IsBackground=true,Name="SpatialWallpaper capability requests"};
   writer=new Thread(()=>{try{
    var header=new byte[64];long sequence=0,lastAudio=0,lastInput=0;bool pushed=false,inputPushed=false;while(Volatile.Read(ref closed)==0){
     bool wrote=false;
     if(endpoint.TryTake(out var reply)){byte[] body=reply.Payload;CapabilityWire.Encode(header,identity,reply.Operation,true,reply.Error,reply.RequestId,checked(++sequence),body.Length);
      if(Volatile.Read(ref closed)!=0)break;replies.Write(header,0,header.Length);replies.Write(body,0,body.Length);Interlocked.Increment(ref sent);wrote=true;}
     if(audio!=null&&session.Allows("audio-features")&&(!pushed||now()-lastAudio>=200)){
      byte[] body=audio.Snapshot();CapabilityWire.Encode(header,identity,CapabilityOperation.AudioSnapshot,true,false,0,checked(++sequence),body.Length);
      if(Volatile.Read(ref closed)!=0)break;replies.Write(header,0,header.Length);replies.Write(body,0,body.Length);lastAudio=now();pushed=true;wrote=true;
     }
     if(input!=null&&session.Allows("input")&&(!inputPushed||now()-lastInput>=17)){
      byte[] body=input.Snapshot();CapabilityWire.Encode(header,identity,CapabilityOperation.InputSnapshot,true,false,0,checked(++sequence),body.Length);
      if(Volatile.Read(ref closed)!=0)break;replies.Write(header,0,header.Length);replies.Write(body,0,body.Length);lastInput=now();inputPushed=true;Interlocked.Increment(ref sentInputs);wrote=true;
     }
     if(!wrote)wake.WaitOne(10);
    }
   }catch{if(Volatile.Read(ref closed)==0){Interlocked.Exchange(ref fault,1);Close();}}}){IsBackground=true,Name="SpatialWallpaper capability replies"};reader.Start();writer.Start();
  }
  // Nonblocking. Caller terminates/observes the child kernel before Dispose joins these pipe threads.
  public void Close(){Interlocked.Exchange(ref closed,1);endpoint.Close();input?.Dispose();wake.Set();}
  public void Dispose(){
   if(disposed)return;Close();Sound?.Dispose();if(reader!=null&&!reader.Join(1000)||writer!=null&&!writer.Join(1000))throw new InvalidOperationException("capability-pipe-thread-still-alive");requests.Dispose();replies.Dispose();wake.Dispose();disposed=true;
  }
 }
}
