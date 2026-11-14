using System;
using System.Threading.Tasks;
using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Supervision {
 // Backend ownership adapter. It does not own the global Supervisor and never
 // closes the backend when a manager or preview session is closed.
 public static class SupervisedRuntimeSession {
  public static RuntimeSession Start(SupervisorRuntime runtime,LaunchRequest request){
   if(runtime==null||request==null)throw new ArgumentNullException();
   return RuntimeSession.Start(request.Instance,request.Generation,async()=>Own(await runtime.Start(request).ConfigureAwait(false)),request.BuildId);
  }
  internal static IRuntimeSessionLease Own(SupervisedSession session)=>new Lease(session);
  sealed class Lease:IRuntimeSessionLease {
   readonly SupervisedSession session;readonly Task terminal;
   internal Lease(SupervisedSession value){session=value;Identity=new WorkerIdentity(value.Attempt,value.Request.Instance,value.Request.Generation);terminal=ObserveCompletion();}
   public WorkerIdentity Identity{get;}
   public Task Running=>session.Ready;
   public Task Completion=>terminal;
   async Task ObserveCompletion(){await session.Completion.ConfigureAwait(false);if(session.Fault.HasValue)throw new InvalidOperationException("worker-fault:"+session.Fault.Value);}
   public async Task StopAsync(){
    session.Stop();await session.Completion.ConfigureAwait(false);
    if(session.CleanedAt<0||session.ExitedAt<0)throw new InvalidOperationException("session-cleanup-unproven");
   }
  }
 }
}
