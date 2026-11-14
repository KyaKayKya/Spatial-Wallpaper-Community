using System;using System.Threading.Tasks;using SpatialWallpaper.Sdk.Hosting;
static class RuntimeSessionChecks {
 static TaskCompletionSource<T> Promise<T>()=>new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
 static void Need(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 sealed class Lease:IRuntimeSessionLease {
  public WorkerIdentity Identity{get;set;}public readonly TaskCompletionSource<bool> Ready=Promise<bool>(),End=Promise<bool>();public TaskCompletionSource<bool> Cleanup=Promise<bool>();
  public Task Running=>Ready.Task;public Task Completion{get{System.Threading.Interlocked.Increment(ref CompletionReads);return End.Task;}}public int Stops,CompletionReads;
  public Lease(Guid instance,uint generation){Identity=new WorkerIdentity(Guid.NewGuid(),instance,generation);}
  public Task StopAsync(){System.Threading.Interlocked.Increment(ref Stops);return Cleanup.Task;}
 }
 static async Task Run(){
  var instance=Guid.NewGuid();var create=Promise<IRuntimeSessionLease>();var lease=new Lease(instance,1);
  var session=RuntimeSession.Start(instance,1,()=>create.Task);session.Stop();session.Stop();
  Need(!session.Completion.IsCompleted,"stop-before-start-retains-pending-creation");create.SetResult(lease);
  await Wait(()=>lease.Stops==1);Need(!session.Completion.IsCompleted,"late-created-resource-is-stopped-and-owned");
  lease.Ready.SetResult(true);Need(session.Running.IsCanceled,"late-running-cannot-resurrect-stopped-session");
  lease.Cleanup.SetResult(true);await Wait(()=>lease.CompletionReads>=2||session.Completion.IsCompleted);Need(!session.Completion.IsCompleted,"cleanup-return-without-resource-completion-is-not-released");lease.End.SetResult(true);await session.Completion;
  Need(session.ResourcesReleased&&lease.Stops==1&&session.State==RuntimeSessionState.Stopped,"stop-idempotent-complete-only-after-resource-end");
  instance=Guid.NewGuid();lease=new Lease(instance,2);session=RuntimeSession.Start(instance,2,()=>Task.FromResult<IRuntimeSessionLease>(lease));
  lease.Ready.SetResult(true);await session.Running;Need(session.State==RuntimeSessionState.Running&&!session.Completion.IsCompleted,"running-is-not-session-completion-or-presented");
  lease.End.SetException(new Exception("worker-failed"));await Wait(()=>lease.Stops==1);lease.Cleanup.SetResult(true);
  try{await session.Completion;throw new Exception("failure-lost");}catch(Exception e){Need(e.Message=="worker-failed"&&session.ResourcesReleased,"natural-fault-preserved-after-cleanup");}
  instance=Guid.NewGuid();lease=new Lease(instance,3);session=RuntimeSession.Start(instance,3,()=>Task.FromResult<IRuntimeSessionLease>(lease));session.Stop();await Wait(()=>lease.Stops==1);lease.Cleanup.SetException(new Exception("cleanup-failed"));
  try{await session.Completion;throw new Exception("cleanup-error-lost");}catch(Exception){Need(session.State==RuntimeSessionState.CleanupFailed&&!session.ResourcesReleased,"cleanup-failure-retains-resource-ownership");}
  Need(!session.ResourcesCompletion.IsCompleted,"cleanup-failure-does-not-complete-resource-release");
  lease.Cleanup=Promise<bool>();var retry=session.RetryCleanupAsync();var repeated=session.RetryCleanupAsync();Need(object.ReferenceEquals(retry,repeated),"concurrent-cleanup-retries-share-one-attempt");await Wait(()=>lease.Stops==2);
  lease.Cleanup.SetResult(true);lease.End.SetResult(true);await retry;await session.ResourcesCompletion;
  Need(session.ResourcesReleased&&session.Completion.IsFaulted&&session.CleanupError.Message=="cleanup-failed"&&lease.Stops==2,"retry-releases-resources-without-erasing-original-failure");
  instance=Guid.NewGuid();lease=new Lease(instance,6);lease.Ready.SetResult(true);lease.End.SetException(new Exception("already-ended"));session=RuntimeSession.Start(instance,6,()=>Task.FromResult<IRuntimeSessionLease>(lease));await Wait(()=>lease.Stops==1);lease.Cleanup.SetResult(true);
  try{await session.Completion;throw new Exception("simultaneous-fault-lost");}catch(Exception e){Need(e.Message=="already-ended"&&!session.Running.IsCompletedSuccessfully(),"simultaneous-ready-and-fault-never-publishes-running");}
  instance=Guid.NewGuid();lease=new Lease(Guid.NewGuid(),4);session=RuntimeSession.Start(instance,4,()=>Task.FromResult<IRuntimeSessionLease>(lease));await Wait(()=>lease.Stops==1);lease.Cleanup.SetResult(true);lease.End.SetResult(true);
  try{await session.Completion;throw new Exception("wrong-identity-accepted");}catch(Exception e){Need(e.Message=="session-created-identity"&&session.ResourcesReleased,"wrong-identity-is-owned-and-cleaned-before-failure");}
  session=RuntimeSession.Start(Guid.NewGuid(),5,()=>throw new Exception("factory-failed"));
  try{await session.Completion;throw new Exception("factory-failure-lost");}catch(Exception e){Need(e.Message=="factory-failed"&&session.ResourcesReleased,"synchronous-factory-failure-is-observed");}
  instance=Guid.NewGuid();lease=new Lease(instance,7);var lateIdentity=lease.Identity;lease.Identity=null;session=RuntimeSession.Start(instance,7,()=>Task.FromResult<IRuntimeSessionLease>(lease));
  await Wait(()=>lease.CompletionReads>0||lease.Stops>0);Need(lease.Stops==0&&!session.Running.IsCompleted,"reserved-remote-lease-owned-before-worker-identity");
  lease.Identity=lateIdentity;lease.Ready.SetResult(true);await session.Running;Need(session.Identity==lateIdentity,"running-requires-confirmed-remote-identity");session.Stop();await Wait(()=>lease.Stops==1);lease.Cleanup.SetResult(true);lease.End.SetResult(true);await session.Completion;
 }
 static bool IsCompletedSuccessfully(this Task task)=>task.Status==TaskStatus.RanToCompletion;
 static async Task Wait(Func<bool> condition){var watch=System.Diagnostics.Stopwatch.StartNew();while(!condition()){if(watch.ElapsedMilliseconds>3000)throw new TimeoutException();await Task.Delay(1);}}
 static int Main(){try{Run().GetAwaiter().GetResult();return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
