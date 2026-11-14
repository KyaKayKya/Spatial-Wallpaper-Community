using UnityEngine;
using UnityEditor.SceneManagement;
using System;
using System.IO;
using System.Linq;
public static class PinlightChecks {
 static void Require(bool ok,string text){if(!ok)throw new Exception("FAIL: "+text);}
 public static string Run(){
  var group=GameObject.Find("FixedPinlights");
  Require(group,"two physical ceiling fixtures are missing");
  var rs=group.GetComponentsInChildren<MeshRenderer>();
  Require(rs.Length==2,"expected exactly two fixture meshes");
  var p=UnityEngine.Object.FindObjectOfType<WindowProjection>();
  var original=p.EyeScreen;var camera=p.GetComponent<Camera>();int count=0;
  try{
   foreach(var r in rs){
    var center=p.screenFrame.InverseTransformPoint(r.bounds.center);
    Require(center.z>.02f&&center.z<.08f,"fixture must be inside the screen plane");
    Require(center.y>.13f&&center.y<.16f,"fixture must mount to the ceiling");
    Require(r.sharedMaterials.All(m=>m&&m.GetTexture("_Radiance")),"fixture radiance missing");
   }
   foreach(var eye in new[]{new Vector3(0,0,-.6f),new Vector3(-.2f,0,-.6f),new Vector3(.2f,0,-.6f),new Vector3(0,.12f,-.6f),new Vector3(0,-.12f,-.6f),new Vector3(0,0,-.35f),new Vector3(0,0,-.95f)}){
    Require(p.SetEye(eye),"eye rejected");
    foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
     var q=camera.WorldToViewportPoint(p.screenFrame.TransformPoint(new Vector3(x*p.displayWidth,y*p.displayHeight,0)));
     Require(Vector2.Distance(new Vector2(q.x,q.y),new Vector2(x+.5f,y+.5f))<.00002f,"screen corner moved");count++;
    }
    foreach(var r in rs){
     var point=p.screenFrame.InverseTransformPoint(r.bounds.center);float d=-eye.z;
     var expected=(d*new Vector2(point.x,point.y)+point.z*new Vector2(eye.x,eye.y))/(d+point.z);
     var actual=camera.WorldToViewportPoint(r.bounds.center);
     Require(Mathf.Abs(actual.x-(.5f+expected.x/p.displayWidth))<.00002f&&Mathf.Abs(actual.y-(.5f+expected.y/p.displayHeight))<.00002f,"fixture parallax differs from physical screen geometry");count++;
    }
   }
  }finally{p.SetEye(original);}
  return "PASS: two physical fixtures; "+count+" screen-corner and fixture-depth projections across seven eye positions";
 }
 public static void CheckBatch(){
  EditorSceneManager.OpenScene("Assets/Showcase/Scenes/Showcase.unity");
  File.WriteAllText("../Validation/Showcase/unity/pinlight-checks.txt",Run());
 }
}
