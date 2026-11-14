using System;using System.Threading;
namespace SpatialWallpaper.Supervision {
 // Keep the first cleanup failure independently of task replies and later finally errors.
 public sealed class CleanupFailures {
  Exception first;
  public Exception Failure=>Volatile.Read(ref first);
  public bool Failed=>Failure!=null;
  public void Record(Exception error){if(error!=null)Interlocked.CompareExchange(ref first,error,null);}
 }
}
