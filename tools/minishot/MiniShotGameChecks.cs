using System;using System.IO;using System.Reflection;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEngine;
public static class MiniShotGameChecks {
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);Debug.Log("PASS "+reason);}
 public static void Run(){string output=Environment.GetEnvironmentVariable("SDK_MINISHOT_OUTPUT");try{
  var type=Type.GetType("MiniShotSandbox, Assembly-CSharp");Need(type!=null,"SDK-MiniShot-author-missing");var go=new GameObject("Actual author picking checks");var script=go.AddComponent(type);
  type.GetField("centers").SetValue(script,new[]{new Vector3(0,0,.3f),new Vector3(0,0,-.1f),new Vector3(.16f,.05f,.5f)});type.GetField("radii").SetValue(script,new[]{.04f,.02f,.03f});
  var pick=type.GetMethod("Pick");Need(pick!=null,"SDK-MiniShot-picking-missing");
  Func<float,float,Vector3,Vector3,int> run=(x,y,eye,size)=>(int)pick.Invoke(script,new object[]{x,y,eye,size});
  var size=new Vector3(.53f,.298125f,0);var eye=new Vector3(0,0,-.6f);
  Need(run(.5f,.5f,eye,size)==1,"foreground-target-occludes-rear-target");Need(run(.01f,.01f,eye,size)==-1,"empty-space-does-not-hit");
  Need(run(.5f,.5f,new Vector3(0,0,-.08f),size)==0,"target-behind-eye-does-not-hit");
  foreach(var e in new[]{eye,new Vector3(-.16f,.08f,-.35f),new Vector3(.16f,-.08f,-1.1f)})foreach(var s in new[]{size,new Vector3(.8f,.298125f,0),new Vector3(.22f,.298125f,0),new Vector3(.53f,.53f,0)}){
   var center=new Vector3(.16f,.05f,.5f);float distance=-e.z;float x=(distance*center.x+center.z*e.x)/(distance+center.z)/s.x+.5f;float y=.5f-(distance*center.y+center.z*e.y)/(distance+center.z)/s.y;
   Need(run(x,y,e,s)==2,"head-and-window-aware-target-hit");
  }
  UnityEngine.Object.DestroyImmediate(go);File.WriteAllText(Path.Combine(output,"picking.json"),new JObject{{"status","passed-actual-author-picking"},{"checks",15}}.ToString());EditorApplication.Exit(0);
 }catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}}
}
