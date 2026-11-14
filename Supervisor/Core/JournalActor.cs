using System;using System.Threading;using System.Threading.Tasks;using System.Collections.Concurrent;
namespace SpatialWallpaper.Supervision {
 // Trusted persistence lane: one OS write at a time, finite pending work, no caller-thread continuations.
 // Close is nonblocking; Completion signals that accepted work and journal ownership have been released.
 public sealed class JournalActor {
  abstract class Work {internal abstract void Run(FaultJournal journal);internal abstract void Reject(Exception error);}
  sealed class Work<T>:Work {
   readonly Func<FaultJournal,T> action;internal readonly TaskCompletionSource<T> Result=new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
   internal Work(Func<FaultJournal,T> body){action=body;}
   internal override void Run(FaultJournal journal){
    try{Result.TrySetResult(action(journal));}
    catch(Exception error){Result.TrySetException(error);if(!journal.Available||!(error is ArgumentException||error is InvalidOperationException))throw;}
   }
   internal override void Reject(Exception error){Result.TrySetException(error);}
  }
  readonly BlockingCollection<Work> queue;readonly TaskCompletionSource<bool> completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  readonly string root;readonly Thread thread;int closed;
  readonly TaskCompletionSource<bool> initialized=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  public Task Initialized=>initialized.Task;
  public Task Completion=>completion.Task;
  public JournalActor(string directory,int pendingCapacity){
   if(string.IsNullOrEmpty(directory)||pendingCapacity<1||pendingCapacity>256)throw new ArgumentException("journal-actor-policy");
   root=directory;queue=new BlockingCollection<Work>(pendingCapacity);thread=new Thread(Run){IsBackground=true,Name="SpatialWallpaper persistence"};thread.Start();
  }
  Task<T> Submit<T>(Func<FaultJournal,T> action){
   var work=new Work<T>(action);
   try{if(Volatile.Read(ref closed)!=0||!queue.TryAdd(work))work.Reject(new InvalidOperationException("journal-actor-unavailable-or-full"));}
   catch(InvalidOperationException){work.Reject(new InvalidOperationException("journal-actor-closed"));}
   return work.Result.Task;
  }
  void Run(){
   Exception failure=null;
   try{using(var journal=new FaultJournal(root)){initialized.TrySetResult(true);foreach(var work in queue.GetConsumingEnumerable())work.Run(journal);}}
   catch(Exception error){failure=error;}
   finally{
    Interlocked.Exchange(ref closed,1);queue.CompleteAdding();Work pending;
    while(queue.TryTake(out pending))pending.Reject(failure??new InvalidOperationException("journal-actor-closed"));
    if(!initialized.Task.IsCompleted)initialized.TrySetException(failure??new InvalidOperationException("journal-not-initialized"));
    if(failure==null)completion.TrySetResult(true);else completion.TrySetException(failure);
   }
  }
  public void Close(){if(Interlocked.Exchange(ref closed,1)==0)queue.CompleteAdding();}
  public Task<AttemptReceipt> Begin(Guid install,string build,Guid instance,uint generation,StartIntent intent)=>Submit(j=>j.Begin(install,build,instance,generation,intent));
  public Task<bool> RecordProcess(AttemptReceipt receipt,int pid)=>Submit(j=>{j.RecordProcess(receipt,pid);return true;});
  public Task<bool> MarkRunning(AttemptReceipt receipt)=>Submit(j=>j.MarkRunning(receipt));
  public Task<bool> MarkStopping(AttemptReceipt receipt)=>Submit(j=>j.MarkStopping(receipt));
  public Task<bool> Complete(AttemptReceipt receipt)=>Submit(j=>{j.Complete(receipt);return true;});
  public Task<bool> Fail(AttemptReceipt receipt,FaultReason reason)=>Submit(j=>j.Fail(receipt,reason));
  public Task<bool> RecordDiagnostic(AttemptReceipt receipt,string diagnostic)=>Submit(j=>j.RecordDiagnostic(receipt,diagnostic));
  public Task<Newtonsoft.Json.Linq.JObject> Diagnostic(Guid attempt)=>Submit(j=>j.Diagnostic(attempt));
  public Task<bool> IsQuarantined(Guid install,string build)=>Submit(j=>j.IsQuarantined(install,build));
  public Task<RollbackTicket> ReserveRollback(Guid install,string failed,string healthy)=>Submit(j=>j.ReserveRollback(install,failed,healthy));
  public Task<AttemptReceipt> BeginRollback(RollbackTicket ticket,Guid instance,uint generation)=>Submit(j=>j.BeginRollback(ticket,instance,generation));
 }
}
