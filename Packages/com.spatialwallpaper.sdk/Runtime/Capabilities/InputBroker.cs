using System;using System.Collections.Generic;using System.Linq;
using SpatialWallpaper.Sdk.Capabilities;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Sdk.Capabilities {
 public interface IPreviewInputTarget {
  ulong CaptureEpoch{get;}
  void Observe(InputObservation observation);
  void Revoke();
 }
 public readonly struct InputFocusConfirmation {
  public readonly ulong Epoch;public readonly bool Focused,Ready;
  public InputFocusConfirmation(ulong epoch,bool focused,bool ready=true){Epoch=epoch;Focused=focused;Ready=ready;}
 }
 // Trusted desktop sinks must enqueue without waiting for external I/O. Focus
 // is confirmed by the broker, never inferred from sending an acquire request.
 public interface IDesktopInputTarget:IPreviewInputTarget {
  void AcquireFocus(InputObservation request);
  InputFocusConfirmation GetFocusConfirmation();
 }
 // Trusted registered target, never constructed from a Worker capability message.
 // The object itself is the registration token. Closing permanently seals it.
 public sealed class InputTarget:IDisposable,IDesktopInputTarget {
  internal readonly InputBroker Owner;internal readonly InputGate Gate;
  internal readonly uint[] Downs=new uint[7],Ups=new uint[7];
  internal readonly InputEventJournal Journal;
  internal ulong Epoch;internal long Wheel;internal bool Closed;internal Action<ulong,long> PublishAuthority;
  public readonly WorkerIdentity Identity;
  internal readonly bool Preview;internal bool SourceAttached;
  internal InputTarget(InputBroker owner,WorkerIdentity identity,bool explicitFocus,bool ordered){Owner=owner;Identity=identity;Gate=new InputGate(explicitFocus);Preview=!explicitFocus;if(ordered)Journal=new InputEventJournal();}
  public ulong CaptureEpoch=>Owner.Capture(this);
  public bool Matches(WorkerIdentity identity)=>identity!=null&&Identity.Attempt==identity.Attempt&&Identity.Instance==identity.Instance&&Identity.Generation==identity.Generation;
  public void Observe(InputObservation observation)=>Owner.Publish(this,observation,false);
  public void AcquireFocus(InputObservation request)=>Owner.Publish(this,request,true);
  public InputFocusConfirmation GetFocusConfirmation()=>Owner.Confirm(this);
  public void Revoke()=>Owner.Revoke(this);
  public byte[] Snapshot()=>Owner.Snapshot(this);
  public OrderedInputBatch OrderedSnapshot(long acknowledged)=>Owner.OrderedSnapshot(this,acknowledged);
  public void Dispose()=>Owner.Remove(this);
 }
 // Opt-in trusted native delivery. Called only under the broker's existing
 // lock, after focus validation. Preserve accepted observation order, including
 // wheel directions whose cumulative sum is zero; overflow revokes the epoch.
 internal sealed class InputEventJournal {
  readonly Queue<OrderedInputEvent> events=new Queue<OrderedInputEvent>();long sequence,acknowledged;
  internal void Reset(){events.Clear();}
  internal bool Append(InputState state,long eventTime){
   if(!state.Focused||(state.Pressed|state.Released)==0&&state.Wheel==0)return true;
   if(events.Count>=OrderedInputBatch.MaxEvents)return false;events.Enqueue(new OrderedInputEvent(checked(++sequence),eventTime,state.Epoch,state.Held,state.Pressed,state.Released,state.Wheel));return true;
  }
  internal OrderedInputBatch Read(byte[] snapshot,long after){
   if(after<acknowledged||after>sequence)throw new InvalidOperationException("input-event-cursor");
   while(events.Count!=0&&events.Peek().Sequence<=after)events.Dequeue();acknowledged=after;
   return new OrderedInputBatch(snapshot,events.ToArray(),sequence);
  }
 }
 // Every target transition, exclusive-focus transfer and snapshot uses one lock.
 // No focus/COM query or pipe write runs while this lock is held.
 public sealed class InputBroker:IDisposable {
  readonly object sync=new object();readonly Func<long> clock;
  readonly HashSet<InputTarget> active=new HashSet<InputTarget>();
  readonly Dictionary<Guid,WeakReference> registrations=new Dictionary<Guid,WeakReference>();bool closed;InputTarget focused;
  public InputBroker(Func<long> machineClock=null){clock=machineClock??WindowsMachineClock.Milliseconds;}
  public InputTarget Register(WorkerIdentity identity,bool explicitFocus)=>Register(identity,explicitFocus,false);
  public InputTarget Register(WorkerIdentity identity,bool explicitFocus,bool ordered){lock(sync){
   if(identity==null)throw new ArgumentNullException(nameof(identity));
   if(closed)throw new ObjectDisposedException(nameof(InputBroker));
   if(active.Count>=2)throw new InvalidOperationException("input-target-capacity");
   foreach(var key in registrations.Where(pair=>!pair.Value.IsAlive).Select(pair=>pair.Key).ToArray())registrations.Remove(key);
   if(registrations.TryGetValue(identity.Attempt,out var previous)&&previous.Target!=null)throw new InvalidOperationException("input-attempt-already-registered");
   if(registrations.Count>=64)throw new InvalidOperationException("input-retained-registration-capacity");
   var target=new InputTarget(this,identity,explicitFocus,ordered);active.Add(target);registrations[identity.Attempt]=new WeakReference(target);return target;
  }}
  void Check(InputTarget target){if(target==null||target.Owner!=this)throw new ArgumentException("input-registration-owner");}
  // Bind a fixed, nonblocking trusted metadata store, never author callbacks.
  internal void BindAuthority(InputTarget target,Action<ulong,long> publish){lock(sync){Check(target);if(closed||target.Closed||target.PublishAuthority!=null||publish==null)throw new InvalidOperationException("input-authority-binding");target.PublishAuthority=publish;var state=target.Gate.Read(clock());publish(state.Focused?state.Epoch:0,state.Focused?state.EvidenceTime:0);}}
  internal ulong Capture(InputTarget target){lock(sync){Check(target);return target.Gate.CaptureEpoch;}}
  internal void AttachSource(InputTarget target,bool preview=true){lock(sync){Check(target);if(closed||target.Closed||target.Preview!=preview||target.SourceAttached)throw new InvalidOperationException("input-source-unavailable");target.Gate.RestartSource(clock());Reset(target,target.Gate.CaptureEpoch);target.SourceAttached=true;if(focused==target)focused=null;}}
  internal InputFocusConfirmation Confirm(InputTarget target){lock(sync){Check(target);var state=target.Gate.Read(clock());if(target.Epoch!=state.Epoch)Reset(target,state.Epoch);if(!state.Focused&&focused==target)focused=null;return new InputFocusConfirmation(state.Epoch,!closed&&!target.Closed&&state.Focused);}}
  internal void DetachSource(InputTarget target){lock(sync){Revoke(target);target.SourceAttached=false;}}
  static void Reset(InputTarget target,ulong epoch){target.PublishAuthority?.Invoke(0,0);target.Epoch=epoch;target.Wheel=0;Array.Clear(target.Downs,0,7);Array.Clear(target.Ups,0,7);target.Journal?.Reset();}
  static void Accumulate(InputTarget target,InputState state,long now,long eventTime){
   if(target.Epoch!=state.Epoch)Reset(target,state.Epoch);
   if(!state.Focused)return;
   try{checked{
    for(int i=0;i<7;i++){if((state.Pressed&(1u<<i))!=0)++target.Downs[i];if((state.Released&(1u<<i))!=0)++target.Ups[i];}
    target.Wheel+=state.Wheel;
    if(target.Journal!=null&&!target.Journal.Append(state,eventTime))throw new OverflowException("input-event-capacity");
    target.PublishAuthority?.Invoke(state.Epoch,state.EvidenceTime);
   }}catch(OverflowException){target.Gate.Revoke(now);Reset(target,target.Gate.CaptureEpoch);}
  }
  internal void Publish(InputTarget target,InputObservation observation,bool acquire){lock(sync){
   Check(target);if(closed||target.Closed)return;long now=clock();
   var state=acquire?target.Gate.AcquireFocus(observation,now):target.Gate.Observe(observation,now);
   if(state.Focused&&focused!=target){
    foreach(var other in active)if(other!=target){other.Gate.Revoke(now);Reset(other,other.Gate.CaptureEpoch);}
    focused=target;
   }else if(!state.Focused&&focused==target)focused=null;
   Accumulate(target,state,now,observation.Time);
  }}
  internal void Revoke(InputTarget target){lock(sync){Check(target);if(target.Closed)return;target.Gate.Revoke(clock());Reset(target,target.Gate.CaptureEpoch);if(focused==target)focused=null;}}
  internal void Remove(InputTarget target){lock(sync){Check(target);if(target.Closed)return;target.Gate.Revoke(clock());Reset(target,target.Gate.CaptureEpoch);target.Closed=true;active.Remove(target);if(focused==target)focused=null;}}
  internal byte[] Snapshot(InputTarget target){lock(sync){
   Check(target);long now=clock();var state=target.Gate.Read(now);if(target.Epoch!=state.Epoch)Reset(target,state.Epoch);if(!state.Focused&&focused==target)focused=null;
   // Preserve original evidence time. A delayed write cannot extend key lifetime.
   return InputSnapshotCodec.Encode(state.Epoch,state.Focused?state.EvidenceTime:now,state.Focused,state.Held,
    state.Focused?(ushort)Math.Round(state.X*65535): (ushort)0,state.Focused?(ushort)Math.Round(state.Y*65535):(ushort)0,
    target.Wheel,target.Downs,target.Ups);
  }}
  internal OrderedInputBatch OrderedSnapshot(InputTarget target,long acknowledged){lock(sync){
   Check(target);if(target.Journal==null)throw new InvalidOperationException("input-event-journal-not-registered");
   var snapshot=Snapshot(target);return target.Journal.Read(snapshot,acknowledged);
  }}
  public void Dispose(){lock(sync){if(closed)return;foreach(var target in active.ToArray())Remove(target);closed=true;}}
 }
}
