using System;using System.IO;using System.IO.Pipes;using System.Text;using System.Threading;using System.Threading.Tasks;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;
static class FrameTransportChecks {
 sealed class Lease:IRuntimeSessionLease {
  public WorkerIdentity Identity{get;}public Task Running=>Task.CompletedTask;readonly TaskCompletionSource<bool> ended=new TaskCompletionSource<bool>();public Task Completion=>ended.Task;
  internal Lease(WorkerIdentity identity){Identity=identity;}public Task StopAsync(){ended.TrySetResult(true);return Task.CompletedTask;}
 }
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);Console.WriteLine("PASS "+reason);}
 internal static void Run(string directory){
  var identity=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),7);
  foreach(string mode in new[]{"wrong-host","wrong-generation","replay","partial","oversized","stall","concurrent","stop"}){
   string pipe="SpatialWallpaper.Supervisor."+Guid.NewGuid().ToString("N");Guid host=Guid.NewGuid();string descriptor=Path.Combine(directory,"frame-endpoint.json");
   File.WriteAllText(descriptor,new JObject{{"version",1},{"pipe",pipe},{"auth",Convert.ToBase64String(new byte[32])},{"session_host",host.ToString("N")}}.ToString());
   using(var server=new NamedPipeServerStream(pipe,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous))
   using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
    var serving=Task.Run(()=>{
     try{server.WaitForConnection();using(var reader=new BinaryReader(server,Encoding.UTF8,true))using(var writer=new BinaryWriter(server,Encoding.UTF8,true)){
      int length=reader.ReadInt32();var request=JObject.Parse(Encoding.UTF8.GetString(reader.ReadBytes(length)));Need((string)request["operation"]=="ReadFrame"&&(string)request["payload"]["host"]==host.ToString("N")&&(string)request["payload"]["attempt"]==identity.Attempt.ToString("N")&&(long)request["payload"]["after"]==1,"frame-request-pinned-"+mode);
      var reply=Encoding.UTF8.GetBytes(new JObject{{"ok",true},{"host",(mode=="wrong-host"?Guid.NewGuid():host).ToString("N")},{"available",true}}.ToString());
      writer.Write(mode=="oversized"?RuntimeLimits.ManagerReplyBytes+1:reply.Length);if(mode=="oversized")return;writer.Write(reply);writer.Flush();
      if(mode=="wrong-host")return;
      var header=new byte[WorkerFrameCodec.HeaderBytes];WorkerFrameCodec.Encode(header,mode=="wrong-generation"?new WorkerIdentity(identity.Attempt,identity.Instance,8):identity,640,360,mode=="replay"?1:2);writer.Write(header);writer.Flush();
      if(mode=="wrong-generation"||mode=="replay")return;
      if(mode=="stall"||mode=="concurrent"||mode=="stop"){entered.Set();if(!release.Wait(5000))throw new TimeoutException("frame-test-release");if(mode=="stop"){writer.Write(new byte[640*360*4]);writer.Flush();}return;}
      writer.Write(new byte[]{17,29,31});writer.Flush();
     }}finally{server.Dispose();}
    });
    var client=new SupervisorManagerConnection(descriptor);var destination=new byte[640*360*4];destination[0]=93;destination[destination.Length-1]=71;
    RuntimeSession owner=null;Task<long> reading;
    if(mode=="stop"){
     var lease=new Lease(identity);owner=RuntimeSession.Start(identity.Instance,identity.Generation,()=>Task.FromResult<IRuntimeSessionLease>(lease),new string('a',64));owner.Running.GetAwaiter().GetResult();
     var method=typeof(SupervisorManagerConnection).GetMethod("ReadSessionFrame");Need(method!=null,"session-bound-frame-read-available");reading=(Task<long>)method.Invoke(client,new object[]{owner,640,360,1L,destination});
    }else reading=client.ReadFrame(identity,640,360,1,destination);
    var clock=System.Diagnostics.Stopwatch.StartNew();
    if(mode=="stop"){Need(entered.Wait(3000),"session-frame-payload-held-before-stop");owner.Stop();owner.Completion.GetAwaiter().GetResult();release.Set();}
    if(mode=="concurrent"){
     Need(entered.Wait(3000),"frame-first-read-in-flight");bool busy=false;try{client.ReadFrame(identity,640,360,1,destination).GetAwaiter().GetResult();}catch(InvalidOperationException error){busy=error.Message=="presenter-frame-read-pending";}Need(busy,"frame-concurrent-read-rejected");release.Set();
    }
    bool rejected=false;try{reading.GetAwaiter().GetResult();}catch(Exception error){
     rejected=mode=="stop"?error is InvalidOperationException&&error.Message=="presenter-session-ended":mode=="stall"?error is TimeoutException:mode=="partial"||mode=="concurrent"?error is EndOfStreamException:error is InvalidDataException;
     if(!rejected)Console.WriteLine("Unexpected frame error: "+error);
    }finally{release.Set();}
    Need(rejected&&destination[0]==93&&destination[destination.Length-1]==71,"frame-"+mode+"-rejected-without-partial-commit");
    if(mode=="stall")Need(clock.ElapsedMilliseconds<5000,"frame-payload-timeout-bounded");
    if(!serving.Wait(6000))throw new TimeoutException("frame-server-cleanup");serving.GetAwaiter().GetResult();
   }
  }
 }
}
