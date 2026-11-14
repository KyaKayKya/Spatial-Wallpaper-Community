using System;using System.Threading;
namespace SpatialWallpaper.Sdk.Execution {
 // Only the trusted renderer may report completed GPU readbacks. A heartbeat is insufficient.
 public sealed class RenderRetirementQueue:IRetirementQueue {
  struct Entry {internal object Native,Retained;internal ResourceLease Lease;internal long After;internal bool DestructionObserved;}
  readonly Entry[] entries;readonly Func<object,bool> destroyed;readonly ResourceLease registry;
  readonly int thread;int head,count;long completed,submitted;bool closing,submissionTracking;
  public int Pending=>count;public bool Closed{get;private set;}
  public RenderRetirementQueue(ResourceLedger ledger,int objectLimit,Func<object,bool> isDestroyed){
   if(objectLimit<1||objectLimit>65536)throw new ArgumentOutOfRangeException(nameof(objectLimit));
   destroyed=isDestroyed??throw new ArgumentNullException(nameof(isDestroyed));thread=Thread.CurrentThread.ManagedThreadId;
   int capacity=checked(objectLimit*2+128);
   registry=ledger.Reserve(new ResourceCost(checked(1024L+capacity*64L),0,0,0));
   try{entries=new Entry[capacity];registry.Commit();}catch{registry.Dispose();throw;}
  }
  void ThreadCheck(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("render-retirement-thread");}
  public void RequireCapacity(int additional){
   ThreadCheck();if(closing||Closed||additional<0||additional>entries.Length-count)throw new InvalidOperationException("render-retirement-capacity-or-state");
  }
  public void Retire(object native,ResourceLease lease,Action<object> destroy,object retained=null){
   RequireCapacity(1);if(lease==null)throw new ArgumentNullException(nameof(lease));
   // Publish before destruction: even a native destroy failure must retain its estimate.
   entries[(head+count)%entries.Length]=new Entry{Native=native,Retained=retained,Lease=lease,After=checked((submissionTracking?submitted:completed)+2)};count++;
   if(native!=null)destroy(native);
   int tail=(head+count-1)%entries.Length;
   entries[tail].DestructionObserved=native==null||destroyed(native);
  }
  public void AdvanceSubmittedRender(long sequence){
   ThreadCheck();if(Closed||sequence<=submitted||sequence<completed)throw new InvalidOperationException("render-submission-order");submissionTracking=true;submitted=sequence;
  }
  public void AdvanceCompletedRender(long sequence){
   ThreadCheck();if(Closed||sequence<completed||submissionTracking&&sequence>submitted)throw new InvalidOperationException("render-progress-order");completed=sequence;
   int pending=count;
   for(int i=0;i<pending;i++){
    var entry=entries[head];entries[head]=default;head=(head+1)%entries.Length;count--;
    if(!entry.DestructionObserved&&destroyed(entry.Native)){entry.DestructionObserved=true;entry.After=checked((submissionTracking?submitted:completed)+2);}
    if(entry.DestructionObserved&&completed>=entry.After)entry.Lease.Dispose();
    else{entries[(head+count)%entries.Length]=entry;count++;}
    GC.KeepAlive(entry.Retained);
   }
   Drain();
  }
  public void CloseWhenDrained(){ThreadCheck();closing=true;Drain();}
  void Drain(){if(closing&&count==0&&!Closed){Closed=true;registry.Dispose();}}
 }
}
