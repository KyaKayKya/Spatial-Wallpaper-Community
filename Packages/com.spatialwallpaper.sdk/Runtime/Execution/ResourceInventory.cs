using System;using System.Collections.Generic;using System.Linq;using System.Threading;
namespace SpatialWallpaper.Sdk.Execution {
 public sealed class ResourceInventory:IDisposable {
  sealed class Slot {
   internal readonly ResourceInventory Owner;internal readonly string Kind;internal readonly ResourceLease Metadata;
   internal object Native;internal NativeReservation Allocation;internal OwnedHandle Handle;internal bool Released;
   internal Slot(ResourceInventory owner,string kind,ResourceLease metadata){Owner=owner;Kind=kind;Metadata=metadata;}
   ~Slot(){Metadata.Dispose();}
  }
  readonly OwnedDomain domain;readonly IRetirementQueue retirement;readonly Action<object> destroy;readonly HashSet<Slot> slots;readonly ResourceLease registryMemory;readonly int thread;
  readonly List<NativeReservation> uncertainNative;
  bool disposed;
  public ResourceInventory(OwnedDomain owner,IRetirementQueue queue,Action<object> destroyNative){
   domain=owner??throw new ArgumentNullException(nameof(owner));retirement=queue??throw new ArgumentNullException(nameof(queue));destroy=destroyNative??throw new ArgumentNullException(nameof(destroyNative));thread=Thread.CurrentThread.ManagedThreadId;
   registryMemory=domain.Ledger.Reserve(new ResourceCost(checked(4096+domain.Ledger.Limit.Objects*96),0,0,0));
   try{slots=new HashSet<Slot>((int)domain.Ledger.Limit.Objects);uncertainNative=new List<NativeReservation>((int)domain.Ledger.Limit.Objects);registryMemory.Commit();}catch{registryMemory.Dispose();throw;}
  }
  void Live(){if(disposed||Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("native-inventory-state-or-thread");}
  static void Cost(ResourceCost cost){if(cost.Objects!=1||cost.ManagedBytes<64)throw new ArgumentException("native-resource-estimate");}
  Slot Get(OwnedHandle handle,string kind){
   Live();var slot=domain.Unwrap(handle,kind) as Slot;
   if(slot==null||!ReferenceEquals(slot.Owner,this)||slot.Kind!=kind||slot.Released||slot.Native==null)throw new InvalidOperationException("foreign-or-released-native-slot");return slot;
  }
  public object Native(OwnedHandle handle,string kind)=>Get(handle,kind).Native;
  public ResourceCost Allocation(OwnedHandle handle,string kind)=>Get(handle,kind).Allocation.Cost;
  void Failed(NativeReservation allocation,object native,bool entered){
   if(allocation==null)return;
   if(!entered)allocation.AbandonBeforeNative();
   else if(native!=null)allocation.Retire(retirement,native,destroy);
   else uncertainNative.Add(allocation); // Unknown native constructor state: no safe refund before process exit.
  }
  public OwnedHandle Create(string kind,ResourceCost cost,Func<object> factory)=>Create(kind,cost,born=>factory());
  // Report the new wrapper immediately after construction, before any fallible configuration.
  public OwnedHandle Create(string kind,ResourceCost cost,Func<Action<object>,object> factory){
   Live();Cost(cost);retirement.RequireCapacity(1);long checkpoint=domain.Checkpoint();NativeReservation allocation=null;ResourceLease metadata=null;Slot slot=null;object native=null;bool entered=false;
   try{
    allocation=NativeReservation.Reserve(domain.Ledger,cost);metadata=domain.Ledger.Reserve(new ResourceCost(512,0,0,0));
    slot=new Slot(this,kind,metadata);slot.Handle=domain.Register(kind,slot);slots.Add(slot);
    Action<object> born=value=>{if(value==null||native!=null)throw new InvalidOperationException("native-construction-report");allocation.BindManagedOwner(value);native=value;};
    entered=true;object result=factory(born);if(native==null)born(result);else if(!ReferenceEquals(native,result))throw new InvalidOperationException("native-construction-result");
    slot.Native=native;slot.Allocation=allocation;allocation.Commit();metadata.Commit();return slot.Handle;
   }catch{
    if(slot!=null){slot.Released=true;slot.Native=null;slots.Remove(slot);}else metadata?.Dispose();
    domain.Rollback(checkpoint);
    Failed(allocation,native,entered);
    throw;
   }
  }
  public void Replace(OwnedHandle handle,ResourceCost cost,ResourceCost temporaryCopy,Func<object,object> factory,Action<object,object> rebind)=>Replace(handle,cost,temporaryCopy,(old,born)=>factory(old),rebind);
  public void Replace(OwnedHandle handle,ResourceCost cost,ResourceCost temporaryCopy,Func<object,Action<object>,object> factory,Action<object,object> rebind){
   var slot=Get(handle,handle.Kind);Cost(cost);if(temporaryCopy.Objects!=0)throw new ArgumentException("temporary-resource-object-count");
   retirement.RequireCapacity(3);
   NativeReservation allocation=null,temporary=null;object candidate=null;bool entered=false,rebindEntered=false;
   object previous=slot.Native;NativeReservation previousAllocation=slot.Allocation;
   try{
    allocation=NativeReservation.Reserve(domain.Ledger,cost);temporary=NativeReservation.Reserve(domain.Ledger,temporaryCopy);
    Action<object> born=value=>{if(value==null||candidate!=null)throw new InvalidOperationException("native-construction-report");allocation.BindManagedOwner(value);candidate=value;};
    entered=true;object result=factory(previous,born);if(candidate==null)born(result);else if(!ReferenceEquals(candidate,result))throw new InvalidOperationException("native-construction-result");
    rebindEntered=true;rebind(previous,candidate);
    allocation.Commit();temporary.Commit();slot.Native=candidate;slot.Allocation=allocation;
   }catch{
    if(rebindEntered){try{rebind(candidate,previous);}catch{Failed(allocation,candidate,entered);if(temporary!=null)temporary.Retire(retirement,null,destroy);throw new InvalidOperationException("native-rebind-rollback-failed");}}
    Failed(allocation,candidate,entered);
    if(temporary!=null){if(entered)temporary.Retire(retirement,null,destroy);else temporary.AbandonBeforeNative();}
    throw;
   }
   previousAllocation.Retire(retirement,previous,destroy);temporary.Retire(retirement,null,destroy);
  }
  public void Release(OwnedHandle handle){
   if(handle==null){Live();return;}
   var slot=Get(handle,handle.Kind);Release(slot);
  }
  void Release(Slot slot){
   retirement.RequireCapacity(1);
   slot.Released=true;domain.InvalidateValue(slot.Handle);slots.Remove(slot);
   object native=slot.Native;slot.Native=null;var allocation=slot.Allocation;slot.Allocation=null;allocation.Retire(retirement,native,destroy);
  }
  public void Dispose(){
   if(disposed)return;Live();foreach(var slot in slots.ToArray())Release(slot);disposed=true;registryMemory.Dispose();
  }
 }
}
