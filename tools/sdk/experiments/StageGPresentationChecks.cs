using System;using System.IO;using Newtonsoft.Json.Linq;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;using UnityEditor;using UnityEditor.SceneManagement;using SpatialWallpaper.Sdk;using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Security;using SpatialWallpaper.Sdk.Execution;
public static class StageGPresentationChecks {
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static JObject Reject(string name,Action<ScenePresentationSettings> edit,string reason){
  var root=new GameObject("Unsupported presentation fixture");var source=root.AddComponent<ScenePresentationSettings>();
  try{
   if(name=="child"){UnityEngine.Object.DestroyImmediate(source);var child=new GameObject("Child settings");child.transform.SetParent(root.transform,false);source=child.AddComponent<ScenePresentationSettings>();}
   edit(source);try{ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),new JArray(new JObject{{"name","Original rejection fixture"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}}));}
   catch(Exception error){Need(error.ToString().Contains(reason),"wrong-presentation-rejection:"+name+":"+error);return new JObject{{"case",name},{"reason",reason}};}
   throw new Exception("unsupported-presentation-exported:"+name);
  }finally{UnityEngine.Object.DestroyImmediate(root);}
 }
 static long ProfileLifetime(byte[] bytes){
  var build=Admission.Admit(bytes,"windows-x86_64");using(var domain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new UnityOwnedHost(build,domain)){
   var runtime=new VmRuntime(build,domain,host,new ExecutionQuota(100000,64,100));host.Bind(runtime);var profile=host.Root.GetComponentInChildren<Volume>().sharedProfile;
   host.Call("SpatialWallpaper.Sdk::Objects::Destroy(Node)->System.Void",null,new object[]{host.Resolve("node",new JValue(0),0)});host.CompletedRender(2);
   Need(profile,"post-profile-lifetime-fixture-not-alive");long retained=domain.Ledger.Used.NativeBytes;Need(retained>=65536,"live-post-profile-budget-refunded-after-node-destroy:"+retained);runtime.Stop();return retained;
  }
 }
 static void GradientState(ScenePresentationSettings source,JArray license,Camera view){
  bool ambient=source.customAmbient,gradient=source.customAmbientGradient;Color sky=source.ambientSky,equator=source.ambientEquator,ground=source.ambientGround;
  try{source.customAmbient=false;source.customAmbientGradient=true;source.ambientSky=new Color(.3f,.4f,.5f,1);source.ambientEquator=new Color(.1f,.2f,.3f,1);source.ambientGround=new Color(.02f,.03f,.04f,1);
   byte[] bytes=ScriptSceneExporter.Export(source.gameObject,new Vector3(.6f,.3375f,.5f),license);using(var domain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new UnityOwnedHost(Admission.Admit(bytes,"windows-x86_64"),domain)){host.ConfigurePresentation(view);Need(RenderSettings.ambientMode==AmbientMode.Trilight&&RenderSettings.ambientSkyColor==source.ambientSky&&RenderSettings.ambientEquatorColor==source.ambientEquator&&RenderSettings.ambientGroundColor==source.ambientGround,"modern-gradient-state");}
  }finally{source.customAmbient=ambient;source.customAmbientGradient=gradient;source.ambientSky=sky;source.ambientEquator=equator;source.ambientGround=ground;}
 }
 public static void Run(){string output=Environment.GetEnvironmentVariable("SDK_G_PRESENTATION_OUTPUT");try{
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=new GameObject("Original bounded presentation fixture");
  var source=root.AddComponent<ScenePresentationSettings>();source.customBackground=true;source.backgroundColor=new Color(.04f,.08f,.12f,1);source.customAmbient=true;source.ambientIrradiance=new Color(.1f,.15f,.2f,1);source.postProcessing=true;source.bloom=new Vector3(.75f,.3f,.6f);source.aces=true;source.exposure=.4f;source.saturation=-12;source.vignette=new Vector2(.25f,.7f);
  var license=new JArray(new JObject{{"name","Original presentation validation"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}});byte[] bytes=ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),license);File.WriteAllBytes(Path.Combine(output,"presentation.spatial"),bytes);
  RenderSettings.reflectionIntensity=.37f;var view=new GameObject("Trusted test camera").AddComponent<Camera>();view.gameObject.AddComponent<UniversalAdditionalCameraData>();
  using(var domain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new UnityOwnedHost(Admission.Admit(bytes,"windows-x86_64"),domain)){
   typeof(UnityOwnedHost).GetMethod("ConfigurePresentation").Invoke(host,new object[]{view});
   Need(RenderSettings.reflectionIntensity==0,"modern-inherited-reflection-not-disabled");Need(view.backgroundColor==source.backgroundColor,"modern-background-state");Need(RenderSettings.ambientMode==AmbientMode.Flat&&RenderSettings.ambientLight==source.ambientIrradiance,"modern-ambient-state");
   var volumes=host.Root.GetComponentsInChildren<Volume>(true);Need(volumes.Length==1&&volumes[0].isGlobal,"modern-post-volume");var profile=volumes[0].sharedProfile;
   Need(profile.TryGet<Bloom>(out var bloom)&&bloom.threshold.value==source.bloom.x&&bloom.intensity.value==source.bloom.y&&bloom.scatter.value==source.bloom.z,"modern-bloom-state");Need(profile.TryGet<Tonemapping>(out var tone)&&tone.mode.value==TonemappingMode.ACES,"modern-tone-state");Need(profile.TryGet<ColorAdjustments>(out var color)&&color.postExposure.value==source.exposure&&color.saturation.value==source.saturation,"modern-color-state");Need(profile.TryGet<Vignette>(out var vignette)&&vignette.intensity.value==source.vignette.x&&vignette.smoothness.value==source.vignette.y,"modern-vignette-state");
  }
  Need(RenderSettings.reflectionIntensity==.37f,"modern-reflection-not-restored");
  var rejected=new JArray(Reject("camera",x=>x.customCamera=true,"presentation-camera-or-clip-override-not-supported"),Reject("clip",x=>x.customClipDepth=true,"presentation-camera-or-clip-override-not-supported"),Reject("disabled",x=>x.enabled=false,"presentation-root-and-enabled-required"),Reject("child",x=>{},"presentation-root-and-enabled-required"),Reject("ambient-conflict",x=>{x.customAmbient=true;x.customAmbientGradient=true;},"duplicate-author-ambient"),Reject("bloom-range",x=>x.bloom=new Vector3(1,11,.5f),"number-range"),Reject("arbitrary-volume",x=>x.gameObject.AddComponent<Volume>(),"component-not-yet-exportable:UnityEngine.Rendering.Volume"));
  GradientState(source,license,view);long retained=ProfileLifetime(bytes);var rendered=StageGPresentationVisuals.Run(source,output);
  UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(view.gameObject);File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_G_PRESENTATION_RUN")},{"state_preserved",true},{"live_profile_native_bytes_after_node_destroy",retained},{"rejected",rejected},{"rendered",rendered},{"scope","bounded export/admission/native presentation state, profile lifetime and fixed-view Editor render/control observations; not LPAC or RC"}}.ToString());
 }catch(Exception error){File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());Debug.LogException(error);EditorApplication.Exit(1);}}
}
