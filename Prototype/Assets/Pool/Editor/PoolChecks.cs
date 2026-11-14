using System;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEditor;

public static class PoolChecks {
 public static void WaterlineBatch(){
  try{
   UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Pool/Scenes/Pool.unity");
   var surface=GameObject.Find("WaterSurface");float scale=surface.transform.lossyScale.x;
   float clearance=float.MaxValue;string closest="";
   foreach(var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())if(r.name.StartsWith("Spiral step")){
    float d=Mathf.Abs(r.bounds.max.y-surface.transform.position.y)/scale;
    if(d<clearance){clearance=d;closest=r.name;}
   }
   var mat=surface.GetComponent<Renderer>().sharedMaterial;
   Require(!mat.IsKeywordEnabled("_DISPLACEMENTMODE_GERSTNER"),"Unexpected water geometry displacement");
   File.WriteAllText("../Validation/Pool/waterline-checks.txt",$"closest={closest}; minimum tread/water clearance in source metres={clearance}");
   Require(clearance>=.08f,"Tread nearly coplanar with animated water: "+clearance);EditorApplication.Exit(0);
  }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
 }
 public static void FrontBatch(){
  try{
   UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Pool/Scenes/Pool.unity");
   var projection=UnityEngine.Object.FindObjectOfType<WindowProjection>();var camera=projection.GetComponent<Camera>();
   float left=GameObject.Find("Left wall").GetComponent<Renderer>().bounds.max.x;
   float right=GameObject.Find("Right wall").GetComponent<Renderer>().bounds.min.x;
   float bottom=GameObject.Find("Underwater floor").GetComponent<Renderer>().bounds.max.y;
   float top=GameObject.Find("Ceiling front").GetComponent<Renderer>().bounds.min.y;
   foreach(var name in new[]{"Left wall","Right wall","Underwater floor","Ceiling front"})
    Require(Mathf.Abs(GameObject.Find(name).GetComponent<Renderer>().bounds.min.z)<.00001f,"Front edge not on screen plane: "+name);
   float maxError=0;
   foreach(var eye in new[]{new Vector3(0,0,-.6f),new Vector3(.2f,0,-.6f),new Vector3(-.2f,0,-.6f),new Vector3(0,.12f,-.6f),new Vector3(0,-.12f,-.6f),new Vector3(0,0,-.35f),new Vector3(0,0,-.95f)}){
    projection.SetEye(eye);
    for(int x=0;x<2;x++)for(int y=0;y<2;y++){
     var p=camera.WorldToViewportPoint(new Vector3(x==0?left:right,y==0?bottom:top,0));
     maxError=Mathf.Max(maxError,Vector2.Distance(new Vector2(p.x,p.y),new Vector2(x,y)));
    }
   }
   File.WriteAllText("../Validation/Pool/front-aperture.txt",$"actual masonry opening: left={left} right={right} bottom={bottom} top={top}; maxViewportError={maxError}");
   Require(maxError<.0001f,"Actual masonry front does not coincide with screen corners: "+maxError);EditorApplication.Exit(0);
  }catch(Exception e){Debug.LogException(e);EditorApplication.Exit(1);}
 }
 public static void Require(bool condition,string reason){if(!condition)throw new Exception("FAIL: "+reason);}
 public static string AssetsCheck(){
  var model=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Pool/Models/pool_static.fbx");
  Require(model,"Pool FBX has not been imported");
  var meshes=model.GetComponentsInChildren<MeshFilter>(true);
  Require(meshes.Length==40,"Expected all 40 source meshes");
  Require(meshes.Sum(m=>(long)m.sharedMesh.triangles.Length/3)==13700,"Mesh triangles differ from source manifest");
  Require(meshes.Count(m=>m.name.IndexOf("ring",StringComparison.OrdinalIgnoreCase)>=0)==1,"Independent ring missing");
  Require(Shader.Find("SpatialWallpaper/PoolWater"),"Water surface shader missing");
  return "PASS: 40 imported meshes, 13700 triangles, independent ring, water shader";
 }
 public static void AssetsBatch(){
  Directory.CreateDirectory("../Validation/Pool");
  try{File.WriteAllText("../Validation/Pool/asset-checks.txt",AssetsCheck());EditorApplication.Exit(0);}
  catch(Exception e){File.WriteAllText("../Validation/Pool/asset-checks.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
 }
 public static void SceneBatch(){
  try {
   Require(File.Exists("Assets/Pool/Scenes/Pool.unity"),"Pool scene missing");
   UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Pool/Scenes/Pool.unity");
   var floor=GameObject.Find("Underwater floor").GetComponent<MeshRenderer>().bounds;
   Require(floor.size.y<.01f&&floor.center.y<-.15f,"Source up axis was not converted to Unity Y");
   Require(GameObject.Find("Back wall").GetComponent<MeshRenderer>().bounds.center.z>.4f,"Source depth was not converted to positive Z");
   var p=UnityEngine.Object.FindObjectOfType<WindowProjection>();Require(p,"Shared projection missing");
   Require(UnityEngine.Object.FindObjectsOfType<WindowProjection>().Length==1,"Multiple display rigs");
   var cam=p.GetComponent<Camera>();
   foreach(var eye in new[]{new Vector3(0,0,-.6f),new Vector3(.2f,0,-.6f),new Vector3(-.2f,0,-.6f),new Vector3(0,.12f,-.6f),new Vector3(0,-.12f,-.6f),new Vector3(0,0,-.35f),new Vector3(0,0,-.95f)}){
    Require(p.SetEye(eye),"Valid eye rejected");
    foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
     var q=cam.WorldToViewportPoint(p.screenFrame.TransformPoint(new Vector3(x*p.displayWidth,y*p.displayHeight,0)));
     Require(Vector2.Distance(new Vector2(q.x,q.y),new Vector2(x+.5f,y+.5f))<.0001f,"Screen edge drift");
    }
    Require(Quaternion.Angle(cam.transform.rotation,p.screenFrame.rotation)<.001f,"Camera rotates toward scene");
    foreach(var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())Require(cam.WorldToViewportPoint(r.bounds.center).z+r.bounds.extents.magnitude<cam.farClipPlane,"Far clip intersects "+r.name);
   }
   Require(GameObject.Find("PoolContent"),"Replaceable content root missing");
   Require(GameObject.Find("WaterSurface"),"Water missing");
   Require(GameObject.Find("WaterFront"),"Underwater cutaway missing");
   var state=UnityEngine.Object.FindObjectOfType<PoolMaterialState>();
   Require(state&&state.waterSurface==GameObject.Find("WaterSurface").transform,"Water height must bind to live surface");
   foreach(var renderer in GameObject.Find("PoolArchitecture").GetComponentsInChildren<MeshRenderer>()){
    var mat=renderer.sharedMaterial;
    Require(mat.shader.name=="Universal Render Pipeline/Lit","Architecture must use baked URP Lit: "+renderer.name);
    Require(renderer.lightmapIndex>=0&&renderer.lightmapIndex<LightmapSettings.lightmaps.Length,"Missing Bakery map on "+renderer.name);
    var mesh=renderer.GetComponent<MeshFilter>().sharedMesh;
    Require(mesh.uv2.Length==mesh.vertexCount,"Missing baked UV on "+renderer.name);
   }
   var cadence=p.GetComponent<AdaptiveRenderCadence>();
   Require(GameObject.Find("WaterSurface").GetComponent<Renderer>().sharedMaterial.shader.name.Contains("URPWater"),"Purchased water shader missing");
   var demand=typeof(AdaptiveRenderCadence).GetMethod("RequestContinuous");
   Require(demand!=null,"Animated content cannot request continuous rendering");
   var token=new GameObject("TestDemand");
   demand.Invoke(cadence,new object[]{token,true});cadence.SendMessage("LateUpdate");
   Require(UnityEngine.Rendering.OnDemandRendering.renderFrameInterval==1,"Water freezes at stationary eye");
   demand.Invoke(cadence,new object[]{token,false});UnityEngine.Object.DestroyImmediate(token);
   var flags=System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance;
   typeof(AdaptiveRenderCadence).GetField("lastMove",flags).SetValue(cadence,-100f);
   typeof(AdaptiveRenderCadence).GetField("anchor",flags).SetValue(cadence,p.EyeScreen);
   cadence.SendMessage("LateUpdate");Require(UnityEngine.Rendering.OnDemandRendering.renderFrameInterval==cadence.idleInterval,"Released animation demand leaves static content rendering");
   var mirror=PoolPlanarReflection.Mirror(.2f);
   Require(Vector3.Distance(mirror.MultiplyPoint(new Vector3(.1f,.6f,-.4f)),new Vector3(.1f,-.2f,-.4f))<.00001f,"Reflected eye incorrect");
   Require(Vector3.Distance(mirror.MultiplyPoint(new Vector3(.3f,.2f,.4f)),new Vector3(.3f,.2f,.4f))<.00001f,"Water plane moves under reflection");
   foreach(var r in UnityEngine.Object.FindObjectsOfType<MeshRenderer>())foreach(var material in r.sharedMaterials)
    Require(material&&material.shader&&material.shader.isSupported&&!ShaderUtil.ShaderHasError(material.shader),"Unsupported material on "+r.name);
   File.WriteAllText("../Validation/Pool/scene-checks.txt","PASS: seven eyes, aperture, far clip, modular content and animation demand\n"+ShowcaseChecks.Run());
   EditorApplication.Exit(0);
  }catch(Exception e){File.WriteAllText("../Validation/Pool/scene-checks.txt",e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}
 }
}
