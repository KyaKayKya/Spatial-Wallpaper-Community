using System;
namespace SpatialWallpaper.Sdk.Execution {
 public interface IRetirementQueue {
  // Queue must retain the lease until native destruction and a verified safe rendering boundary.
  void RequireCapacity(int entries);
  void Retire(object native,ResourceLease lease,Action<object> destroy,object retained=null);
 }
}
