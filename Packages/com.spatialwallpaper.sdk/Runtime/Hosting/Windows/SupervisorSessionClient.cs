using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Sdk.Hosting {
 public static class SupervisorSessionClient {
  public static RuntimeSession Start(ISupervisorSessionTransport connection,Guid installation,string build,Guid instance,uint generation,string package,bool preview){
   if(connection==null||connection.Host==Guid.Empty)throw new ArgumentException("session-connection");
   return RuntimeSession.Start(instance,generation,async()=>{
    var reply=await connection.Call(preview?"ReservePreviewSession":"ReserveSession",new JObject{{"host",connection.Host.ToString("N")},{"installation",installation.ToString("N")},{"build",build},{"instance",instance.ToString("N")},{"generation",generation},{"intent","Manual"},{"package",package}}).ConfigureAwait(false);
    // A lost reservation reply can only leave an expiring reservation, never a
    // Worker. Once the token is known, return its owner before sending Start.
    var token=Token(reply);var lease=new Lease(connection,token,instance,generation);lease.Check(reply,false);lease.Begin();return (IRuntimeSessionLease)lease;
   },build);
  }
  static Guid Token(JObject reply){Guid token;if(reply==null||reply["token"]?.Type!=JTokenType.String||!Guid.TryParseExact((string)reply["token"],"N",out token)||token==Guid.Empty)throw new InvalidDataException("session-reservation-token");return token;}
  sealed class Lease:IRuntimeSessionLease {
   readonly ISupervisorSessionTransport connection;readonly Guid token,instance,host;readonly uint generation;
   readonly SemaphoreSlim io=new SemaphoreSlim(1,1);readonly object gate=new object();bool stopping;WorkerIdentity identity;
   readonly TaskCompletionSource<bool> ready=Promise(),ended=Promise();
   static TaskCompletionSource<bool> Promise()=>new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
   internal Lease(ISupervisorSessionTransport connection,Guid token,Guid instance,uint generation){this.connection=connection;host=connection.Host;this.token=token;this.instance=instance;this.generation=generation;}
   public WorkerIdentity Identity{get{lock(gate)return identity;}}public Task Running=>ready.Task;public Task Completion=>ended.Task;
   internal void Begin(){Task.Run(Pump);}
   internal string Check(JObject reply,bool allowAbsent){
    var names=new[]{"ok","host","token","state","resources_released","instance","generation","attempt","primary_error","cleanup_error"};
    if(reply==null||!reply.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(names.OrderBy(n=>n,StringComparer.Ordinal))||reply["ok"].Type!=JTokenType.Boolean||!(bool)reply["ok"]||reply["host"].Type!=JTokenType.String||(string)reply["host"]!=host.ToString("N")||reply["token"].Type!=JTokenType.String||(string)reply["token"]!=token.ToString("N")||reply["state"].Type!=JTokenType.String||reply["resources_released"].Type!=JTokenType.Boolean)throw new InvalidDataException("session-response-identity");
    string state=(string)reply["state"];bool released=(bool)reply["resources_released"];
    foreach(string error in new[]{"primary_error","cleanup_error"})if(reply[error].Type!=JTokenType.Null&&(reply[error].Type!=JTokenType.String||((string)reply[error]).Length>4096))throw new InvalidDataException("session-response-error");
    if(state=="Absent"){
     if(!allowAbsent||!released||reply["instance"].Type!=JTokenType.Null||reply["generation"].Type!=JTokenType.Null||reply["attempt"].Type!=JTokenType.Null)throw new InvalidDataException("session-unexpected-absence");return state;
    }
    if(!new[]{"Reserved","Starting","Running","Stopping","Stopped","Faulted","CleanupFailed"}.Contains(state)||reply["instance"].Type!=JTokenType.String||(string)reply["instance"]!=instance.ToString("N")||reply["generation"].Type!=JTokenType.Integer||(long)reply["generation"]!=generation||released&&(state!="Stopped"&&state!="Faulted"))throw new InvalidDataException("session-response-state");
    if(reply["attempt"].Type!=JTokenType.Null){Guid attempt;if(reply["attempt"].Type!=JTokenType.String||!Guid.TryParseExact((string)reply["attempt"],"N",out attempt)||attempt==Guid.Empty)throw new InvalidDataException("session-response-attempt");lock(gate){if(identity!=null&&identity.Attempt!=attempt)throw new InvalidDataException("session-attempt-changed");identity=new WorkerIdentity(attempt,instance,generation);}}
    if(state=="Running"&&Identity==null)throw new InvalidDataException("session-running-without-identity");return state;
   }
   async Task<JObject> Call(string operation){return await connection.Call(operation,new JObject{{"host",host.ToString("N")},{"token",token.ToString("N")}}).ConfigureAwait(false);}
   async Task Pump(){try{
    bool start=true;while(true){
     await io.WaitAsync().ConfigureAwait(false);try{
      lock(gate)if(stopping)return;
      var reply=await Call(start?"StartSession":"SessionStatus").ConfigureAwait(false);start=false;string state=Check(reply,false);
      lock(gate){if(stopping)return;if(state=="Running")ready.TrySetResult(true);}
      if(state=="CleanupFailed"||state=="Faulted")throw new InvalidOperationException("remote-session-fault:"+((string)reply["primary_error"]??(string)reply["cleanup_error"]??state));
      if((bool)reply["resources_released"]){ended.TrySetResult(true);return;}
     }finally{io.Release();}await Task.Delay(100).ConfigureAwait(false);
    }
   }catch(Exception error){ready.TrySetException(error);ended.TrySetException(error);}}
   public async Task StopAsync(){
    lock(gate){stopping=true;ready.TrySetCanceled();}
    var clock=Stopwatch.StartNew();bool sent=false,retried=false;
    while(clock.ElapsedMilliseconds<15000){
     await io.WaitAsync().ConfigureAwait(false);try{
      var reply=await Call(sent?"SessionStatus":"StopSession").ConfigureAwait(false);sent=true;string state=Check(reply,true);
      if((bool)reply["resources_released"]){
       if(state!="Absent"){var forgotten=await Call("ForgetSession").ConfigureAwait(false);if(Check(forgotten,true)!="Absent")throw new InvalidDataException("session-forget-unconfirmed");}
       ended.TrySetResult(true);return;
      }
      if(state=="CleanupFailed"&&!retried){Check(await Call("RetrySessionCleanup").ConfigureAwait(false),false);retried=true;}
     }finally{io.Release();}await Task.Delay(100).ConfigureAwait(false);
    }
    throw new TimeoutException("session-cleanup-unproven");
   }
  }
 }
}
