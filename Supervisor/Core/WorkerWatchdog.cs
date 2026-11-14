using System;
namespace SpatialWallpaper.Supervision {
 public enum GuardState {Starting,Running,Stopping,Stopped,Quarantined}
 public enum GuardAction {None,Ready,Kill,Stopped}
 public sealed class WatchdogPolicy {
  public readonly long StartupMilliseconds,FrameMilliseconds,StopMilliseconds,PrivateBytes,CpuWindowMilliseconds,CpuMillisecondsPerWindow,GpuBytes,GpuStaleMilliseconds;
  public WatchdogPolicy(long startup,long frame,long stop,long memory,long cpuWindow,long cpuBudget,long gpuBytes=0,long gpuStaleMilliseconds=2000){
   if(startup<1||frame<1||stop<1||memory<1||cpuWindow<1||cpuBudget<1||gpuBytes<0||gpuStaleMilliseconds<1)throw new ArgumentOutOfRangeException("watchdog-policy");
   StartupMilliseconds=startup;FrameMilliseconds=frame;StopMilliseconds=stop;PrivateBytes=memory;CpuWindowMilliseconds=cpuWindow;CpuMillisecondsPerWindow=cpuBudget;GpuBytes=gpuBytes;GpuStaleMilliseconds=gpuStaleMilliseconds;
  }
 }
 // Driven by a trusted independent monitor using monotonic elapsed milliseconds and OS process counters.
 public sealed class WorkerWatchdog {
  readonly WatchdogPolicy policy;readonly long created;
  long clock,lastFrame,sequence,stopAt,cpuAt,cpuBase,lastGpuAt;bool cpuStarted,gpuSeen;
  public GuardState State{get;private set;}=GuardState.Starting;
  public FaultReason? Reason{get;private set;}
  public WorkerWatchdog(WatchdogPolicy limits,long now){policy=limits??throw new ArgumentNullException(nameof(limits));if(now<0)throw new ArgumentOutOfRangeException(nameof(now));created=clock=lastFrame=lastGpuAt=now;}
  void Clock(long now){if(now<clock)throw new InvalidOperationException("non-monotonic-monitor-clock");clock=now;}
  bool Terminal=>State==GuardState.Stopped||State==GuardState.Quarantined;
  GuardAction Fault(FaultReason reason){if(Terminal)return GuardAction.None;Reason=reason;State=GuardState.Quarantined;return GuardAction.Kill;}
  public void NoteHeartbeat(long now){Clock(now);} // Explicitly cannot refresh startup/render deadlines.
  public GuardAction CompletedFrame(long now,long next)=>CompletedFrame(now,next,now);
  public GuardAction CompletedFrame(long now,long next,long receivedAt){
   Clock(now);if(Terminal||State==GuardState.Stopping)return GuardAction.None;
   if(receivedAt<created||receivedAt>now||receivedAt<lastFrame)return Fault(FaultReason.Protocol);
   if(State==GuardState.Starting&&receivedAt-created>policy.StartupMilliseconds)return Fault(FaultReason.StartTimeout);
   if(State==GuardState.Running&&receivedAt-lastFrame>policy.FrameMilliseconds)return Fault(FaultReason.FrameTimeout);
   if(next<=sequence||next<1)return Fault(FaultReason.Protocol);
   sequence=next;lastFrame=receivedAt;
   if(State==GuardState.Starting){State=GuardState.Running;return GuardAction.Ready;}
   return GuardAction.None;
  }
  public void RequestStop(long now){
   Clock(now);if(Terminal||State==GuardState.Stopping)return;
   State=GuardState.Stopping;stopAt=now;
  }
  public GuardAction Poll(long now,bool alive,long privateBytes,long cpuMilliseconds,long gpuBytes=-1,long gpuSampleAt=0){
   Clock(now);if(Terminal)return GuardAction.None;
   if(!alive){
    if(State==GuardState.Stopping){State=GuardState.Stopped;return GuardAction.Stopped;}
    return Fault(FaultReason.ProcessExit);
   }
   if(privateBytes<0||cpuMilliseconds<0)return Fault(FaultReason.Monitoring);
   if(privateBytes>policy.PrivateBytes)return Fault(FaultReason.Budget);
   if(policy.GpuBytes>0&&State!=GuardState.Stopping){
    if(gpuBytes>=0){if(gpuSampleAt<0||gpuSampleAt>now||gpuSeen&&gpuSampleAt<lastGpuAt)return Fault(FaultReason.Monitoring);lastGpuAt=gpuSampleAt;gpuSeen=true;if(gpuBytes>policy.GpuBytes)return Fault(FaultReason.Budget);}
    if(State==GuardState.Running&&now-lastGpuAt>policy.GpuStaleMilliseconds)return Fault(FaultReason.Monitoring);
   }
   if(!cpuStarted){cpuStarted=true;cpuAt=now;cpuBase=cpuMilliseconds;}
   if(cpuMilliseconds<cpuBase)return Fault(FaultReason.Monitoring);
   if(now-cpuAt>=policy.CpuWindowMilliseconds){
    // Scale for a late OS sample, avoiding integer overflow and a false short-window verdict.
    double allowed=policy.CpuMillisecondsPerWindow*((double)(now-cpuAt)/policy.CpuWindowMilliseconds);
    if(cpuMilliseconds-cpuBase>allowed)return Fault(FaultReason.Budget);
    cpuAt=now;cpuBase=cpuMilliseconds;
   }
   if(State==GuardState.Stopping&&now-stopAt>=policy.StopMilliseconds)return Fault(FaultReason.StopTimeout);
   if(State==GuardState.Starting&&now-created>policy.StartupMilliseconds)return Fault(FaultReason.StartTimeout);
   if(State==GuardState.Running&&now-lastFrame>policy.FrameMilliseconds)return Fault(FaultReason.FrameTimeout);
   return GuardAction.None;
  }
 }
}
