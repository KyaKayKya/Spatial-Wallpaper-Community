using System;using System.Runtime.CompilerServices;
namespace SpatialWallpaper.Sdk.Execution {
 // Native/GPU retirement and managed collection are different lifetime boundaries.
 internal sealed class NativeReservation {
  sealed class ManagedOwner {
   internal readonly ResourceLease Lease;
   internal ManagedOwner(ResourceLease lease){Lease=lease;}
   ~ManagedOwner(){Lease.Dispose();}
  }
  static readonly ConditionalWeakTable<object,ManagedOwner> managedOwners=new ConditionalWeakTable<object,ManagedOwner>();
  readonly ManagedOwner managed;internal readonly ResourceLease NativeLease;internal readonly ResourceCost Cost;
  NativeReservation(ResourceLease native,ManagedOwner body,ResourceCost cost){NativeLease=native;managed=body;Cost=cost;}
  internal static NativeReservation Reserve(ResourceLedger ledger,ResourceCost requested){
   long bytes=Math.Max(256,requested.ManagedBytes);ResourceLease native=null,body=null;ManagedOwner owner=null;
   try{
    native=ledger.Reserve(new ResourceCost(0,requested.NativeBytes,requested.GpuBytes,requested.Objects));
    body=ledger.Reserve(new ResourceCost(bytes,0,0,0));owner=new ManagedOwner(body);
    return new NativeReservation(native,owner,new ResourceCost(bytes,requested.NativeBytes,requested.GpuBytes,requested.Objects));
   }catch{native?.Dispose();if(owner==null)body?.Dispose();throw;}
  }
  internal void Commit(){NativeLease.Commit();managed.Lease.Commit();}
  internal void BindManagedOwner(object native){
   if(native==null)throw new ArgumentNullException(nameof(native));
   try{managedOwners.Add(native,managed);}catch(ArgumentException error){throw new InvalidOperationException("native-wrapper-already-owned",error);}
  }
  internal void AbandonBeforeNative(){NativeLease.Dispose();} // ManagedOwner releases only after GC.
  internal void Retire(IRetirementQueue queue,object native,Action<object> destroy)=>queue.Retire(native,NativeLease,destroy,this);
 }
}
