using System;

namespace SpatialWallpaper.Sdk.Capabilities {
 // Trusted adapter evidence, never deserialized from author/Worker messages.
 // Time is the original capture's machine monotonic milliseconds, not dequeue time.
 public readonly struct InputObservation {
  public readonly long Sequence,Time;
  public readonly ulong CaptureEpoch;
  public readonly bool Target,Foreground,ShellFocus,Session,Adapter,Modifier,PrimaryDown;
  public readonly uint Held;
  public readonly float X,Y;
  public readonly int Wheel;
  public readonly bool Heartbeat;
  public InputObservation(long sequence,long time,bool target,bool foreground,bool shellFocus,bool session,bool adapter,bool modifier,bool primaryDown,uint held,float x,float y,int wheel,ulong captureEpoch){
   this=new InputObservation(sequence,time,target,foreground,shellFocus,session,adapter,modifier,primaryDown,held,x,y,wheel,captureEpoch,false);
  }
  public InputObservation(long sequence,long time,bool target,bool foreground,bool shellFocus,bool session,bool adapter,bool modifier,bool primaryDown,uint held,float x,float y,int wheel,ulong captureEpoch,bool heartbeat){
   Heartbeat=heartbeat;
   CaptureEpoch=captureEpoch;Sequence=sequence;Time=time;Target=target;Foreground=foreground;ShellFocus=shellFocus;Session=session;Adapter=adapter;Modifier=modifier;PrimaryDown=primaryDown;Held=held;X=x;Y=y;Wheel=wheel;
  }
 }
 public readonly struct InputState {
  public readonly ulong Epoch;
  public readonly long EvidenceTime;
  public readonly bool Focused;
  public readonly uint Held,Pressed,Released;
  public readonly float X,Y;
  public readonly int Wheel;
  internal InputState(ulong epoch,bool focused,uint held,uint pressed,uint released,float x,float y,int wheel,long evidenceTime=0){Epoch=epoch;Focused=focused;Held=held;Pressed=pressed;Released=released;X=x;Y=y;Wheel=wheel;EvidenceTime=evidenceTime;}
 }
 // One instance per trusted registered target. The adapter must select exactly one
 // target before supplying evidence; ambiguous/occluded hit tests revoke all targets.
 public sealed class InputGate {
  public const long FreshnessMilliseconds=250;
  readonly object sync=new object();
  readonly bool explicitFocus;
  long sequence,clock,lastEvidence,lastEventTime,focusBegan;
  uint suppressed;
  ulong epoch=1;
  ulong acquiredEpoch;long acquiredSequence,acquiredTime;
  InputState state=new InputState(1,false,0,0,0,0,0,0);
  public InputGate(bool explicitFocus=false){this.explicitFocus=explicitFocus;}
  // Capture with the raw event BEFORE queueing or querying another process.
  public ulong CaptureEpoch { get { lock(sync)return epoch; } }
  // The adapter keeps original click time/epoch while doing asynchronous checks.
  // Foreground/ShellFocus prove the registered receiver's actual OS focus;
  // Adapter additionally proves a complete current physical-key snapshot.
  // Capture time of queued key messages must never be replaced by dequeue time.
  public InputState AcquireFocus(InputObservation request,long now){lock(sync){
   bool repeated=state.Focused&&request.CaptureEpoch==acquiredEpoch&&request.Sequence==acquiredSequence&&request.Time==acquiredTime;
   if(request.Heartbeat||!explicitFocus||request.CaptureEpoch!=epoch&&!repeated||now<clock||request.Time>now||
      !request.PrimaryDown||!Valid(request)){Clear();return state;}
   // A repeated confirmation cannot refresh evidence or reset live held keys.
   if(state.Focused)return Read(now);
   if(request.Sequence<=sequence||request.Time<lastEventTime||now-request.Time>FreshnessMilliseconds){Clear();return state;}
   // Rotate at confirmation, not merely on revocation: even events captured in
   // the same millisecond before confirmation must not enter the new focus.
   Clear();acquiredEpoch=request.CaptureEpoch;acquiredSequence=request.Sequence;acquiredTime=request.Time;
   sequence=request.Sequence;lastEventTime=request.Time;clock=now;lastEvidence=focusBegan=now;suppressed=request.Held;
   state=new InputState(epoch,true,0,0,0,request.X,request.Y,0,now);return state;
  }}
  static bool Valid(InputObservation value)=>value.Target&&value.Foreground&&value.ShellFocus&&value.Session&&value.Adapter&&!value.Modifier&&
   (value.Held&~127u)==0&&!float.IsNaN(value.X)&&!float.IsNaN(value.Y)&&value.X>=0&&value.X<=1&&value.Y>=0&&value.Y<=1&&
   value.Wheel>=-1200&&value.Wheel<=1200;
  void Clear(){
   if(epoch==ulong.MaxValue)throw new InvalidOperationException("input-epoch-exhausted");++epoch;
   state=new InputState(epoch,false,0,0,0,0,0,0);suppressed=0;
   acquiredEpoch=0;acquiredSequence=acquiredTime=0;
  }
  public void Revoke(long now){lock(sync){clock=Math.Max(clock,now);Clear();}}
  // Only a new trusted source lease resets its sequence space. Epoch rotation
  // prevents observations captured by the previous source from crossing leases.
  internal void RestartSource(long now){lock(sync){Revoke(now);sequence=lastEventTime=0;}}
  public InputState Read(long now){lock(sync){
   if(now<clock){Clear();return state;}
   clock=now;if(state.Focused&&now-lastEvidence>FreshnessMilliseconds)Clear();
   // Heartbeats carry held state only. Only Observe returns a new event's edges.
   return new InputState(state.Epoch,state.Focused,state.Held,0,0,state.X,state.Y,0,state.EvidenceTime);
  }}
  public InputState Observe(InputObservation value)=>Observe(value,value.Time);
  public InputState Observe(InputObservation value,long now){lock(sync){
   if(value.CaptureEpoch!=epoch||value.Sequence<=sequence||now<clock||!value.Heartbeat&&value.Time<lastEventTime||value.Time>now||
      now-value.Time>FreshnessMilliseconds||state.Focused&&value.Time<focusBegan){Clear();return state;}
   sequence=value.Sequence;clock=now;
   if(!Valid(value)){Clear();return state;}
   if(state.Focused&&now-lastEvidence>FreshnessMilliseconds){Clear();return state;}
   if(value.Heartbeat){
    if(!state.Focused){Clear();return state;}
    // A completed background proof may precede a subsequently delivered raw
    // edge. It can renew evidence, never order raw events or replay held state.
    if(value.Time<lastEvidence)return Read(now);
    if(value.PrimaryDown||value.Wheel!=0||(value.Held&~suppressed)!=state.Held){Clear();return state;}
    lastEvidence=value.Time;state=new InputState(epoch,true,state.Held,0,0,state.X,state.Y,0,lastEvidence);return state;
   }
   lastEventTime=value.Time;lastEvidence=Math.Max(lastEvidence,value.Time);
   if(!state.Focused){
    if(explicitFocus||!value.PrimaryDown||(value.Held&1)==0)return state;
    Clear();
    focusBegan=value.Time;
    suppressed=value.Held&~1u;
    state=new InputState(epoch,true,1,1,0,value.X,value.Y,0,value.Time);return state;
   }
   suppressed&=value.Held;
   uint held=value.Held&~suppressed;
   state=new InputState(epoch,true,held,held&~state.Held,state.Held&~held,value.X,value.Y,value.Wheel,lastEvidence);
   return state;
  }}
 }
}
