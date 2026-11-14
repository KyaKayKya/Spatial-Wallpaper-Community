using System;
using System.Threading;
using System.Threading.Tasks;
namespace SpatialWallpaper.Supervision.Interactive {
 // Owner-thread-only, one actual in-flight query. Invalidation never releases
 // its slot early or hides failure. This class grants no input authority.
 internal sealed class InputHeartbeat<T> {
  readonly int owner=Thread.CurrentThread.ManagedThreadId;long version,captured;bool closed;Task<T> pending;
  void Owner(){if(Thread.CurrentThread.ManagedThreadId!=owner)throw new InvalidOperationException("heartbeat-owner-thread");}
  public Task Completion=>pending??Task.CompletedTask;
  public bool Busy=>pending!=null;
  public void Invalidate(){Owner();version=checked(version+1);}
  public bool Begin(Func<T> query){Owner();if(closed||pending!=null)return false;if(query==null)throw new ArgumentNullException(nameof(query));captured=version;pending=Task.Run(query);return true;}
  public bool TryTake(out T value){Owner();value=default(T);if(pending==null||!pending.IsCompleted)return false;var done=pending;pending=null;T result=done.GetAwaiter().GetResult();if(closed||captured!=version)return false;value=result;return true;}
  public void Close(){Owner();if(closed)return;closed=true;Invalidate();}
 }
}
