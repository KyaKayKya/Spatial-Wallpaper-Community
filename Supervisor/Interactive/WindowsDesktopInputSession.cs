using System;using System.Threading;using System.Threading.Tasks;using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Sdk.Capabilities;
namespace SpatialWallpaper.Supervision.Interactive {
 // Own on the receiver UI thread. Call Tick regularly, including after Dispose
 // until Completion settles. Keep the receiver thread pumping matching ups.
 // The factory must open the SAME authenticated Worker identity with bounded
 // I/O and honor cancellation. A late successful open is still owned/closed.
 public sealed class WindowsDesktopInputSession:IDisposable {
  sealed class Connection:IInputRecoveryLease {internal readonly DesktopInputChannel Value;internal Connection(DesktopInputChannel value){Value=value??throw new ArgumentNullException(nameof(value));}public Task Completion=>Value.Completion;public string Failure=>Value.Failure;public bool Released=>Value.Completion.Status==TaskStatus.RanToCompletion;public void Dispose()=>Value.Dispose();}
  sealed class Source:IInputRecoverySource {internal readonly WindowsDesktopInput Value;internal Source(WindowsDesktopInput value){Value=value;}public Task Completion=>Value.Completion;public string Failure=>Value.Failure;public bool Ready=>Value.Ready;public bool Focused=>Value.Focused;public bool Released=>Value.Released;public void Dispose()=>Value.Dispose();}
  readonly InputRecovery lifetime;readonly ReceiverWindowLifetime receiverLifetime;readonly Task completion;bool closing;
  internal DesktopInputChannel CurrentChannel{get;private set;}
  internal WindowsDesktopInput CurrentSource{get;private set;}
  public WindowsDesktopInputSession(IntPtr receiver,Func<CancellationToken,DesktopInputChannel> connect,string helper,int left,int top,int width,int height,Func<bool> binding=null){
   if(connect==null)throw new ArgumentNullException(nameof(connect));
   receiverLifetime=new ReceiverWindowLifetime(receiver);
   try{lifetime=new InputRecovery(WindowsMachineClock.Milliseconds,token=>Task.Run<IInputRecoveryLease>(()=>new Connection(connect(token))),lease=>{
    var connection=(Connection)lease;var source=new WindowsDesktopInput(receiver,connection.Value,helper,left,top,width,height,binding);
    CurrentChannel=connection.Value;CurrentSource=source;return new Source(source);
   },()=>!receiverLifetime.Destroyed);completion=Task.WhenAll(lifetime.Completion,receiverLifetime.Completion);
   }catch{receiverLifetime.Dispose();throw;}
  }
  public InputRecoveryState State=>lifetime.State;public string Failure=>receiverLifetime.RemovalError!=0?"receiver-cleanup-"+receiverLifetime.RemovalError:lifetime.Failure;public string LastCleanupFailure=>receiverLifetime.RemovalError!=0?"Win32Exception":lifetime.LastCleanupError?.GetType().Name;public bool ResourcesReleased=>lifetime.ResourcesReleased&&receiverLifetime.Released;public bool Ready=>!closing&&!receiverLifetime.Destroyed&&lifetime.Ready;public bool Focused=>Ready&&lifetime.Focused;public Task Completion=>completion;
  public void Tick(){receiverLifetime.Owner();if(receiverLifetime.Destroyed&&!closing)Dispose();lifetime.Tick();}
  // A live session retains its window guard across source generations. Retry
  // readiness therefore differs from releasing every session-owned resource.
  public bool CanRetry=>!closing&&!receiverLifetime.Destroyed&&receiverLifetime.RemovalError==0&&lifetime.State==InputRecoveryState.Faulted&&lifetime.ResourcesReleased;
  public void Retry(){receiverLifetime.Owner();if(!CanRetry)throw new InvalidOperationException("input-receiver-retry-not-ready");lifetime.Retry();}
  public void RetryCleanup(){receiverLifetime.Owner();try{lifetime.RetryCleanup();}finally{if(closing)receiverLifetime.Dispose();}}
  public void Dispose(){receiverLifetime.Owner();closing=true;try{lifetime.Dispose();}finally{receiverLifetime.Dispose();}}
 }
}
