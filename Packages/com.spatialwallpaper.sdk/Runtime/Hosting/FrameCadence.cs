using System;using System.Diagnostics;
namespace SpatialWallpaper.Sdk.Hosting {
 // Trusted wall-clock limiter; Unity vSync and occluded-window frame pacing are not a security limit.
 public sealed class FrameCadence {
  readonly long interval;long observed,accepted;bool started;
  public FrameCadence(int maximumFramesPerSecond):this(maximumFramesPerSecond,Stopwatch.Frequency){}
  internal FrameCadence(int rate,long frequency){
   if(rate<1||rate>240||frequency<1)throw new ArgumentOutOfRangeException("frame-cadence-policy");
   interval=frequency/rate+(frequency%rate==0?0:1);
  }
  public bool TryBegin(long timestamp){
   if(timestamp<0||started&&timestamp<observed)throw new InvalidOperationException("non-monotonic-frame-clock");observed=timestamp;
   if(started){
    long elapsed=timestamp-accepted;if(elapsed<interval)return false;
    // Keep the wall-clock phase across sub-frame scheduling jitter. Reset only
    // after a missed full period, so a stall never accumulates catch-up work.
    accepted=elapsed/interval>=2?timestamp:accepted+interval;
   }else{started=true;accepted=timestamp;}
   return true;
  }
 }
}
