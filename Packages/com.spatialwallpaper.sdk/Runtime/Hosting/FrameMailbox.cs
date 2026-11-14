using System;using System.Threading;using SpatialWallpaper.Sdk.Execution;
namespace SpatialWallpaper.Sdk.Hosting {
 public sealed class FrameSlot {
  internal readonly FrameMailbox Owner;internal int State;
  public byte[] Pixels{get;}public long Sequence{get;internal set;}public long ProjectionRevision{get;internal set;}
  internal FrameSlot(FrameMailbox owner,int bytes){Owner=owner;Pixels=new byte[bytes];}
 }
 // Fixed bounded ownership: capturing, pending and sending never overwrite each other.
 public sealed class FrameMailbox {
  readonly object gate=new object();readonly FrameSlot[] slots;readonly AutoResetEvent ready;readonly ResourceLease memory;
  FrameSlot pending,sending;bool closed;long lastPublished,lastSent,dropped;int writerThread;
  public long LastSent{get{lock(gate)return lastSent;}}public long Dropped{get{lock(gate)return dropped;}}
  public FrameMailbox(ResourceLedger ledger,int width,int height,int bufferCount=3){
   if(bufferCount<1||bufferCount>3)throw new ArgumentOutOfRangeException(nameof(bufferCount));
   int bytes=WorkerFrameCodec.PayloadBytes(width,height);
   memory=ledger.Reserve(new ResourceCost(checked(8192L+bufferCount*(bytes+128L)),4096,0,bufferCount+1));
   try{slots=new FrameSlot[bufferCount];for(int i=0;i<slots.Length;i++)slots[i]=new FrameSlot(this,bytes);ready=new AutoResetEvent(false);memory.Commit();}
   catch{ready?.Dispose();throw;}
  }
  ~FrameMailbox(){ready?.Dispose();memory?.Dispose();}
  void Slot(FrameSlot slot,int state){if(slot==null||!ReferenceEquals(slot.Owner,this)||slot.State!=state)throw new InvalidOperationException("frame-slot-owner-or-state");}
  public FrameSlot AcquireCapture(){
   lock(gate){if(closed)return null;foreach(var slot in slots)if(slot.State==0){slot.State=1;slot.Sequence=0;return slot;}return null;}
  }
  // A completed capture can replace pending data, never in-flight storage.
  // With one slot, busy sending drops this capture instead of allocating.
  internal FrameSlot AcquireLatestCapture(){lock(gate){var slot=AcquireCapture();if(slot!=null||closed)return slot;if(pending==null)return null;slot=pending;pending=null;dropped++;slot.State=1;slot.Sequence=0;return slot;}}
  public void CancelCapture(FrameSlot slot){lock(gate){Slot(slot,1);slot.State=0;}}
  public void Publish(FrameSlot slot,long sequence,long projectionRevision=0){
   lock(gate){
    Slot(slot,1);if(closed){slot.State=0;return;}if(sequence<=lastPublished||projectionRevision<0)throw new InvalidOperationException("frame-publish-order");
    if(pending!=null){pending.State=0;dropped++;}slot.Sequence=sequence;slot.ProjectionRevision=projectionRevision;slot.State=2;pending=slot;lastPublished=sequence;ready.Set();
   }
  }
  public FrameSlot Take(){
   int thread=Thread.CurrentThread.ManagedThreadId;
   while(true){
    lock(gate){
     if(writerThread==0)writerThread=thread;if(writerThread!=thread||sending!=null)throw new InvalidOperationException("frame-writer-thread-or-inflight");
     if(closed)return null;
     if(pending!=null){sending=pending;pending=null;sending.State=3;return sending;}
    }
    ready.WaitOne();
   }
  }
  public void Sent(FrameSlot slot){
   lock(gate){Slot(slot,3);if(!ReferenceEquals(sending,slot)||slot.Sequence<=lastSent)throw new InvalidOperationException("frame-send-order");lastSent=slot.Sequence;slot.State=0;sending=null;}
  }
  public void Close(){
   lock(gate){if(closed)return;closed=true;if(pending!=null){pending.State=0;pending=null;}ready.Set();}
  }
 }
}
