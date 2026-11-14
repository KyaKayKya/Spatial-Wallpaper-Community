using System;using System.IO;using System.Linq;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEngine;using UnityEngine.Rendering;
public static class MiniShotGeometryChecks {
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static void Capture(Camera camera,string file){
  var target=new RenderTexture(800,450,24,RenderTextureFormat.ARGB32);var image=new Texture2D(800,450,TextureFormat.RGB24,false);var previous=RenderTexture.active;
  try{Need(target.Create(),"MiniShot-capture-target");camera.rect=new Rect(0,0,1,1);camera.targetTexture=target;var request=new RenderPipeline.StandardRequest{destination=target};for(int i=0;i<3;i++)RenderPipeline.SubmitRenderRequest(camera,request);RenderTexture.active=target;image.ReadPixels(new Rect(0,0,800,450),0,0);image.Apply();File.WriteAllBytes(file,image.EncodeToPNG());}
  finally{camera.targetTexture=null;RenderTexture.active=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);UnityEngine.Object.DestroyImmediate(image);}
 }
 public static void Run(){string output=Environment.GetEnvironmentVariable("SDK_MINISHOT_OUTPUT");try{
  var definition=MiniShotSdkBuilder.Create();var author=definition.GetComponent<MiniShotSandbox>();var projection=UnityEngine.Object.FindObjectOfType<WindowProjection>();projection.viewportManagedExternally=true;var camera=projection.GetComponent<Camera>();
  var meshes=definition.GetComponentsInChildren<MeshFilter>().Where(value=>value.name!="Save status").ToArray();Need(meshes.Length==25&&author.centers.Count(value=>value.z<0)==2,"MiniShot-original-visible-inventory");var matrices=meshes.Select(value=>value.transform.localToWorldMatrix).ToArray();
  var floor=meshes.Single(value=>value.name=="Floor");var floorPoints=floor.sharedMesh.vertices.Select(value=>floor.transform.TransformPoint(value)).ToArray();Need(floorPoints.All(value=>Mathf.Abs(value.y+definition.authorFrame.y*.5f)<.000001f),"MiniShot-real-floor-authored-height");var front=floorPoints.Where(value=>Mathf.Abs(value.z)<.000001f).ToArray();Need(front.Length>=2,"MiniShot-real-floor-starts-at-window");
  var eyes=new[]{new Vector3(0,0,-.6f),new Vector3(-.18f,0,-.6f),new Vector3(.18f,0,-.6f),new Vector3(0,.1f,-.6f),new Vector3(0,-.1f,-.6f),new Vector3(0,0,-.25f),new Vector3(0,0,-1.2f)};
  var pixels=new[]{new Vector2Int(1600,900),new Vector2Int(1600,1000),new Vector2Int(1200,900),new Vector2Int(1600,450),new Vector2Int(540,960)};var cases=new JArray();float worst=0;
  foreach(var px in pixels)foreach(bool preserveWidth in new[]{false,true})foreach(var physicalEye in eyes){
   float physicalHeight=.298125f,physicalWidth=physicalHeight*px.x/px.y;float scale=preserveWidth?physicalWidth/definition.authorFrame.x:physicalHeight/definition.authorFrame.y;
   projection.displayWidth=physicalWidth/scale;projection.displayHeight=physicalHeight/scale;camera.pixelRect=new Rect(0,0,px.x,px.y);Need(projection.SetScaledEye(physicalEye/scale,scale),"MiniShot-legal-physical-eye");float error=0;
   foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){var point=new Vector3(x*projection.displayWidth,y*projection.displayHeight,0);var actual=camera.WorldToViewportPoint(point);error=Mathf.Max(error,new Vector2((actual.x-x-.5f)*px.x,(actual.y-y-.5f)*px.y).magnitude);}
   Need(error<.01f,"MiniShot-native-window-corner-error");worst=Mathf.Max(worst,error);
   if(!preserveWidth){Need(front.Min(value=>value.x)<=-projection.displayWidth*.5f+.000001f&&front.Max(value=>value.x)>=projection.displayWidth*.5f-.000001f,"MiniShot-floor-covers-effective-lower-corners");foreach(float x in new[]{-.5f,.5f}){var p=camera.WorldToViewportPoint(new Vector3(x*projection.displayWidth,-definition.authorFrame.y*.5f,0));Need(Mathf.Abs(p.y)*px.y<.01f,"MiniShot-real-floor-attached-to-bottom");}}
   for(int i=0;i<meshes.Length;i++)for(int j=0;j<16;j++)Need(meshes[i].transform.localToWorldMatrix[j]==matrices[i][j],"MiniShot-content-moved-with-eye");
   cases.Add(new JObject{{"pixels",new JArray(px.x,px.y)},{"preserve_width",preserveWidth},{"physical_eye",new JArray(physicalEye.x,physicalEye.y,physicalEye.z)},{"corner_error_px",error},{"floor_bottom_checked",!preserveWidth}});
  }
  projection.displayWidth=.53f;projection.displayHeight=.298125f;string[] names={"front","left","right","up","down","near","far"};for(int i=0;i<eyes.Length;i++){Need(projection.SetScaledEye(eyes[i],1),"MiniShot-capture-eye");Capture(camera,Path.Combine(output,names[i]+".png"));}
  var hero=author.centers.OrderBy(value=>value.z).First();Need(projection.SetEye(new Vector3(0,0,hero.z+.02f)),"MiniShot-past-front-target-eye");Need(camera.WorldToViewportPoint(hero).z<0,"MiniShot-front-target-clips-behind-eye");
  File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"status","passed-selected-MiniShot-native-geometry"},{"cases",cases},{"max_corner_error_px",worst},{"real_meshes",meshes.Length},{"foreground_targets",2},{"content_transforms_unchanged",true},{"rendered_images",new JArray(names.Select(name=>name+".png"))},{"graphics",SystemInfo.graphicsDeviceName},{"scope","actual author scene meshes, native Unity camera and selected rendered eyes; not LPAC, full visual support or pixel equality"}}.ToString());EditorApplication.Exit(0);
 }catch(Exception e){File.WriteAllText(Path.Combine(output,"error.txt"),e.ToString());Debug.LogException(e);EditorApplication.Exit(1);}}
}
