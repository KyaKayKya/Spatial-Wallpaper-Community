using System;
using System.Threading.Tasks;
namespace SpatialWallpaper.Sdk.Hosting {
 // Trusted host adapters only. A factory owns partially created resources until
 // it returns a lease, including when the caller has already requested Stop.
 public interface IRuntimeSessionLease {
  // A reserved remote lease can precede Worker creation. Identity may be null
  // until Running, but must then be stable and match the requested generation.
  WorkerIdentity Identity{get;}
  Task Running{get;}
  Task Completion{get;}
  // Completes only after owned input, presentation and process cleanup. A fault
  // means cleanup is unproven; the session retains the lease and reports it.
  Task StopAsync();
 }
 public enum RuntimeSessionState {Starting,Running,Stopping,Stopped,Faulted,CleanupFailed}
 public sealed class RuntimeSessionSnapshot {
  public WorkerIdentity Identity{get;}public RuntimeSessionState State{get;}public bool ResourcesReleased{get;}
  public Exception PrimaryError{get;}public Exception CleanupError{get;}
  internal RuntimeSessionSnapshot(WorkerIdentity identity,RuntimeSessionState state,bool released,Exception primary,Exception cleanup){Identity=identity;State=state;ResourcesReleased=released;PrimaryError=primary;CleanupError=cleanup;}
 }
 public sealed class RuntimeSession {
  readonly object gate=new object();readonly Guid instance;readonly uint generation;
  readonly TaskCompletionSource<bool> running=Promise(),completion=Promise(),stop=Promise(),resourcesCompletion=Promise();
  RuntimeSessionState state=RuntimeSessionState.Starting;bool stopRequested,released;IRuntimeSessionLease owned;
  Exception primaryError,cleanupError;Task retryCleanup;WorkerIdentity identity;
  static TaskCompletionSource<bool> Promise()=>new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  public string BuildId{get;}
  RuntimeSession(Guid id,uint version,string build){if(id==Guid.Empty||version==0)throw new ArgumentException("session-request-identity");if(build!=null){if(build.Length!=64)throw new ArgumentException("session-build-identity");foreach(char c in build)if(!(c>='0'&&c<='9'||c>='a'&&c<='f'))throw new ArgumentException("session-build-identity");}instance=id;generation=version;BuildId=build;}
  public static RuntimeSession Start(Guid instance,uint generation,Func<Task<IRuntimeSessionLease>> create,string buildId=null){
   if(create==null)throw new ArgumentNullException(nameof(create));var session=new RuntimeSession(instance,generation,buildId);
   // Publish the owner before invoking adapter code, including synchronous code.
   Task.Run(()=>session.Run(create));return session;
  }
  public Task Running=>running.Task;
  public RuntimeSessionSnapshot Snapshot(){lock(gate)return new RuntimeSessionSnapshot(identity,state,released,primaryError,cleanupError);}
  public WorkerIdentity Identity{get{lock(gate)return identity;}}
  public Task Completion=>completion.Task;
  // Unlike Completion, this stays pending after failed cleanup. The caller must
  // retain the session and explicitly retry before allowing a replacement owner.
  public Task ResourcesCompletion=>resourcesCompletion.Task;
  public RuntimeSessionState State{get{lock(gate)return state;}}
  public bool ResourcesReleased{get{lock(gate)return released;}}
  public Exception PrimaryError{get{lock(gate)return primaryError;}}
  public Exception CleanupError{get{lock(gate)return cleanupError;}}
  // Serializes the final local use of a frame with Stop. Transport waits and
  // GPU/IO scheduling must happen outside this small synchronous commit.
  public bool TryUseRunning(WorkerIdentity expected,Action commit){
   if(expected==null||commit==null)throw new ArgumentNullException();lock(gate){
    if(stopRequested||state!=RuntimeSessionState.Running||identity==null||identity.Attempt!=expected.Attempt||identity.Instance!=expected.Instance||identity.Generation!=expected.Generation)return false;
    commit();return !stopRequested&&state==RuntimeSessionState.Running;
   }
  }
  public void Stop(){lock(gate){if(stopRequested||completion.Task.IsCompleted)return;stopRequested=true;state=RuntimeSessionState.Stopping;running.TrySetCanceled();stop.TrySetResult(true);}}
  public Task RetryCleanupAsync(){lock(gate){
   if(released)return Task.CompletedTask;
   if(!completion.Task.IsCompleted||owned==null)throw new InvalidOperationException("session-cleanup-not-retryable");
   if(retryCleanup!=null&&!retryCleanup.IsCompleted)return retryCleanup;
   state=RuntimeSessionState.Stopping;return retryCleanup=Task.Run(RetryCleanup);
  }}
  async Task RetryCleanup(){
   IRuntimeSessionLease lease;lock(gate)lease=owned;
   try{
    await lease.StopAsync().ConfigureAwait(false);
    try{await lease.Completion.ConfigureAwait(false);}catch(Exception error){lock(gate)if(primaryError==null)primaryError=error;}
    lock(gate){owned=null;released=true;state=RuntimeSessionState.Faulted;resourcesCompletion.TrySetResult(true);}
   }catch{lock(gate)state=RuntimeSessionState.CleanupFailed;throw;}
  }
  async Task Run(Func<Task<IRuntimeSessionLease>> create){
   IRuntimeSessionLease lease=null;Exception failure=null,cleanup=null;
   try{
    lease=await create().ConfigureAwait(false);if(lease==null)throw new InvalidOperationException("session-created-null");
    lock(gate)owned=lease;
    var createdIdentity=lease.Identity;
    if(createdIdentity!=null&&(createdIdentity.Instance!=instance||createdIdentity.Generation!=generation))throw new InvalidOperationException("session-created-identity");
    lock(gate)identity=createdIdentity;
    Task ready=lease.Running,ended=lease.Completion;if(ready==null||ended==null)throw new InvalidOperationException("session-created-tasks");
    var first=await Task.WhenAny(ready,ended,stop.Task).ConfigureAwait(false);
    bool stopping;lock(gate)stopping=stopRequested;
    if(!stopping){
     if(ended.IsCompleted){await ended.ConfigureAwait(false);throw new InvalidOperationException("session-ended-before-running");}
     await ready.ConfigureAwait(false);
     if(ended.IsCompleted){await ended.ConfigureAwait(false);throw new InvalidOperationException("session-ended-before-running");}
     var readyIdentity=lease.Identity;
     if(readyIdentity==null||readyIdentity.Instance!=instance||readyIdentity.Generation!=generation||(createdIdentity!=null&&readyIdentity.Attempt!=createdIdentity.Attempt))throw new InvalidOperationException("session-created-identity");
     lock(gate)identity=readyIdentity;
     lock(gate){if(!stopRequested){state=RuntimeSessionState.Running;running.TrySetResult(true);}}
     first=await Task.WhenAny(ended,stop.Task).ConfigureAwait(false);
     if(first==ended)await ended.ConfigureAwait(false);
    }
   }catch(Exception error){failure=error;}
   finally{
    lock(gate){primaryError=failure;if(state!=RuntimeSessionState.Stopping)state=RuntimeSessionState.Stopping;}
    if(lease!=null){
     try{
      await lease.StopAsync().ConfigureAwait(false);
      try{await lease.Completion.ConfigureAwait(false);}catch(Exception error){if(failure==null)failure=error;}
     }catch(Exception error){cleanup=error;}
    }
    lock(gate){
     if(cleanup==null){released=true;owned=null;}
     primaryError=failure;cleanupError=cleanup;
     if(!running.Task.IsCompleted){if(failure!=null)running.TrySetException(failure);else running.TrySetCanceled();}
     state=cleanup!=null?RuntimeSessionState.CleanupFailed:failure!=null?RuntimeSessionState.Faulted:RuntimeSessionState.Stopped;
     if(released)resourcesCompletion.TrySetResult(true);
     if(cleanup!=null&&failure!=null)completion.TrySetException(new AggregateException(failure,cleanup));
     else if(cleanup!=null||failure!=null)completion.TrySetException(cleanup??failure);
     else completion.TrySetResult(true);
    }
   }
  }
 }
}
