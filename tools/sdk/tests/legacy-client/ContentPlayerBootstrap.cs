using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Content;
using SpatialWallpaper.Sdk;
using UnityEngine;

public sealed class ContentPlayerBootstrap:MonoBehaviour {
 public Camera displayCamera;
 readonly ContentLoader loader=new ContentLoader();readonly List<string> errors=new List<string>();
 WindowProjection projection;ContentHandle handle;ViewportMetrics viewport;JObject report;string reportPath;
 float viewportReadyAfter;
 Vector3 authorFrame;
 public static string Argument(string name){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,name);return i>=0&&i+1<args.Length?args[i+1]:null;}
 void Awake(){Application.logMessageReceived+=Log;Application.runInBackground=true;}
 void Log(string condition,string stack,LogType type){if(type==LogType.Error||type==LogType.Assert||type==LogType.Exception)errors.Add(type+": "+condition);}
 IEnumerator Start(){
  string packagePath=Argument("--content-package");reportPath=Argument("--content-report");if(packagePath==null)yield break;
  report=new JObject{["passed"]=false,["camera_started"]=false,["cases"]=new JArray()};
  Check("external-shader-absent-before-load",Shader.Find("SdkSample/External")==null);
  ValidatedPackage package=null;
  try {package=PackageValidator.Validate(packagePath);var frame=package.Scene["frame"];authorFrame=new Vector3((float)frame[0],(float)frame[1],(float)frame[2]);projection=displayCamera.GetComponent<WindowProjection>();if(!projection)projection=displayCamera.gameObject.AddComponent<WindowProjection>();projection.viewportManagedExternally=true;}
  catch(Exception e){Fail(e);yield break;}
  // Windows initialization has a time budget, independent of rendered FPS.
  // The explicit test flag exercises delayed readiness with this same binary.
  bool delay=Argument("--content-test-delay-viewport")=="true";float start=Time.realtimeSinceStartup;viewportReadyAfter=start+(delay?2.5f:0);
  while(viewport==null&&Time.realtimeSinceStartup-start<10){Measure();if(viewport==null)yield return new WaitForSecondsRealtime(.05f);}
  if(viewport==null){Fail(new InvalidOperationException("viewport-unavailable:"+DisplayViewport.Diagnostic));yield break;}
  var task=loader.LoadAsync(new SceneLoadRequest{Package=package,Camera=displayCamera,ViewportWidth=viewport.Width,ViewportHeight=viewport.Height},CancellationToken.None);
  while(!task.IsCompleted)yield return null;
  if(task.IsFaulted||task.IsCanceled){Fail(task.Exception??new Exception("load-cancelled"));yield break;}
  handle=task.Result;
  if(!Measure()){var cleanup=loader.UnloadAsync(handle);while(!cleanup.IsCompleted)yield return null;Fail(new InvalidOperationException("projection-context-rejected"));yield break;}
  yield return new WaitForEndOfFrame();
  if(reportPath==null)yield break;
  report["viewport_wait_seconds"]=Time.realtimeSinceStartup-start;Check("bounded-viewport-readiness",!delay||Time.realtimeSinceStartup-start>=2.5f);
  var initialEye=viewport.ToView(new[]{0.0,0,-.67});Check("actual-camera-consumes-viewport-eye-once",(projection.EyeScreen-new Vector3((float)initialEye[0],(float)initialEye[1],(float)initialEye[2])).sqrMagnitude<1e-12f);
  Check("package-first-frame",handle.Root.activeInHierarchy&&handle.Root.GetComponentsInChildren<MeshRenderer>().Length>0);
  Check("external-shader-loaded",handle.Root.GetComponentsInChildren<MeshRenderer>().Any(r=>r.sharedMaterials.Any(m=>m.shader.name=="SdkSample/External")));
  var external=handle.Root.GetComponentsInChildren<MeshRenderer>().SelectMany(r=>r.sharedMaterials).Single(m=>m.shader.name=="SdkSample/External");
  Check("texture-transform-roundtrip",external.GetTextureScale("_Texture")==new Vector2(2,3)&&external.GetTextureOffset("_Texture")==new Vector2(.125f,.25f));
  Check("cubemap-data-reconstructed",external.GetTexture("_Cube") is Cubemap);
  Check("cubemap-mips-and-sampling-preserved",external.GetTexture("_Cube").mipmapCount==3&&external.GetTexture("_Cube").anisoLevel==4);
  var image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);image.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);image.Apply();
  int orange=image.GetPixels32().Count(c=>c.r>90&&c.r>c.g*1.5f&&c.b<c.r*.4f);Destroy(image);report["external_shader_orange_pixels"]=orange;Check("external-shader-real-pixels",orange>100);
  Check("texture-and-lightmap-reconstructed",LightmapSettings.lightmaps.Length==1&&LightmapSettings.lightmaps[0].lightmapColor!=null);
  Check("non-directional-lightmap-mode-preserved",LightmapSettings.lightmapsMode==LightmapsMode.NonDirectional);
  float error=0;
  foreach(bool keep in new[]{true,false})foreach(var size in new[]{new Vector2(.53f,.298125f),new Vector2(.53f,.33125f),new Vector2(.53f,.3975f),new Vector2(.70f,.298125f),new Vector2(.20f,.355555f)}){
   float scale=(float)Framing.Scale((double)package.Scene["frame"][0],(double)package.Scene["frame"][1],size.x,size.y,keep);handle.Root.transform.localScale=Vector3.one*scale;projection.displayWidth=size.x;projection.displayHeight=size.y;projection.contentDepth=(float)package.Scene["frame"][2]*scale;
   foreach(var eye in new[]{new Vector3(-.12f,0,-.67f),new Vector3(.12f,0,-.67f),new Vector3(0,.08f,-.67f),new Vector3(0,-.08f,-.67f),new Vector3(0,0,-.4f),new Vector3(0,0,-1)}){
    if(!projection.SetEye(eye)){Fail(new Exception("projection-rejected-test-eye"));yield break;}
    foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){Vector3 p=displayCamera.WorldToViewportPoint(new Vector3(x*size.x,y*size.y,0));error=Mathf.Max(error,new Vector2((p.x-x-.5f)*displayCamera.pixelWidth,(p.y-y-.5f)*displayCamera.pixelHeight).magnitude);}
   }
   if((handle.Root.transform.localScale-Vector3.one*scale).sqrMagnitude>1e-10f){Fail(new Exception("non-uniform-content-scale"));yield break;}
  }
  report["corner_max_pixels"]=error;Check("five-aspects-two-framing-six-eyes",error<.01f);
  var displayEye=new[]{.1,.02,-.67};var local=viewport.ToView(displayEye);Check("actual-viewport-center-once",Math.Abs(local[0]+viewport.CenterX-displayEye[0])<1e-12&&Math.Abs(local[1]+viewport.CenterY-displayEye[1])<1e-12&&local[2]==displayEye[2]);
  report["viewport"]=new JObject{["width"]=viewport.Width,["height"]=viewport.Height,["center_x"]=viewport.CenterX,["center_y"]=viewport.CenterY};
  Measure();
  yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(Path.GetDirectoryName(reportPath),"player.png"));yield return null;
  var root=handle.Root;var owned=handle.Assets.ToArray();var previous=handle.PreviousLightmaps;Color ambient=handle.Ambient;bool fog=handle.Fog;
  var unload=loader.UnloadAsync(handle);while(!unload.IsCompleted)yield return null;
  if(unload.IsFaulted){Fail(unload.Exception);yield break;}yield return null;
  Check("unload-owned-resources",!root&&owned.All(x=>!x));
  Check("restore-environment",LightmapSettings.lightmaps.Length==previous.Length&&RenderSettings.ambientLight==ambient&&RenderSettings.fog==fog);
  Check("restore-lightmap-mode",LightmapSettings.lightmapsMode==handle.PreviousLightmapMode);
  Check("shader-bundles-released",!AssetBundle.GetAllLoadedAssetBundles().Any());
  string directionalPath=Argument("--content-directional-package");
  if(directionalPath!=null){
   ValidatedPackage directional=null;try{directional=PackageValidator.Validate(directionalPath);}catch(Exception e){Fail(e);yield break;}
   var directionalLoad=loader.LoadAsync(new SceneLoadRequest{Package=directional,Camera=displayCamera,ViewportWidth=viewport.Width,ViewportHeight=viewport.Height},CancellationToken.None);
   while(!directionalLoad.IsCompleted)yield return null;if(directionalLoad.IsFaulted||directionalLoad.IsCanceled){Fail(directionalLoad.Exception??new Exception("directional-load-cancelled"));yield break;}
   handle=directionalLoad.Result;
   Check("directional-lightmap-mode-and-textures-preserved",LightmapSettings.lightmapsMode==LightmapsMode.CombinedDirectional&&LightmapSettings.lightmaps.Length==1&&LightmapSettings.lightmaps[0].lightmapColor&&LightmapSettings.lightmaps[0].lightmapDir);
   var directionalUnload=loader.UnloadAsync(handle);while(!directionalUnload.IsCompleted)yield return null;if(directionalUnload.IsFaulted){Fail(directionalUnload.Exception);yield break;}
   yield return null;Check("directional-unload-restores-mode-and-resources",LightmapSettings.lightmapsMode==handle.PreviousLightmapMode&&LightmapSettings.lightmaps.Length==previous.Length&&handle.Assets.Count==0&&!AssetBundle.GetAllLoadedAssetBundles().Any());
  }
  report["errors"]=new JArray(errors);report["build_id"]=package.BuildId;report["passed"]=errors.Count==0&&((JArray)report["cases"]).All(c=>(bool)c["passed"]);Write();Application.Quit((bool)report["passed"]?0:1);
 }
 bool Measure(){
  if(!displayCamera||Time.realtimeSinceStartup<viewportReadyAfter)return false;
  if(!DisplayViewport.TryMeasure(displayCamera.pixelRect,.53,out var measured,out var display))return false;viewport=measured;
  if(!projection)return false;projection.displayWidth=(float)viewport.Width;projection.displayHeight=(float)viewport.Height;
  if(handle!=null&&!handle.Unloaded){float scale=(float)Framing.Scale(authorFrame.x,authorFrame.y,viewport.Width,viewport.Height,true);handle.Root.transform.localScale=Vector3.one*scale;projection.contentDepth=authorFrame.z*scale;}
  var eye=viewport.ToView(new[]{0.0,0,-.67});return projection.SetEye(new Vector3((float)eye[0],(float)eye[1],(float)eye[2]));
 }
 void LateUpdate(){if(handle!=null&&!handle.Unloaded)Measure();}
 void Check(string name,bool pass){((JArray)report["cases"]).Add(new JObject{["name"]=name,["passed"]=pass});}
 void Fail(Exception e){errors.Add(e.ToString());if(report==null)report=new JObject();report["passed"]=false;report["errors"]=new JArray(errors);Write();Debug.LogError(e);Application.Quit(1);}
 void Write(){if(reportPath!=null){Directory.CreateDirectory(Path.GetDirectoryName(reportPath));File.WriteAllText(reportPath,report.ToString(Formatting.Indented));}}
 void OnDestroy(){Application.logMessageReceived-=Log;}
}
