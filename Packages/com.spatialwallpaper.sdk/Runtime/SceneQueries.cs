using System;
using UnityEngine;

namespace SpatialWallpaper.Sdk {
 public static class SceneQueries {
  public const int Unknown=0,Pending=1,Ready=2,Failed=3;
  static Func<string,object[],object> play;
  internal static void BindPlay(Func<string,object[],object> session){play=session;}
  static object Call(string operation,params object[] arguments)=>(play??throw new InvalidOperationException("SDK native query Play session is not running"))(operation,arguments);
  public static int BeginBounds(GameObject node)=>(int)Call("BeginBounds",node);
  public static int BeginRaycast(GameObject node,Vector3 origin,Vector3 direction,float distance)=>(int)Call("BeginRaycast",node,origin,direction,distance);
  public static int BeginAnimatorState(GameObject node,int layer)=>(int)Call("BeginAnimatorState",node,layer);
  public static int State(int request)=>(int)Call("QueryState",request);
  public static string Error(int request)=>(string)Call("QueryError",request);
  public static Vector3 ReadVector(int request,string field)=>(Vector3)Call("QueryVector",request,field);
  public static float ReadFloat(int request,string field)=>(float)Call("QueryFloat",request,field);
  public static int ReadInt(int request,string field)=>(int)Call("QueryInt",request,field);
  public static bool ReadBool(int request,string field)=>(bool)Call("QueryBool",request,field);
  public static GameObject ReadNode(int request)=>(GameObject)Call("QueryNode",request);
  public static void Release(int request){Call("QueryRelease",request);}
 }
}
