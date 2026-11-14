namespace SpatialWallpaper.Sdk.Execution {
 // Keep transient allocation charges until GC confirms this completed frame is unreachable.
 internal sealed class AllocationFrame {
  readonly ResourceLease lease;
  AllocationFrame(ResourceLease reservation){lease=reservation;}
  internal static AllocationFrame Begin(ResourceLedger ledger){
   var reservation=ledger.Reserve(new ResourceCost(192,0,0,0));
   try{var frame=new AllocationFrame(reservation);reservation.Commit();return frame;}catch{reservation.Dispose();throw;}
  }
  internal void Charge(long bytes)=>lease.Grow(new ResourceCost(bytes,0,0,0));
  ~AllocationFrame(){lease.Dispose();}
 }
}
