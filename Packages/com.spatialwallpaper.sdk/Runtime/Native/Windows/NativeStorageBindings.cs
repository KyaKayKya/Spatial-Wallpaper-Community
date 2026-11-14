using System;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Execution;
using SpatialWallpaper.Sdk.Capabilities;
namespace SpatialWallpaper.Sdk.Native.Windows {
 public sealed class NativeStorageBindings:IVmHost {
  readonly CapabilityBindings bindings;
  public NativeStorageBindings(CapabilityCalls calls){bindings=new CapabilityBindings(calls);}
  public bool Supports(string member)=>NativeCapabilityBindings.IsStorage(member);
  public object Resolve(string type,JToken value,int scope)=>throw new NotSupportedException("native-storage-has-no-object-references");
  public object Call(string member,object receiver,object[] args){if(!Supports(member))throw new InvalidOperationException("native-storage-member");return bindings.Call(member,args);}
 }
}
