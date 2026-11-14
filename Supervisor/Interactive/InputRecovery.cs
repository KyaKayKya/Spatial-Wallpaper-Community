using System;using System.Threading;using System.Threading.Tasks;
namespace SpatialWallpaper.Supervision.Interactive {
 public enum InputRecoveryState { Backoff,Connecting,WaitingForReady,Ready,Draining,Faulted,Closed }
 internal interface IInputRecoveryLease:IDisposable {Task Completion{get;}string Failure{get;}bool Released{get;}}
 internal interface IInputRecoverySource:IInputRecoveryLease {bool Ready{get;}bool Focused{get;}}
 // UI-thread coordinator. Tick must continue during close, including while a
 // consumed mouse-down waits for its matching up. No native I/O occurs here.
 internal sealed class InputRecovery:IDisposable {
  readonly int thread=Thread.CurrentThread.ManagedThreadId;readonly Func<bool> available;readonly Func<long> clock;readonly Func<CancellationToken,Task<IInputRecoveryLease>> connect;readonly Func<IInputRecoveryLease,IInputRecoverySource> create;
  readonly TaskCompletionSource<bool> completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  CancellationTokenSource cancellation;Task<IInputRecoveryLease> opening;IInputRecoveryLease channel;IInputRecoverySource source;
  long due;bool closing,terminal,sourceDisposed,channelDisposed,constructionUnknown,cleanupRetryRequested;Exception cleanupError;int attempts;
  public InputRecoveryState State{get;private set;}=InputRecoveryState.Backoff;
  public string Failure{get;private set;}public Exception LastCleanupError{get;private set;}public Task Completion=>completion.Task;
  public bool ResourcesReleased=>opening==null&&source==null&&channel==null&&!constructionUnknown;
  public bool Ready=>State==InputRecoveryState.Ready&&source!=null&&source.Failure==null&&channel.Failure==null&&source.Ready;
  public bool Focused=>Ready&&source.Focused;
  public InputRecovery(Func<long> clock,Func<CancellationToken,Task<IInputRecoveryLease>> connect,Func<IInputRecoveryLease,IInputRecoverySource> create,Func<bool> available=null){this.available=available??(()=>true);this.clock=clock??throw new ArgumentNullException(nameof(clock));this.connect=connect??throw new ArgumentNullException(nameof(connect));this.create=create??throw new ArgumentNullException(nameof(create));}
  void Owner(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("input-recovery-owning-ui-thread");}
  void CleanupFailed(Exception error){if(cleanupError==null)cleanupError=error;LastCleanupError=cleanupError;terminal=true;}
  void ObserveCleanup(){foreach(var lease in new IInputRecoveryLease[]{source,channel})if(lease!=null){if(lease.Completion.IsFaulted)CleanupFailed(lease.Completion.Exception);else if(lease.Completion.IsCanceled)CleanupFailed(new InvalidOperationException("input-recovery-cleanup-cancelled"));}}
  void Drain(string reason,bool stop=false){Failure=reason;terminal|=stop;State=InputRecoveryState.Draining;due=clock()+3000;
   try{cancellation?.Cancel();}catch(Exception error){CleanupFailed(error);}
   DisposeResources();
  }
  void DisposeResources(){
   if(source!=null&&!sourceDisposed){sourceDisposed=true;try{source.Dispose();}catch(Exception error){CleanupFailed(error);}}
   if(channel!=null&&!channelDisposed){channelDisposed=true;try{channel.Dispose();}catch(Exception error){CleanupFailed(error);}}
  }
  void Finish(){cancellation?.Dispose();cancellation=null;if(cleanupError!=null){LastCleanupError=cleanupError;Failure="cleanup-"+cleanupError.GetType().Name;State=InputRecoveryState.Faulted;if(closing)completion.TrySetException(cleanupError);return;}
   source=null;channel=null;sourceDisposed=channelDisposed=false;
   if(closing){State=InputRecoveryState.Closed;completion.TrySetResult(true);}else if(terminal||attempts>=3)State=InputRecoveryState.Faulted;else{State=InputRecoveryState.Backoff;due=clock()+250;}
  }
  public void Tick(){Owner();if(!closing&&!available())Dispose();
   if(cleanupRetryRequested){ObserveCleanup();CollectRetriedResources();}
   if(State==InputRecoveryState.Closed||State==InputRecoveryState.Faulted)return;
   if(State==InputRecoveryState.Backoff){if(clock()<due)return;attempts++;cancellation=new CancellationTokenSource();due=clock()+5000;State=InputRecoveryState.Connecting;try{opening=connect(cancellation.Token)??throw new InvalidOperationException("input-recovery-null-open-task");}catch(Exception error){opening=Task.FromException<IInputRecoveryLease>(error);}return;}
   if(State==InputRecoveryState.Connecting){
    if(clock()>=due){Drain("connect-timeout");return;}if(!opening.IsCompleted)return;
    try{channel=opening.GetAwaiter().GetResult()??throw new InvalidOperationException("input-recovery-null-channel");channelDisposed=false;opening=null;}
    catch(Exception error){opening=null;Drain("connect-"+error.GetType().Name);return;}
    if(!available()){Dispose();return;}
    try{source=create(channel)??throw new InvalidOperationException("input-recovery-null-source");sourceDisposed=false;if(closing){Drain("closed",true);return;}if(!available()){Dispose();return;}due=clock()+2000;State=InputRecoveryState.WaitingForReady;}
    catch(Exception error){constructionUnknown=true;cleanupError=new InvalidOperationException("input-recovery-source-construction-cleanup-unconfirmed",error);Drain("source-construction",true);return;}
   }
   if(State==InputRecoveryState.WaitingForReady||State==InputRecoveryState.Ready){
    try{if(channel.Failure!=null||source.Failure!=null){Drain(channel.Failure!=null?"channel-"+channel.Failure:"source-"+source.Failure);return;}
     if(source.Ready){State=InputRecoveryState.Ready;return;}
     if(State==InputRecoveryState.Ready){State=InputRecoveryState.WaitingForReady;due=clock()+2000;}
     if(clock()>=due)Drain("ready-timeout");
    }catch(Exception error){Drain("source-"+error.GetType().Name);}return;
   }
   if(State==InputRecoveryState.Draining){
    if(clock()>=due){terminal=true;if(cleanupError==null)Failure="drain-timeout";}
    if(opening!=null){if(!opening.IsCompleted)return;try{channel=opening.GetAwaiter().GetResult();channelDisposed=false;}catch{var observed=opening.Exception;}opening=null;DisposeResources();}
    ObserveCleanup();foreach(var lease in new IInputRecoveryLease[]{source,channel})if(lease!=null&&!lease.Completion.IsCompleted)return;
    Finish();
   }
  }
  void CollectRetriedResources(){if(opening==null&&(source==null||source.Released)&&(channel==null||channel.Released)){source=null;channel=null;sourceDisposed=channelDisposed=false;cleanupRetryRequested=false;}}
  public void RetryCleanup(){Owner();if(State!=InputRecoveryState.Faulted&&State!=InputRecoveryState.Draining)throw new InvalidOperationException("input-recovery-cleanup-not-stopped");ObserveCleanup();cleanupRetryRequested=true;sourceDisposed=channelDisposed=false;DisposeResources();ObserveCleanup();CollectRetriedResources();}
  public void Retry(){Owner();if(closing||State!=InputRecoveryState.Faulted||!ResourcesReleased)throw new InvalidOperationException("input-recovery-retry-before-cleanup");attempts=0;terminal=false;cleanupError=null;Failure=null;due=clock();State=InputRecoveryState.Backoff;}
  public void Dispose(){Owner();if(closing){if(State==InputRecoveryState.Faulted||State==InputRecoveryState.Draining)RetryCleanup();return;}closing=true;if(State==InputRecoveryState.Faulted){RetryCleanup();if(cleanupError!=null)completion.TrySetException(cleanupError);else{State=InputRecoveryState.Closed;completion.TrySetResult(true);}return;}Drain("closed");}
 }
}
