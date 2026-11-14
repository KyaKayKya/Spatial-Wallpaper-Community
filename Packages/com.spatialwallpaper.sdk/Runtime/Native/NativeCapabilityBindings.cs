using System;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Execution;
namespace SpatialWallpaper.Sdk.Native {
 // Pure CLR author facade. Storage owns a separate asynchronous capability lane;
 // view reads only the latest trusted tick snapshot and never requests a camera RPC.
 public sealed class NativeCapabilityBindings:IVmHost {
  readonly NativeInputBindings input;readonly IVmHost storage;NativeViewSnapshot view;
  public NativeCapabilityBindings(OwnedDomain domain,Func<long> clock,IVmHost storage=null){input=new NativeInputBindings(domain,clock);this.storage=storage;}
  public static bool IsView(string member)=>member=="SpatialWallpaper.Sdk::View::GetEye()->UnityEngine.Vector3"||member=="SpatialWallpaper.Sdk::View::GetSize()->UnityEngine.Vector3";
  public static bool IsStorage(string member)=>member!=null&&member.StartsWith("SpatialWallpaper.Sdk::Storage::",StringComparison.Ordinal)&&(member=="SpatialWallpaper.Sdk::Storage::BeginRead(System.String)->System.Int32"||member=="SpatialWallpaper.Sdk::Storage::BeginWriteText(System.String,System.String,System.String)->System.Int32"||member=="SpatialWallpaper.Sdk::Storage::State(System.Int32)->System.Int32"||member=="SpatialWallpaper.Sdk::Storage::Version(System.Int32)->System.String"||member=="SpatialWallpaper.Sdk::Storage::Error(System.Int32)->System.String"||member=="SpatialWallpaper.Sdk::Storage::TakeText(System.Int32)->System.String"||member=="SpatialWallpaper.Sdk::Storage::Release(System.Int32)->System.Boolean");
  public static bool SupportsMember(string member)=>NativeInputBindings.SupportsMember(member)||IsView(member)||IsStorage(member);
  public bool Supports(string member)=>SupportsMember(member);
  public void Advance(TickInput tick){input.Advance(tick);view=tick.View;}
  public object Resolve(string type,JToken value,int scope)=>throw new NotSupportedException("native-capability-has-no-object-references");
  public object Call(string member,object receiver,object[] args){
   if(IsView(member)){if(view==null)throw new InvalidOperationException("view-unavailable");return member.Contains("GetEye")?new VectorValue("vector3",view.EyeX,view.EyeY,view.EyeZ):new VectorValue("vector3",view.Width,view.Height,0);}
   if(IsStorage(member)){if(storage==null)throw new InvalidOperationException("native-storage-not-configured");return storage.Call(member,receiver,args);}
   return input.Call(member,receiver,args);
  }
 }
 public sealed class NativeViewSnapshot {
  public float EyeX{get;}public float EyeY{get;}public float EyeZ{get;}public float Width{get;}public float Height{get;}
  public NativeViewSnapshot(float eyeX,float eyeY,float eyeZ,float width,float height){
   foreach(float value in new[]{eyeX,eyeY,eyeZ,width,height})if(float.IsNaN(value)||float.IsInfinity(value)||Math.Abs(value)>100000)throw new ArgumentException("native-view-value");
   if(width<=0||height<=0)throw new ArgumentException("native-view-size");EyeX=eyeX;EyeY=eyeY;EyeZ=eyeZ;Width=width;Height=height;
  }
 }
}
