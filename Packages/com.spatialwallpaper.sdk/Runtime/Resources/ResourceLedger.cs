using System;
namespace SpatialWallpaper.Sdk.Execution {
 public readonly struct ResourceCost {
  public readonly long ManagedBytes,NativeBytes,GpuBytes,Objects;
  public ResourceCost(long managedBytes,long nativeBytes,long gpuBytes,long objects){
   if(managedBytes<0||nativeBytes<0||gpuBytes<0||objects<0)throw new ArgumentOutOfRangeException("resource-cost");
   ManagedBytes=managedBytes;NativeBytes=nativeBytes;GpuBytes=gpuBytes;Objects=objects;
  }
  internal static ResourceCost Add(ResourceCost a,ResourceCost b)=>new ResourceCost(checked(a.ManagedBytes+b.ManagedBytes),checked(a.NativeBytes+b.NativeBytes),checked(a.GpuBytes+b.GpuBytes),checked(a.Objects+b.Objects));
  internal static ResourceCost Subtract(ResourceCost a,ResourceCost b)=>new ResourceCost(checked(a.ManagedBytes-b.ManagedBytes),checked(a.NativeBytes-b.NativeBytes),checked(a.GpuBytes-b.GpuBytes),checked(a.Objects-b.Objects));
  internal bool Within(ResourceCost limit)=>ManagedBytes<=limit.ManagedBytes&&NativeBytes<=limit.NativeBytes&&GpuBytes<=limit.GpuBytes&&Objects<=limit.Objects;
  internal static ResourceCost Maximum(ResourceCost a,ResourceCost b)=>new ResourceCost(Math.Max(a.ManagedBytes,b.ManagedBytes),Math.Max(a.NativeBytes,b.NativeBytes),Math.Max(a.GpuBytes,b.GpuBytes),Math.Max(a.Objects,b.Objects));
 }
 public sealed class ResourceBudgetException:InvalidOperationException {
  public ResourceBudgetException():base("resource-reservation-denied"){}
 }
 public sealed class ResourceLease:IDisposable {
  internal readonly ResourceLedger Owner;internal int State; // 0 reserved, 1 committed, 2 released
  internal ResourceCost Value;public ResourceCost Cost=>Owner.Cost(this);
  internal ResourceLease(ResourceLedger owner,ResourceCost cost){Owner=owner;Value=cost;}
  public void Commit()=>Owner.Commit(this);
  public void Grow(ResourceCost additional)=>Owner.Grow(this,additional);
  public void Dispose()=>Owner.Release(this);
 }
 public sealed class ResourceLedger:IDisposable {
  readonly object gate=new object();readonly ResourceCost limit;
  ResourceCost used,reserved,committed,peak;bool disposed;
  public ResourceLedger(ResourceCost limits){limit=limits;}
  public ResourceCost Limit=>limit;
  public ResourceCost Used{get{lock(gate)return used;}}
  public ResourceCost Reserved{get{lock(gate)return reserved;}}
  public ResourceCost Committed{get{lock(gate)return committed;}}
  public ResourceCost Peak{get{lock(gate)return peak;}}
  void Live(){if(disposed)throw new InvalidOperationException("resource-ledger-stopped");}
  public ResourceLease Reserve(ResourceCost cost){
   lock(gate){
    Live();ResourceCost next;
    try{next=ResourceCost.Add(used,cost);}catch(OverflowException){throw new ResourceBudgetException();}
    if(!next.Within(limit))throw new ResourceBudgetException();
    var lease=new ResourceLease(this,cost);
    // Both additions are bounded by next. Publish only after every check/allocation succeeded.
    var nextReserved=ResourceCost.Add(reserved,cost);
    used=next;reserved=nextReserved;peak=ResourceCost.Maximum(peak,used);return lease;
   }
  }
  public bool TryReserve(ResourceCost cost,out ResourceLease lease){
   try{lease=Reserve(cost);return true;}catch(ResourceBudgetException){lease=null;return false;}
  }
  internal ResourceCost Cost(ResourceLease lease){lock(gate)return lease.Value;}
  internal void Grow(ResourceLease lease,ResourceCost additional){
   lock(gate){
    Live();if(!ReferenceEquals(lease.Owner,this)||lease.State==2)throw new InvalidOperationException("reservation-state");
    ResourceCost next,nextValue;
    try{next=ResourceCost.Add(used,additional);nextValue=ResourceCost.Add(lease.Value,additional);}catch(OverflowException){throw new ResourceBudgetException();}
    if(!next.Within(limit))throw new ResourceBudgetException();
    if(lease.State==0)reserved=ResourceCost.Add(reserved,additional);else committed=ResourceCost.Add(committed,additional);
    used=next;lease.Value=nextValue;peak=ResourceCost.Maximum(peak,used);
   }
  }
  internal void Commit(ResourceLease lease){
   lock(gate){
    Live();if(!ReferenceEquals(lease.Owner,this)||lease.State!=0)throw new InvalidOperationException("reservation-state");
    reserved=ResourceCost.Subtract(reserved,lease.Cost);committed=ResourceCost.Add(committed,lease.Cost);lease.State=1;
   }
  }
  internal void Release(ResourceLease lease){
   lock(gate){
    if(!ReferenceEquals(lease.Owner,this))throw new InvalidOperationException("foreign-resource-lease");
    if(lease.State==2)return;
    if(!disposed){
     used=ResourceCost.Subtract(used,lease.Cost);
     if(lease.State==0)reserved=ResourceCost.Subtract(reserved,lease.Cost);else committed=ResourceCost.Subtract(committed,lease.Cost);
    }
    lease.State=2;
   }
  }
  public void Dispose(){lock(gate){if(disposed)return;disposed=true;used=reserved=committed=default;}}
 }
}
