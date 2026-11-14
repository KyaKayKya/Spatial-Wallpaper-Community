using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

// Explicit developer-only Player evidence. The library removes this component.
public sealed class BlueprintCapture:MonoBehaviour {
 string output;bool failed;
 static string Arg(string key){var a=Environment.GetCommandLineArgs();int i=Array.IndexOf(a,key);return i>=0&&i+1<a.Length?a[i+1]:null;}
 void Log(string message,string trace,LogType type){if(output!=null&&(type==LogType.Error||type==LogType.Exception)){failed=true;File.AppendAllText(Path.Combine(output,"errors.txt"),message+"\n"+trace+"\n");}}
 IEnumerator Shot(string name){
  yield return new WaitForEndOfFrame();var tex=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);tex.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);tex.Apply();File.WriteAllBytes(Path.Combine(output,name+".png"),tex.EncodeToPNG());Destroy(tex);
 }
 IEnumerator Start(){
  output=Arg("--blueprint-shot")??Arg("--blueprint-capture")??Arg("--blueprint-benchmark")??Arg("--blueprint-video")??Arg("--blueprint-lighting-check");if(output==null)yield break;
  Directory.CreateDirectory(output);Application.logMessageReceived+=Log;
  var projection=GetComponent<WindowProjection>();var cam=GetComponent<Camera>();
  GetComponent<PreviewEyeDriver>().enabled=false;GetComponent<AdaptiveRenderCadence>().enabled=false;OnDemandRendering.renderFrameInterval=1;QualitySettings.vSyncCount=0;Application.targetFrameRate=60;
  projection.viewportManagedExternally=true;cam.rect=new Rect(0,0,1,1);
  var scene=FindObjectOfType<SpatialWallpaper.Blueprint.BlueprintScene>();float deadline=Time.realtimeSinceStartup+60;
  while(scene&&!scene.Ready&&scene.Error==null&&Time.realtimeSinceStartup<deadline)yield return null;
  if(!scene||!scene.Ready){File.WriteAllText(Path.Combine(output,"failure.txt"),scene?scene.Error??"Import timeout":"Template scene missing");Application.Quit(2);yield break;}
  yield return new WaitForSeconds(2);
  File.WriteAllText(Path.Combine(output,"lighting.json"),new Newtonsoft.Json.Linq.JObject{["additional_keyword"]=Shader.IsKeywordEnabled("_ADDITIONAL_LIGHTS"),["pipeline_mode"]=scene.Lighting.Pipelines[scene.Settings.Quality].additionalLightsRenderingMode.ToString(),["building_scale"]=scene.Building.lossyScale.x,["lights"]=new Newtonsoft.Json.Linq.JArray(FindObjectsOfType<Light>().Select(l=>new Newtonsoft.Json.Linq.JObject{["name"]=l.name,["intensity"]=l.intensity,["range"]=l.range,["position"]=l.transform.position.ToString("F5"),["enabled"]=l.enabled}))}.ToString());
  if(Arg("--blueprint-lighting-check")!=null){
   Time.timeScale=0;var points=FindObjectsOfType<Light>().Where(l=>l.type==LightType.Point&&l.enabled).ToArray();yield return Shot("lamps-on");
   var onTexture=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);onTexture.LoadImage(File.ReadAllBytes(Path.Combine(output,"lamps-on.png")));var on=onTexture.GetPixels32();Destroy(onTexture);
   foreach(var light in points)light.enabled=false;yield return null;yield return Shot("lamps-off");
   var offTexture=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);offTexture.LoadImage(File.ReadAllBytes(Path.Combine(output,"lamps-off.png")));var off=offTexture.GetPixels32();Destroy(offTexture);int brighter=0;long increase=0;for(int i=0;i<on.Length;i++){int d=(on[i].r+on[i].g+on[i].b)-(off[i].r+off[i].g+off[i].b);if(d>12){brighter++;increase+=d;}}foreach(var light in points)light.enabled=true;
   bool lit=points.Length>0&&brighter>Screen.width*Screen.height*.003f;File.WriteAllText(Path.Combine(output,"lighting-checks.json"),new Newtonsoft.Json.Linq.JObject{["pass"]=lit,["standalone"]=!Application.isEditor,["quality"]=scene.Settings.Quality,["visible_point_lights"]=points.Length,["pixels_brightened_by_lamps"]=brighter,["rgb_increase_sum"]=increase,["resolution"]=Screen.width+"x"+Screen.height}.ToString());Application.Quit(lit?0:2);yield break;
  }
  if(Arg("--blueprint-shot")!=null){yield return Shot("front");File.WriteAllText(Path.Combine(output,"scene.json"),new Newtonsoft.Json.Linq.JObject{["standalone"]=!Application.isEditor,["simulated_eye_only"]=true,["triangles"]=scene.Triangles,["mesh_objects"]=scene.MeshObjects,["cache_bytes"]=scene.CacheBytes,["cache_hit"]=scene.CacheHit,["import_ms"]=scene.ImportMilliseconds}.ToString());Application.Quit(failed?2:0);yield break;}

  if(Arg("--blueprint-benchmark")!=null){
   OnDemandRendering.renderFrameInterval=scene.Settings.FrameRate==60?1:scene.Settings.FrameRate==30?2:4;
   File.WriteAllText(Path.Combine(output,"import.json"),new Newtonsoft.Json.Linq.JObject{["blocks"]=scene.Document.BlockCount,["volume"]=scene.Document.Indices.Length,["cache_hit"]=scene.CacheHit,["cache_bytes"]=scene.CacheBytes,["import_ms"]=scene.ImportMilliseconds,["building_triangles"]=scene.Triangles,["building_batches"]=scene.MeshObjects}.ToString());
   var times=new List<float>();var gpu=new List<double>();var timing=new FrameTiming[1];double last=0;Action<ScriptableRenderContext,Camera> rendered=(context,renderCamera)=>{if(renderCamera!=cam)return;double now=Time.realtimeSinceStartupAsDouble;if(last>0)times.Add((float)((now-last)*1000));last=now;};RenderPipelineManager.endCameraRendering+=rendered;NativeProcessCounters.Sample();double cpu=NativeProcessCounters.CpuSeconds;float start=Time.realtimeSinceStartup;
   while(Time.realtimeSinceStartup-start<12){FrameTimingManager.CaptureFrameTimings();if(FrameTimingManager.GetLatestTimings(1,timing)>0&&timing[0].gpuFrameTime>0)gpu.Add(timing[0].gpuFrameTime);yield return null;}RenderPipelineManager.endCameraRendering-=rendered;
   float elapsed=Time.realtimeSinceStartup-start;times.Sort();gpu.Sort();NativeProcessCounters.Sample();
   var report=new Newtonsoft.Json.Linq.JObject{["simulated_eye"]=true,["standalone"]=!Application.isEditor,["resolution"]=Screen.width+"x"+Screen.height,["gpu"]=SystemInfo.graphicsDeviceName,["cpu"]=SystemInfo.processorType,["samples"]=times.Count,["elapsed_s"]=elapsed,["fps"]=times.Count/elapsed,["frame_p50_ms"]=times[times.Count/2],["frame_p95_ms"]=times[(int)(times.Count*.95f)],["process_cpu_core_equivalents"]=(NativeProcessCounters.CpuSeconds-cpu)/elapsed,["working_set_mib"]=NativeProcessCounters.WorkingSet/1048576.0,["private_mib"]=NativeProcessCounters.PrivateBytes/1048576.0,["gpu_frame_p50_ms"]=gpu.Count>0?new Newtonsoft.Json.Linq.JValue(gpu[gpu.Count/2]):Newtonsoft.Json.Linq.JValue.CreateNull(),["triangles"]=FindObjectsOfType<MeshFilter>().Sum(m=>Enumerable.Range(0,m.sharedMesh.subMeshCount).Sum(i=>(long)m.sharedMesh.GetIndexCount(i)/3)),["renderers"]=FindObjectsOfType<MeshRenderer>().Length,["reflection_scale"]=(FindObjectOfType<PoolPlanarReflection>()?FindObjectOfType<PoolPlanarReflection>().resolutionScale:0)};
   report["reflection_enabled"]=FindObjectOfType<PoolPlanarReflection>()&&FindObjectOfType<PoolPlanarReflection>().enabled;report["eye"]=projection.EyeScreen.ToString("F5");report["display_width"]=projection.displayWidth;report["display_height"]=projection.displayHeight;report["content_scale"]=GameObject.Find("SceneModel").transform.localScale.ToString("F5");
   File.WriteAllText(Path.Combine(output,"performance.json"),report.ToString());yield return Shot("front");Application.Quit(failed?2:0);yield break;
  }
  if(Arg("--blueprint-video")!=null){
   Time.captureFramerate=30;for(int i=0;i<180;i++){projection.SetEye(i<90?projection.referenceEye:new Vector3(.12f*Mathf.Sin((i-90)/90f*Mathf.PI*2),.03f*Mathf.Sin((i-90)/90f*Mathf.PI*2),-.6f));yield return Shot(i.ToString("D4"));}Application.Quit(failed?2:0);yield break;
  }
  var sizes=new[]{new Vector2Int(1280,720),new Vector2Int(1280,800),new Vector2Int(1000,750),new Vector2Int(1680,720),new Vector2Int(540,960)};
  var eyes=new[]{new Vector3(0,0,-.6f),new Vector3(-.16f,0,-.6f),new Vector3(.16f,0,-.6f),new Vector3(0,.10f,-.6f),new Vector3(0,-.06f,-.6f),new Vector3(0,0,-.4f),new Vector3(0,0,-.95f)};
  var names=new[]{"front","left","right","up","down","near","far"};var content=GameObject.Find("SceneModel").transform;var ground=GameObject.Find("ActualGround")?.GetComponent<Collider>();
  var csv=new StringBuilder("width,height,keep_height,eye,corner_error_px,floor_plane_error_m,floor_ray_hits,content_changes\n");float worst=0,planeWorst=0;int missing=0,moved=0;var initialPosition=content.position;var initialRotation=content.rotation;
  foreach(var size in sizes){
   Screen.SetResolution(size.x,size.y,false);yield return new WaitForSeconds(.5f);cam.rect=new Rect(0,0,1,1);
   foreach(bool height in new[]{true,false}){
    projection.displayWidth=.53f;projection.displayHeight=.53f*Screen.height/Screen.width;float scale=height?projection.displayHeight/.298125f:1;content.localScale=Vector3.one*scale;projection.contentDepth=10*scale;Physics.SyncTransforms();
    var expected=FindObjectsOfType<MeshFilter>().ToDictionary(m=>m,m=>m.transform.localToWorldMatrix);
    for(int i=0;i<eyes.Length;i++){
     projection.SetEye(eyes[i]);yield return new WaitForEndOfFrame();float error=0;
     for(int corner=0;corner<4;corner++){
      var world=projection.screenFrame.TransformPoint(new Vector3((corner%2==0?-1:1)*projection.displayWidth/2,(corner/2==0?-1:1)*projection.displayHeight/2,0));var p=cam.WorldToViewportPoint(world);
      error=Mathf.Max(error,new Vector2((p.x-corner%2)*cam.pixelWidth,(p.y-corner/2)*cam.pixelHeight).magnitude);
     }
     // Read the real floor collider, not a synthetic projection-plane quad.
     float planeError=ground?Mathf.Abs(ground.bounds.max.y+.298125f*.5f*scale)+Mathf.Abs(ground.bounds.min.z):0;int hits=0;
     if(height&&ground){foreach(float x in new[]{.001f,.999f}){var ray=cam.ViewportPointToRay(new Vector3(x,.002f,0));if(ground.Raycast(ray,out var hit,cam.farClipPlane))hits++;}if(hits!=2)missing++;}
     int changed=expected.Count(p=>p.Key.transform.localToWorldMatrix!=p.Value);if(content.position!=initialPosition||content.rotation!=initialRotation)changed++;moved+=changed;
     worst=Mathf.Max(worst,error);planeWorst=Mathf.Max(planeWorst,planeError);
     csv.AppendLine(FormattableString.Invariant($"{Screen.width},{Screen.height},{height},{names[i]},{error},{planeError},{hits},{changed}"));
     yield return Shot(Screen.width+"x"+Screen.height+(height?"-height-":"-width-")+names[i]);
    }
   }
  }
  bool floorApplicable=scene.Settings.Terrain!="none";bool pass=!failed&&(!floorApplicable||ground)&&worst<.01f&&planeWorst<.00001f&&missing==0&&moved==0;
  File.WriteAllText(Path.Combine(output,"geometry.csv"),csv.ToString());File.WriteAllText(Path.Combine(output,"checks.json"),new Newtonsoft.Json.Linq.JObject{["pass"]=pass,["corner_error_px"]=worst,["template_floor_applicable"]=floorApplicable,["terrain_mode"]=scene.Settings.Terrain,["actual_floor_plane_error_m"]=ground?new Newtonsoft.Json.Linq.JValue(planeWorst):Newtonsoft.Json.Linq.JValue.CreateNull(),["missing_floor_coverage_cases"]=missing,["content_changes_from_eye"]=moved,["cases"]=sizes.Length*2*eyes.Length,["standalone"]=!Application.isEditor,["simulated_eye_only"]=true}.ToString());
  Application.Quit(pass?0:2);
 }
 void OnDestroy(){Application.logMessageReceived-=Log;}
}
