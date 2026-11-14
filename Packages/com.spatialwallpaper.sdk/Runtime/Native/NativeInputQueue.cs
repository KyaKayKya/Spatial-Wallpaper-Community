using System;
using System.IO;
using System.Collections.Generic;
using SpatialWallpaper.Sdk.Capabilities;

namespace SpatialWallpaper.Sdk.Native {
 public sealed class NativeInputDelivery {
  public readonly byte[] Snapshot;public readonly TickEvent[] Events;
  internal NativeInputDelivery(byte[] snapshot,TickEvent[] events){Snapshot=snapshot;Events=events;}
 }
 // Native-side bounded handoff of the existing broker's cumulative counters.
 // State/pointer may coalesce; each edge prefix advances at most once per Tick.
 // A new epoch cancels unsent old edges before assigning event IDs, so there is
 // no ID gap or old-focus replay. The real source timestamp is never renewed.
 public sealed class NativeInputQueue {
  readonly object gate=new object();readonly uint[] downs=new uint[7],ups=new uint[7];byte[] latest;ulong epoch,blockedThrough;long wheel,eventId;
  readonly Queue<OrderedInputEvent> orderedEvents=new Queue<OrderedInputEvent>();int mode,rejectedOrderedEventCount;long orderedCursor,orderedTime;uint orderedHeld;
  static readonly string[] buttons={"Primary","Secondary","Space","Left","Right","Up","Down"};
  public byte[] CurrentSnapshot{get{lock(gate)return latest==null?null:(byte[])latest.Clone();}}
  public int PendingOrderedEventCount{get{lock(gate)return orderedEvents.Count;}}
  public int RejectedOrderedEventCount{get{lock(gate)return rejectedOrderedEventCount;}}
  public void Revoke(){lock(gate){blockedThrough=Math.Max(blockedThrough,epoch);latest=null;orderedEvents.Clear();}}
  public void Publish(byte[] snapshot){
   if(snapshot==null)return;InputSnapshotCodec.RequireShape(snapshot);var copy=(byte[])snapshot.Clone();
   lock(gate){
    if(mode==2)throw new InvalidOperationException("native-input-mode");
    ulong incoming=InputSnapshotCodec.U64(copy,8);if(incoming<epoch||incoming<=blockedThrough)return;
    if(latest!=null&&incoming==epoch&&InputSnapshotCodec.U64(copy,16)<InputSnapshotCodec.U64(latest,16))return;
    bool fresh=incoming>epoch;long pending=0;
    for(int i=0;i<7;i++){
     uint down=InputSnapshotCodec.U32(copy,40+i*4),up=InputSnapshotCodec.U32(copy,68+i*4);
     if(!fresh&&latest!=null&&(down<InputSnapshotCodec.U32(latest,40+i*4)||up<InputSnapshotCodec.U32(latest,68+i*4)))throw new InvalidDataException("native-input-counter-regressed");
     pending+=(long)down-(fresh?0:downs[i])+(long)up-(fresh?0:ups[i]);
    }
    if(pending>SceneCommandCatalog.MaxCommands)throw new InvalidDataException("native-input-event-capacity");
    if(fresh){Array.Clear(downs,0,7);Array.Clear(ups,0,7);wheel=0;epoch=incoming;}
    latest=copy;mode=1;
   }
  }
  public void PublishOrdered(OrderedInputBatch value){
   if(value==null)throw new ArgumentNullException(nameof(value));var packet=new OrderedInputBatch(value.Snapshot,value.Events,value.Cursor);var copy=packet.Snapshot;
   lock(gate){
    if(mode==1)throw new InvalidOperationException("native-input-mode");
    ulong incoming=InputSnapshotCodec.U64(copy,8);if(incoming<epoch||incoming<=blockedThrough)return;
    if(latest!=null&&incoming==epoch&&InputSnapshotCodec.U64(copy,16)<InputSnapshotCodec.U64(latest,16))return;
    bool fresh=incoming>epoch;if(packet.Cursor<orderedCursor)throw new InvalidDataException("native-input-event-cursor-regressed");
    var pending=fresh?new List<OrderedInputEvent>():new List<OrderedInputEvent>(orderedEvents);
    long cursor=orderedCursor,time=fresh?0:orderedTime;
    foreach(var item in packet.Events){
     if(!fresh&&item.Sequence<=orderedCursor)continue;
     if(fresh&&pending.Count==0){if(item.Sequence<=orderedCursor)throw new InvalidDataException("native-input-event-replayed");cursor=item.Sequence-1;}
     if(item.Sequence!=checked(cursor+1)||item.Time<time)throw new InvalidDataException("native-input-event-order");
     pending.Add(item);cursor=item.Sequence;time=item.Time;
    }
    if(fresh&&pending.Count==0)cursor=packet.Cursor;
    if(pending.Count>OrderedInputBatch.MaxEvents)rejectedOrderedEventCount=pending.Count;
    if(cursor!=packet.Cursor||pending.Count>OrderedInputBatch.MaxEvents)throw new InvalidDataException("native-input-event-capacity-or-gap");
    var nextDown=fresh?new uint[7]:(uint[])downs.Clone();var nextUp=fresh?new uint[7]:(uint[])ups.Clone();long nextWheel=fresh?0:wheel;uint held=fresh?0:orderedHeld;
    checked{foreach(var item in pending){
     uint expected=(held|item.Pressed)&~item.Released;
     if(item.Held!=expected||(item.Pressed&held)!=0||(item.Released&~held)!=0)throw new InvalidDataException("native-input-event-held-transition");
     for(int i=0;i<7;i++){if((item.Pressed&(1u<<i))!=0)nextDown[i]++;if((item.Released&(1u<<i))!=0)nextUp[i]++;}nextWheel+=item.Wheel;held=item.Held;
    }}
    for(int i=0;i<7;i++)if(nextDown[i]!=InputSnapshotCodec.U32(copy,40+i*4)||nextUp[i]!=InputSnapshotCodec.U32(copy,68+i*4))throw new InvalidDataException("native-input-event-counter-mismatch");
    if(nextWheel!=unchecked((long)InputSnapshotCodec.U64(copy,32))||held!=InputSnapshotCodec.U32(copy,24))throw new InvalidDataException("native-input-event-state-mismatch");
    // No mutation before all event/counter/state checks have succeeded.
    if(fresh){Array.Clear(downs,0,7);Array.Clear(ups,0,7);wheel=0;orderedHeld=0;epoch=incoming;}
    orderedEvents.Clear();foreach(var item in pending)orderedEvents.Enqueue(item);orderedCursor=cursor;orderedTime=time;latest=copy;mode=2;
   }
  }
  public NativeInputDelivery Take(){lock(gate){
   var events=new List<TickEvent>();if(latest==null)return new NativeInputDelivery(null,events.ToArray());
   bool focus=InputSnapshotCodec.U32(latest,4)==1;
   if(!focus)return new NativeInputDelivery((byte[])latest.Clone(),events.ToArray());
   if(mode==2){
    if(orderedEvents.Count!=0){var item=orderedEvents.Dequeue();orderedHeld=item.Held;
     for(int i=0;i<7;i++){if((item.Pressed&(1u<<i))!=0){downs[i]++;events.Add(new TickEvent(checked(++eventId),"down",buttons[i]));}if((item.Released&(1u<<i))!=0){ups[i]++;events.Add(new TickEvent(checked(++eventId),"up",buttons[i]));}}
     wheel=checked(wheel+item.Wheel);if(item.Wheel!=0)events.Add(new TickEvent(checked(++eventId),"wheel",null,item.Wheel/120f));
    }
    uint location=InputSnapshotCodec.U32(latest,28);return new NativeInputDelivery(InputSnapshotCodec.Encode(epoch,(long)InputSnapshotCodec.U64(latest,16),true,orderedHeld,(ushort)location,(ushort)(location>>16),wheel,downs,ups),events.ToArray());
   }
   for(int i=0;i<7;i++){
    if(downs[i]<InputSnapshotCodec.U32(latest,40+i*4)){downs[i]++;events.Add(new TickEvent(checked(++eventId),"down",buttons[i]));}
    if(ups[i]<InputSnapshotCodec.U32(latest,68+i*4)){ups[i]++;events.Add(new TickEvent(checked(++eventId),"up",buttons[i]));}
   }
   long nextWheel=unchecked((long)InputSnapshotCodec.U64(latest,32)),difference=checked(nextWheel-wheel);
   if(difference!=0){events.Add(new TickEvent(checked(++eventId),"wheel",null,difference/120f));wheel=nextWheel;}
   uint pointer=InputSnapshotCodec.U32(latest,28);
   var snapshot=InputSnapshotCodec.Encode(epoch,(long)InputSnapshotCodec.U64(latest,16),true,InputSnapshotCodec.U32(latest,24),(ushort)pointer,(ushort)(pointer>>16),wheel,downs,ups);
   return new NativeInputDelivery(snapshot,events.ToArray());
  }}
 }
}
