using System;using SpatialWallpaper.Sdk.Hosting;using System.Collections.Generic;using System.Threading;using System.Threading.Tasks;using System.Collections.Concurrent;
namespace SpatialWallpaper.Supervision {
 public class AdmissionFailureException:System.IO.IOException {public AdmissionFailureException(string code):base(code){}}
 public sealed class LaunchFailureException:InvalidOperationException {
  public Guid Attempt{get;}public FaultReason Reason{get;}
  internal LaunchFailureException(AttemptReceipt receipt,FaultReason reason,Exception cause):base("worker-launch-failed",cause){Attempt=receipt.Attempt;Reason=reason;}
 }
 // Only the trusted installation/import layer constructs launch requests. Never deserialize this from a Worker.
 public sealed class LaunchRequest {
  public Guid Installation{get;}public string BuildId{get;}public Guid Instance{get;}public uint Generation{get;}public StartIntent Intent{get;}public string RuntimePath{get;}public long ReservedProcessBytes{get;}
  public bool Preview{get;}
  public LaunchRequest(Guid installation,string build,Guid instance,uint generation,StartIntent intent,string runtimePath,long reservedProcessBytes,bool preview=false){
   if(installation==Guid.Empty||instance==Guid.Empty||generation==0||build==null||build.Length!=64||string.IsNullOrEmpty(runtimePath)||runtimePath.Length>32700||reservedProcessBytes<1||reservedProcessBytes>4294967296L||!Enum.IsDefined(typeof(StartIntent),intent))throw new ArgumentException("trusted-launch-request");
   foreach(char c in build)if(!(c>='0'&&c<='9'||c>='a'&&c<='f'))throw new ArgumentException("canonical-build-digest");
   Installation=installation;BuildId=build;Instance=instance;Generation=generation;Intent=intent;RuntimePath=runtimePath;ReservedProcessBytes=reservedProcessBytes;Preview=preview;
  }
 }
 public readonly struct WorkerSample {
  public readonly long StopPublishedAt;public readonly uint? ExitCode;public readonly bool Alive,ProtocolFault;public readonly long PrivateBytes,CpuMilliseconds,Sequence,ReceivedAt,GpuBytes,GpuSampleAt;
  public WorkerSample(bool alive,long memory,long cpu,long sequence,long receivedAt,bool protocolFault,long gpuBytes=-1,long gpuSampleAt=0,uint? exitCode=null,long stopPublishedAt=-1){StopPublishedAt=stopPublishedAt;ExitCode=exitCode;Alive=alive;PrivateBytes=memory;CpuMilliseconds=cpu;Sequence=sequence;ReceivedAt=receivedAt;ProtocolFault=protocolFault;GpuBytes=gpuBytes;GpuSampleAt=gpuSampleAt;}
 }
 public interface IWorkerDiagnostics {string ReadDiagnostic();}
 public interface IWorkerProcessIdentity {long CreationFileTime{get;}}
 public interface IManagedWorker:IDisposable {int ProcessId{get;}WorkerSample Sample();void RequestStop();void Kill();}
 public interface IWorkerLauncher {IManagedWorker Start(LaunchRequest request,AttemptReceipt receipt,Action<int> created);}
 public sealed class SupervisedSession {
  readonly TaskCompletionSource<bool> ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  readonly object stateGate=new object();int stop,fault;internal readonly IManagedWorker Worker;internal readonly AttemptReceipt Receipt;internal readonly LaunchRequest Request;
  long stopAccepted=-1,stopPublished=-1,forcedAt=-1,exitedAt=-1,cleanedAt=-1;int graceful,forcedAlive;uint? observedExitCode;
  public long StopAcceptedAt=>Interlocked.Read(ref stopAccepted);public long ForcedAt=>Interlocked.Read(ref forcedAt);public long ExitedAt=>Interlocked.Read(ref exitedAt);public long CleanedAt=>Interlocked.Read(ref cleanedAt);
  public bool GracefulStop=>Volatile.Read(ref graceful)!=0;
  public Newtonsoft.Json.Linq.JObject StopEvidence {get{long request=StopAcceptedAt,published=Interlocked.Read(ref stopPublished),forced=ForcedAt,exit=ExitedAt,cleanup=CleanedAt;Func<long,Newtonsoft.Json.Linq.JToken> value=n=>n<0?Newtonsoft.Json.Linq.JValue.CreateNull():new Newtonsoft.Json.Linq.JValue(n);return new Newtonsoft.Json.Linq.JObject{{"timing_clock","host-monotonic-ms"},{"stop_requested_at_ms",value(request)},{"stop_published_at_ms",value(published)},{"forced_started_at_ms",value(forced)},{"worker_kernel_exited_at_ms",value(exit)},{"cleanup_completed_at_ms",value(cleanup)},{"graceful_stop",GracefulStop},{"forced_stop",forced>=0},{"forced_alive_observed",Volatile.Read(ref forcedAlive)!=0},{"stop_reason",GracefulStop?"requested-stop":Fault?.ToString()??"not-stopped"},{"observed_exit_code",observedExitCode.HasValue?new Newtonsoft.Json.Linq.JValue(observedExitCode.Value):Newtonsoft.Json.Linq.JValue.CreateNull()},{"cleanup_completed",cleanup>=0},{"normal_stop_to_kernel_exit_ms",GracefulStop?value(exit-request):null},{"forced_to_kernel_exit_ms",forced>=0&&exit>=forced?value(exit-forced):null},{"kernel_exit_to_cleanup_complete_ms",exit>=0&&cleanup>=exit?value(cleanup-exit):null}};}}
  internal void Observe(WorkerSample sample){if(sample.StopPublishedAt>=0)Interlocked.Exchange(ref stopPublished,sample.StopPublishedAt);if(!sample.Alive)observedExitCode=sample.ExitCode;}
  internal void AcceptStop(long at)=>Interlocked.Exchange(ref stopAccepted,at);
  internal void Forced(long at,bool alive=false){if(Interlocked.CompareExchange(ref forcedAt,at,-1)==-1&&alive)Volatile.Write(ref forcedAlive,1);}
  internal void Exited(long at,bool normal){Interlocked.Exchange(ref exitedAt,at);if(normal)Volatile.Write(ref graceful,1);}
  internal void Cleaned(long at)=>Interlocked.Exchange(ref cleanedAt,at);
  internal SupervisedSession(LaunchRequest request,AttemptReceipt receipt,IManagedWorker worker){Request=request;Receipt=receipt;Worker=worker;}
  public Task Ready=>ready.Task;public Task Completion=>completion.Task;public Guid Attempt=>Receipt.Attempt;
  public FaultReason? Fault=>Volatile.Read(ref fault)==0?(FaultReason?)null:(FaultReason)(Volatile.Read(ref fault)-1);
  public void Stop(){lock(stateGate)Interlocked.Exchange(ref stop,1);}
  internal bool StopRequested=>Volatile.Read(ref stop)!=0;
  internal void Running(){lock(stateGate){if(!StopRequested)ready.TrySetResult(true);}}
  internal void Failed(FaultReason reason){Interlocked.Exchange(ref fault,1+(int)reason);ready.TrySetException(new InvalidOperationException("worker-fault:"+reason));}
  internal void Complete(Exception error){if(!ready.Task.IsCompleted)ready.TrySetException(new InvalidOperationException("worker-stopped-before-ready"));if(error==null)completion.TrySetResult(true);else completion.TrySetException(error);}
 }
 // Launch and cleanup may perform bounded OS/disk waits; no monitor or caller executes that work.
 // One monitor per Worker. Neither a blocked launch nor a blocked native call can hold another monitor.
 public sealed class SupervisorRuntime {
  sealed class StartWork {internal LaunchRequest Request;internal string FailedBuild;internal Func<Action> Inspect;internal Action<Exception> InspectionFailed;internal readonly TaskCompletionSource<SupervisedSession> Result=new TaskCompletionSource<SupervisedSession>(TaskCreationOptions.RunContinuationsAsynchronously);}
  sealed class Cleanup {internal SupervisedSession Session;internal Task Journal;}
  readonly object gate=new object();readonly JournalActor journal;readonly IWorkerLauncher launcher;readonly WatchdogPolicy policy;readonly Func<long> now;
  readonly BlockingCollection<StartWork> launches;readonly BlockingCollection<Cleanup> cleanups;readonly HashSet<SupervisedSession> sessions=new HashSet<SupervisedSession>();
  readonly Dictionary<Guid,int> installationReservations=new Dictionary<Guid,int>();
  readonly CleanupFailures cleanupFailures;readonly int capacity;readonly long memoryCapacity;int reserved;long memory;bool closed,launchClosed;
  readonly TaskCompletionSource<bool> completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  public Task Completion=>completion.Task;
  public SupervisorRuntime(JournalActor persistence,IWorkerLauncher platform,WatchdogPolicy limits,int maximumInstances,long processMemoryCapacity,Func<long> monotonicMilliseconds,CleanupFailures cleanup=null){
   if(persistence==null||platform==null||limits==null||monotonicMilliseconds==null||maximumInstances<1||maximumInstances>64||processMemoryCapacity<limits.PrivateBytes)throw new ArgumentException("supervisor-policy");
   cleanupFailures=cleanup??new CleanupFailures();journal=persistence;launcher=platform;policy=limits;capacity=maximumInstances;memoryCapacity=processMemoryCapacity;now=monotonicMilliseconds;
   launches=new BlockingCollection<StartWork>(capacity);cleanups=new BlockingCollection<Cleanup>(capacity);
   new Thread(LaunchLoop){IsBackground=true,Name="SpatialWallpaper launch"}.Start();new Thread(CleanupLoop){IsBackground=true,Name="SpatialWallpaper cleanup"}.Start();
  }
  public Task<SupervisedSession> Start(LaunchRequest request)=>Submit(request,null);
  public bool CanUninstall(Guid installation){lock(gate)return installation!=Guid.Empty&&!closed&&!cleanupFailures.Failed&&!installationReservations.ContainsKey(installation);}
  // Trusted admission-only work uses the same bounded launch lane and a full
  // process reservation. Its owner must return only after its kernel cleanup.
  public Task<T> Inspect<T>(Func<T> operation){
   if(operation==null)throw new ArgumentNullException(nameof(operation));var result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
   var work=new StartWork{Inspect=()=>{T value=operation();return ()=>result.TrySetResult(value);},InspectionFailed=error=>result.TrySetException(error)};
   lock(gate){
    if(closed||cleanupFailures.Failed||reserved>=capacity||policy.PrivateBytes>memoryCapacity-memory){result.TrySetException(new InvalidOperationException("supervisor-unavailable-or-capacity"));return result.Task;}
    reserved++;memory+=policy.PrivateBytes;
    if(!launches.TryAdd(work)){reserved--;memory-=policy.PrivateBytes;result.TrySetException(new InvalidOperationException("launch-queue-full"));}
   }return result.Task;
  }
  public Task<SupervisedSession> Restore(LaunchRequest healthyRevision,string failedBuild){if(string.IsNullOrEmpty(failedBuild))throw new ArgumentException("failed-build");return Submit(healthyRevision,failedBuild);}
  Task<SupervisedSession> Submit(LaunchRequest request,string failedBuild){
   if(request==null)throw new ArgumentNullException(nameof(request));var work=new StartWork{Request=request,FailedBuild=failedBuild};
   lock(gate){
    if(closed||cleanupFailures.Failed||reserved>=capacity||request.ReservedProcessBytes<policy.PrivateBytes||request.ReservedProcessBytes>memoryCapacity-memory){work.Result.TrySetException(new InvalidOperationException("supervisor-unavailable-or-capacity"));return work.Result.Task;}
    reserved++;memory+=request.ReservedProcessBytes;
    installationReservations.TryGetValue(request.Installation,out var count);installationReservations[request.Installation]=count+1;
    if(!launches.TryAdd(work)){Release(request);work.Result.TrySetException(new InvalidOperationException("launch-queue-full"));}
   }
   return work.Result.Task;
  }
  static T Await<T>(Task<T> task){if(!task.Wait(5000))throw new TimeoutException("journal-deadline");return task.GetAwaiter().GetResult();}
  void Release(LaunchRequest request){lock(gate){int remaining=installationReservations[request.Installation]-1;if(remaining==0)installationReservations.Remove(request.Installation);else installationReservations[request.Installation]=remaining;Release(request.ReservedProcessBytes);}}
  void Release(long bytes){lock(gate){reserved--;memory-=bytes;if(closed&&launchClosed&&reserved==0)cleanups.CompleteAdding();}}
  void LaunchLoop(){
   try{foreach(var work in launches.GetConsumingEnumerable()){
    if(work.Inspect!=null){Action publish;try{lock(gate)if(closed||cleanupFailures.Failed)throw new InvalidOperationException("supervisor-closing-or-cleanup-failed");publish=work.Inspect();}catch(Exception error){publish=()=>work.InspectionFailed(error);}finally{Release(policy.PrivateBytes);}publish();continue;}
    IManagedWorker worker=null;AttemptReceipt receipt=null;
    try{
     lock(gate)if(closed||cleanupFailures.Failed)throw new InvalidOperationException("supervisor-closing-or-cleanup-failed");
     if(work.FailedBuild==null)receipt=Await(journal.Begin(work.Request.Installation,work.Request.BuildId,work.Request.Instance,work.Request.Generation,work.Request.Intent));
     else{
      var ticket=Await(journal.ReserveRollback(work.Request.Installation,work.FailedBuild,work.Request.BuildId));if(ticket==null)throw new InvalidOperationException("rollback-not-available");
      receipt=Await(journal.BeginRollback(ticket,work.Request.Instance,work.Request.Generation));
     }
     worker=launcher.Start(work.Request,receipt,pid=>Await(journal.RecordProcess(receipt,pid)));
     if(worker==null)throw new InvalidOperationException("missing-managed-worker");
     var session=new SupervisedSession(work.Request,receipt,worker);
     lock(gate){sessions.Add(session);if(closed)session.Stop();}
     new Thread(()=>Monitor(session)){IsBackground=true,Name="SpatialWallpaper monitor"}.Start();work.Result.TrySetResult(session);
    }catch(Exception error){
     // The platform launcher owns/terminates partial launches before throwing.
     Exception failure=error.GetBaseException();
     try{worker?.Kill();}catch(Exception cleanupError){cleanupFailures.Record(cleanupError);}
     try{worker?.Dispose();}catch(Exception cleanupError){cleanupFailures.Record(cleanupError);}
     try{if(receipt!=null){var reason=failure is AdmissionFailureException?FaultReason.Admission:FaultReason.Monitoring;Await(journal.Fail(receipt,reason));Await(journal.RecordDiagnostic(receipt,new Newtonsoft.Json.Linq.JObject{{"source","trusted-launch-transaction"},{"worker_reported_reason",reason==FaultReason.Admission?"admission-rejected":"launch-failed"},{"log_tail",""}}.ToString(Newtonsoft.Json.Formatting.None)));failure=new LaunchFailureException(receipt,reason,failure);}}
     catch(Exception persistenceError){failure=new AggregateException(failure,persistenceError);}
     Release(work.Request);work.Result.TrySetException(failure);
    }
   }}finally{lock(gate){launchClosed=true;if(closed&&reserved==0)cleanups.CompleteAdding();}}
  }
  void Monitor(SupervisedSession session){
   var guard=new WorkerWatchdog(policy,now());Task<bool> readyWrite=null;Task terminalWrite=null;bool kill=false,stopping=false;long lastSequence=0;
   try{while(true){
    var sample=session.Worker.Sample();session.Observe(sample);long at=now();GuardAction action=GuardAction.None;FaultReason? stopExitFault=null;
    if(session.StopRequested&&!stopping&&!kill&&sample.Alive){stopping=true;session.AcceptStop(at);guard.RequestStop(at);journal.MarkStopping(session.Receipt);session.Worker.RequestStop();}
    if(!kill&&!stopping&&sample.Sequence>lastSequence){lastSequence=sample.Sequence;action=guard.CompletedFrame(at,sample.Sequence,sample.ReceivedAt);if(action==GuardAction.Ready)readyWrite=journal.MarkRunning(session.Receipt);}
    if(readyWrite!=null&&readyWrite.IsCompleted){if(readyWrite.Status==TaskStatus.RanToCompletion&&readyWrite.Result){if(!stopping)session.Running();}else action=GuardAction.Kill;readyWrite=null;}
    if(sample.ProtocolFault)action=GuardAction.Kill;
    if(action!=GuardAction.Kill)action=guard.Poll(at,sample.Alive,sample.PrivateBytes,sample.CpuMilliseconds,sample.GpuBytes,sample.GpuSampleAt);
    // Exit during Stopping is normal only with a successful OS exit within the original grace.
    if(stopping&&!sample.Alive&&(sample.ExitCode!=0||sample.StopPublishedAt<session.StopAcceptedAt||sample.StopPublishedAt>at||at-session.StopAcceptedAt>policy.StopMilliseconds)){action=GuardAction.Kill;stopExitFault=at-session.StopAcceptedAt>policy.StopMilliseconds?FaultReason.StopTimeout:FaultReason.ProcessExit;}
    if(action==GuardAction.Kill&&!kill){kill=true;var reason=stopExitFault??guard.Reason??(sample.ProtocolFault?FaultReason.Protocol:FaultReason.Monitoring);if(reason==FaultReason.ProcessExit&&sample.ExitCode.HasValue){switch(sample.ExitCode.Value){case WorkerExitCodes.Admission:reason=FaultReason.Admission;break;case WorkerExitCodes.Script:reason=FaultReason.ScriptFault;break;case WorkerExitCodes.ExecutionBudget:case WorkerExitCodes.ResourceBudget:reason=FaultReason.Budget;break;case WorkerExitCodes.Render:reason=FaultReason.DeviceLost;break;}}session.Failed(reason);terminalWrite=journal.Fail(session.Receipt,reason);if(sample.Alive){session.Forced(at,true);session.Worker.Kill();}}
    if(!sample.Alive){session.Exited(at,stopping&&!kill&&sample.ExitCode==0);if(!kill)terminalWrite=journal.Complete(session.Receipt);break;}
    Thread.Sleep(2);
   }}catch{session.Forced(now());session.Failed(FaultReason.Monitoring);terminalWrite=journal.Fail(session.Receipt,FaultReason.Monitoring);try{session.Worker.Kill();}catch{}}
   // A failed query is not evidence of exit. Keep reservation and ownership until the kernel says exited.
   while(true){try{var finalSample=session.Worker.Sample();session.Observe(finalSample);if(!finalSample.Alive){if(session.ExitedAt<0)session.Exited(now(),false);break;}session.Forced(now(),true);session.Worker.Kill();}catch{}Thread.Sleep(10);}
   cleanups.Add(new Cleanup{Session=session,Journal=terminalWrite});
  }
  void CleanupLoop(){
   try{foreach(var cleanup in cleanups.GetConsumingEnumerable()){
    Exception error=null;try{cleanup.Session.Worker.Dispose();if(cleanup.Journal==null||!cleanup.Journal.Wait(5000))throw new TimeoutException("terminal-journal-deadline");cleanup.Journal.GetAwaiter().GetResult();if(cleanup.Session.Worker is IWorkerDiagnostics diagnostics){var write=journal.RecordDiagnostic(cleanup.Session.Receipt,diagnostics.ReadDiagnostic());if(!write.Wait(5000))throw new TimeoutException("diagnostic-journal-deadline");write.GetAwaiter().GetResult();}}catch(Exception caught){error=caught.GetBaseException();}
     if(error==null)cleanup.Session.Cleaned(now());else cleanupFailures.Record(error);lock(gate)sessions.Remove(cleanup.Session);Release(cleanup.Session.Request);cleanup.Session.Complete(error);
    }}catch(Exception error){cleanupFailures.Record(error);}
    finally{var failure=cleanupFailures.Failure;if(failure==null)completion.TrySetResult(true);else completion.TrySetException(failure);}
  }
  public void Close(){lock(gate){if(closed)return;closed=true;foreach(var session in sessions)session.Stop();launches.CompleteAdding();if(launchClosed&&reserved==0)cleanups.CompleteAdding();}}
 }
}
