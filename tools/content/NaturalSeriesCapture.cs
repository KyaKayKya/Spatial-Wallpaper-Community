using System;
using System.IO;
using System.Linq;
using System.Collections;
using System.Threading;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using SpatialWallpaper.Sdk;
// Validation-only observer. It reads the naturally running scene and writes
// untouched frames; it never sets clocks, random seeds, materials or poses.
public sealed class NaturalSeriesCapture:MonoBehaviour {
 public Camera displayCamera;
 int errors;
 void OnEnable(){Application.logMessageReceivedThreaded+=Log;}
 void OnDisable(){Application.logMessageReceivedThreaded-=Log;}
 void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Assert||type==LogType.Exception||message.StartsWith("Shader ",StringComparison.Ordinal)&&message.Contains(": variant ")&&message.TrimEnd().EndsWith(" not found.",StringComparison.Ordinal))Interlocked.Increment(ref errors);}
 public static string Argument(string key){var args=Environment.GetCommandLineArgs();int i=Array.IndexOf(args,key);return i>=0&&i+1<args.Length?args[i+1]:null;}
 public static void Launch(Camera camera){var observer=new GameObject("Natural series observer").AddComponent<NaturalSeriesCapture>();observer.displayCamera=camera;}
 IEnumerator Start(){
  string output=Argument("--series-output");Directory.CreateDirectory(output);float warmup=3;string requested=Argument("--series-warmup");if(requested!=null&&(!float.TryParse(requested,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out warmup)||warmup<3||warmup>40))throw new ArgumentException("series-warmup-range");yield return new WaitForSecondsRealtime(warmup);var frames=new JArray();float start=Time.realtimeSinceStartup;
  for(int i=0;i<32;i++){
   yield return new WaitForEndOfFrame();string file=i.ToString("D3")+".png";ScreenCapture.CaptureScreenshot(Path.Combine(output,file));
   var viewport=displayCamera.pixelRect;var projection=displayCamera.GetComponent<WindowProjection>();var data=displayCamera.GetUniversalAdditionalCameraData();var ring=FindObjectsOfType<MeshRenderer>().FirstOrDefault(r=>r.name.StartsWith("Floating ring"));JToken ringInput=JValue.CreateNull();if(ring){LightProbes.GetInterpolatedProbe(ring.bounds.center,ring,out var input);ringInput=Coefficients(input);}
   var renderers=new JArray(FindObjectsOfType<MeshRenderer>().Select(r=>(JToken)new JObject{["name"]=r.name,["position"]=V(r.transform.position),["bounds_center"]=V(r.bounds.center),["bounds_size"]=V(r.bounds.size),["materials"]=new JArray(r.sharedMaterials.Select(m=>(JToken)new JObject{["shader"]=m.shader.name,["reflection"]=m.HasProperty("_ReflectionTex")&&m.GetTexture("_ReflectionTex")?new JArray(m.GetTexture("_ReflectionTex").width,m.GetTexture("_ReflectionTex").height):null}))}));
   frames.Add(new JObject{["file"]=file,["elapsed"]=Time.realtimeSinceStartup-start,["scene_time"]=Time.timeSinceLevelLoad,["pixels"]=new JArray(Screen.width,Screen.height),["pixel_rect"]=new JArray(viewport.x,viewport.y,viewport.width,viewport.height),["content_depth"]=projection.contentDepth,["eye"]=V(displayCamera.transform.position),["camera_settings"]=new JObject{["hdr"]=displayCamera.allowHDR,["msaa"]=displayCamera.allowMSAA?((UniversalRenderPipelineAsset)GraphicsSettings.currentRenderPipeline).msaaSampleCount:1,["opaque"]=data.requiresColorTexture,["depth"]=data.requiresDepthTexture,["post"]=data.renderPostProcessing},["invert_culling"]=GL.invertCulling,["reflection_cameras"]=Resources.FindObjectsOfTypeAll<Camera>().Count(c=>c.gameObject.scene.IsValid()&&c.cameraType==CameraType.Reflection),["probe_count"]=FindObjectsOfType<ReflectionProbe>().Length,["renderers"]=renderers});
   var captured=(JObject)frames[frames.Count-1];captured["ambient_mode"]=(int)RenderSettings.ambientMode;captured["ambient_colors"]=new JArray(C(RenderSettings.ambientSkyColor),C(RenderSettings.ambientEquatorColor),C(RenderSettings.ambientGroundColor));captured["ambient_probe"]=Coefficients(RenderSettings.ambientProbe);captured["ring_probe"]=ringInput;captured["lightprobe_count"]=LightmapSettings.lightProbes?LightmapSettings.lightProbes.count:0;
   yield return new WaitForSecondsRealtime(.25f);
  }
  yield return new WaitForEndOfFrame();File.WriteAllText(Path.Combine(output,"series.json"),new JObject{["scope"]="Natural playback observed without clock/pose/material overrides",["render_errors"]=errors,["frames"]=frames}.ToString());Application.Quit(errors==0?0:1);
 }
 static JArray V(Vector3 value)=>new JArray(value.x,value.y,value.z);
 static JArray C(Color value)=>new JArray(value.r,value.g,value.b,value.a);
 static JArray Coefficients(SphericalHarmonicsL2 probe){var values=new JArray();for(int channel=0;channel<3;channel++)for(int coefficient=0;coefficient<9;coefficient++)values.Add(probe[channel,coefficient]);return values;}
}
