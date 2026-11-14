using System;using System.Runtime.CompilerServices;
namespace SpatialWallpaper.Sdk.Execution {
 // Charges persistent managed layouts to their actual owner, including after logical invalidation.
 internal sealed class ManagedReservation {
  sealed class Lifetime {
   internal readonly ResourceLease Lease;
   internal Lifetime(ResourceLease lease){Lease=lease;}
   ~Lifetime(){Lease.Dispose();}
  }
  static readonly ConditionalWeakTable<object,Lifetime> owners=new ConditionalWeakTable<object,Lifetime>();
  readonly Lifetime lifetime;bool bound;
  internal ManagedReservation(ResourceLedger ledger,long bytes,long objects=0){
   if(bytes<64)throw new ArgumentOutOfRangeException(nameof(bytes));
   var lease=ledger.Reserve(new ResourceCost(bytes,0,0,objects));
   try{lifetime=new Lifetime(lease);}catch{lease.Dispose();throw;}
  }
  internal void Bind(object owner){
   if(owner==null||bound)throw new InvalidOperationException("managed-reservation-owner");
   owners.Add(owner,lifetime);bound=true;lifetime.Lease.Commit();
  }
 }
}
