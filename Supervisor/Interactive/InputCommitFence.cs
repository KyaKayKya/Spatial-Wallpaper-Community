using System;
namespace SpatialWallpaper.Supervision.Interactive {
 // Queries run outside this gate. Receiver UI invalidation never waits on a
 // helper/query. Only bounded local target enqueue and scalar updates belong
 // in commit/revoke callbacks; no native calls, UI dispatch or external I/O.
 public sealed class InputCommitFence {
  readonly object gate=new object();long generation=1;bool closed;
  public long Capture(){lock(gate)return closed?0:generation;}
  public bool TryCommit(long captured,Action commit){if(commit==null)throw new ArgumentNullException(nameof(commit));lock(gate){if(closed||captured==0||captured!=generation)return false;commit();return true;}}
  public void Invalidate(Action revoke,bool close){if(revoke==null)throw new ArgumentNullException(nameof(revoke));lock(gate){closed|=close;if(generation==long.MaxValue)closed=true;else generation++;revoke();}}
 }
}
