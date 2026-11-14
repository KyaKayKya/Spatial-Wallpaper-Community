using System;
using System.IO;
using UnityEngine;
using UnityEditor.SceneManagement;

public static class MiniShotChecks {
 public static void Validate(){
  const string scene="Assets/MiniShotGame/Scenes/MiniShotGame.unity";
  if(!File.Exists(scene))throw new Exception("MiniShotGame scene has not been built");
  EditorSceneManager.OpenScene(scene);
  var p=UnityEngine.Object.FindObjectOfType<WindowProjection>();
  var box=UnityEngine.Object.FindObjectOfType<DisplaySceneVolume>();
  if(!p||!box)throw new Exception("MiniShotGame needs a fixed screen and projection");
  var content=GameObject.Find("MiniShotContent");
  if(!content)throw new Exception("MiniShotGame content reference missing");
  var meshes=content.GetComponentsInChildren<MeshFilter>();
  if(meshes.Length!=28)throw new Exception("Expected 28 real geometry objects");
  int targets=0,foreground=0;bool left=false,right=false;
  foreach(var mesh in meshes){
   if(mesh.name=="Floor")foreach(var v in mesh.sharedMesh.vertices){
    var w=mesh.transform.TransformPoint(v);
    if(Mathf.Abs(box.transform.InverseTransformPoint(w).y+box.size.y/2)>.000001f)throw new Exception("Actual floor height differs from box bottom");
    left|=Vector3.Distance(w,box.FrontCorner(-.5f,-.5f))<.000001f;
    right|=Vector3.Distance(w,box.FrontCorner(.5f,-.5f))<.000001f;
   }
   if(char.IsDigit(mesh.name[0])){
    targets++;var b=mesh.sharedMesh.bounds;
    if(Mathf.Abs(b.size.x-b.size.y)>.00001f)throw new Exception("Stretched target "+mesh.name);
    if(p.screenFrame.InverseTransformPoint(mesh.transform.TransformPoint(b.center)).z<0)foreground++;
   }
  }
  if(!left||!right||targets!=10||foreground!=2)throw new Exception("Floor/target contract failed");
  if(box.GetComponent<Renderer>()||box.GetComponent<Collider>()||box.showGuide)throw new Exception("Reference box must be hidden");
  float error=0;
  foreach(var eye in new[]{new Vector3(0,0,-.6f),new Vector3(-.2f,0,-.6f),new Vector3(.2f,0,-.6f),new Vector3(0,-.1f,-.6f),new Vector3(0,.1f,-.6f),new Vector3(0,0,-.25f),new Vector3(0,0,-1.2f)}){
   if(!p.SetEye(eye))throw new Exception("Legal eye rejected");
   foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
    var q=p.GetComponent<Camera>().WorldToViewportPoint(box.FrontCorner(x,y));
    error=Mathf.Max(error,new Vector2((q.x-x-.5f)*1600,(q.y-y-.5f)*900).magnitude);
   }
  }
  if(error>=.01f)throw new Exception("Fixed window corner error "+error);
  p.SetEye(p.referenceEye);
  string report="{\"pass\":true,\"meshes\":28,\"targets\":10,\"foregroundTargets\":2,\"actualFloorCorners\":true,\"maxCornerErrorPx\":"+error.ToString(System.Globalization.CultureInfo.InvariantCulture)+"}";
  Directory.CreateDirectory("../Validation/MiniShotGame");
  File.WriteAllText("../Validation/MiniShotGame/editor-checks.json",report);
  Debug.Log("MINISHOT_CHECKS_OK "+report);
 }
}
