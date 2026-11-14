using System;
using System.IO;
using UnityEngine;
public static class ParkingChecks {
 public static void RunBatch(){Directory.CreateDirectory("../Validation/Parking");File.WriteAllText("../Validation/Parking/checks.txt",Run());}
 public static string Run(){
  string original=ShowcaseChecks.Run();
  CheckPhysicalBox();
  var concrete=UnityEditor.AssetDatabase.LoadAssetAtPath<Material>("Assets/Parking/Materials/Weathered_clean_concrete_plinth.mat");
  if(concrete)Require(concrete.GetTexture("_BaseMap")&&concrete.GetTexture("_BumpMap")&&concrete.GetFloat("_Metallic")==0,"Concrete scan or nonmetal response missing");
  Require(typeof(SpatialWallpaper.Parking.ParkingAtmosphere).GetMethod("SetTelevisionsVisible")!=null,"Independent TV visibility API missing");
  var transformBounds=typeof(SpatialWallpaper.Parking.ParkingLampField).GetMethod("TransformBounds");
  Require(transformBounds!=null,"Rotation-safe lamp bounds missing");
  var source=new Bounds(new Vector3(2,3,4),new Vector3(2,7,1));
  var matrix=Matrix4x4.TRS(new Vector3(.5f,-.2f,1),Quaternion.Euler(18,43,27),Vector3.one*.044f);
  var world=(Bounds)transformBounds.Invoke(null,new object[]{source,matrix});
  foreach(float x in new[]{-1f,1f})foreach(float y in new[]{-1f,1f})foreach(float z in new[]{-1f,1f}){
   var corner=matrix.MultiplyPoint3x4(source.center+Vector3.Scale(source.extents,new Vector3(x,y,z)));
   Require(Vector3.Distance(world.ClosestPoint(corner),corner)<.00001f,"Transformed bounds excludes a visible corner");
  }
  var go=new GameObject("ParkingProjectionTest");go.AddComponent<Camera>();var p=go.AddComponent<WindowProjection>();
  try{
   var field=typeof(WindowProjection).GetField("contentDepth");
   Require(field!=null,"Configurable contentDepth missing; parking field is clipped at cabinet depth");
   Require((float)field.GetValue(p)==2,"Default showcase depth changed");
   field.SetValue(p,40f);
   foreach(var eye in new[]{new Vector3(0,0,-.6f),new Vector3(.2f,.1f,-.4f),new Vector3(-.2f,-.12f,-.9f)}){
    Require(p.SetEye(eye),"Valid depth rejected");
    Require(Mathf.Abs(go.GetComponent<Camera>().farClipPlane-(40-eye.z))<.001f,"Far plane incorrect");
    var q=go.GetComponent<Camera>().WorldToViewportPoint(new Vector3(.1f,0,22));
    float analytic=.5f+((-eye.z*.1f+22*eye.x)/(22-eye.z))/.53f;
    Require(Mathf.Abs(q.x-analytic)<.00002f,"Deep geometry parallax incorrect");
   }
   var before=go.GetComponent<Camera>().projectionMatrix;
   foreach(float bad in new[]{0f,-1f,float.NaN,float.PositiveInfinity}){
    field.SetValue(p,bad);Require(!p.SetEye(new Vector3(0,0,-.6f)),"Invalid content depth accepted");
    Require(before==go.GetComponent<Camera>().projectionMatrix,"Invalid depth mutated projection");
   }
   return original+"\nPASS: parking depth, default cabinet depth, deep parallax, invalid depth freeze and actual box corners at 0.2/0.53/1/2 metre display widths";
  }finally{UnityEngine.Object.DestroyImmediate(go);}
 }
 static void CheckPhysicalBox(){
  var frame=new GameObject("BoxScaleTest");var model=new GameObject("Content");model.transform.SetParent(frame.transform,false);
  var box=model.AddComponent<DisplaySceneVolume>();box.size=new Vector3(.53f,.298125f,6.89f);
  var camera=new GameObject("BoxScaleCamera");camera.AddComponent<Camera>();var projection=camera.AddComponent<WindowProjection>();projection.screenFrame=frame.transform;
  var rig=frame.AddComponent<PhysicalDisplayRig>();rig.projection=projection;rig.sceneModel=model.transform;
  try{
   foreach(float width in new[]{.2f,.53f,1f,2f}){
    Require(rig.Configure(new PhysicalDisplayRig.Profile{widthMetres=width,heightMetres=width*9/16}),"Display profile rejected");
    foreach(var eye in new[]{new Vector3(0,0,-.6f),new Vector3(.2f,.1f,-.4f),new Vector3(-.2f,-.1f,-.9f)}){
     projection.SetEye(eye);
     foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
      var corner=box.FrontCorner(x,y);Require(Vector3.Distance(corner,new Vector3(x*width,y*width*9/16,0))<.000001f,"Scaled box leaves display");
      var q=camera.GetComponent<Camera>().WorldToViewportPoint(corner);Require(new Vector2(q.x-x-.5f,q.y-y-.5f).magnitude<.00001f,"Scaled box corner leaves viewport");
     }
    }
   }
  }finally{UnityEngine.Object.DestroyImmediate(camera);UnityEngine.Object.DestroyImmediate(frame);}
 }
 static void Require(bool ok,string msg){if(!ok)throw new Exception(msg);}
}
