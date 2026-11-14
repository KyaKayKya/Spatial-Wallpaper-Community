using System;using UnityEngine;
namespace SpatialWallpaper.Sdk {
 public static class SceneResources {
  public const int Unknown=0,Pending=1,Ready=2,Applied=3,Canceled=4,Failed=5;
  static Func<string,object[],object> play;
  internal static void BindPlay(Func<string,object[],object> session){play=session;}
  static object Call(string action,params object[] args)=>(play??throw new InvalidOperationException("Native resource Play session is not bound"))(action,args);
  public static int BeginMesh(GameObject node,int vertices,int[] submeshIndexCounts)=>(int)Call("ResourceBeginMesh",node,vertices,submeshIndexCounts);
  public static int BeginTexture(Material material,string property,int width,int height)=>(int)Call("ResourceBeginTexture",material,property,width,height);
  public static void WriteFloats(int request,int byteOffset,float[] data){Call("ResourceWriteFloats",request,byteOffset,data);}
  public static void WriteIndices(int request,int byteOffset,int[] data){Call("ResourceWriteIndices",request,byteOffset,data);}
  public static void WriteBytes(int request,int byteOffset,int[] data){Call("ResourceWriteBytes",request,byteOffset,data);}
  public static void Seal(int request){Call("ResourceSeal",request);}
  public static int State(int request)=>(int)Call("ResourceState",request);
  public static string Error(int request)=>(string)Call("ResourceError",request);
  public static void Apply(int request){Call("ResourceApply",request);}
  public static void Cancel(int request){Call("ResourceCancel",request);}
  public static void Release(int request){Call("ResourceRelease",request);}
 }
}
