using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Editor;
using UnityEditor;
using UnityEngine;

public static class CleanSamplesChecks {
 static Type controller;
 static string output;
 static object Call(string name,params object[] args)=>controller.GetMethod(name,BindingFlags.Public|BindingFlags.Static).Invoke(null,args);
 static T Get<T>(string name)=>(T)controller.GetProperty(name,BindingFlags.Public|BindingFlags.Static).GetValue(null);
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static JObject AuthorProfile(string folder){
  var view=UnityEngine.Object.FindObjectsOfType<Camera>().Single(camera=>camera.GetComponent<WindowProjection>());Need(view.GetComponent<UnityEngine.Rendering.Universal.UniversalAdditionalCameraData>(),"sample-author-camera-profile-missing");
  var descriptor=new RenderTextureDescriptor(640,360,RenderTextureFormat.ARGB32,0){graphicsFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB,depthStencilFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.D24_UNorm_S8_UInt,msaaSamples=1};var target=new RenderTexture(descriptor);var previous=view.targetTexture;
  try{Need(target.Create(),"sample-author-profile-target");view.targetTexture=target;var observed=SpatialWallpaper.Sdk.Hosting.UnityRenderObservation.Read(view,target,TextureFormat.RGBA32,out var binding);File.WriteAllText(Path.Combine(folder,"author-profile.json"),observed.ToString());SpatialWallpaper.Sdk.Security.RenderProfiles.RequireObservation(SpatialWallpaper.Sdk.Security.RenderProfiles.DefaultIdentity,observed);return observed;}
  finally{view.targetTexture=previous;target.Release();UnityEngine.Object.DestroyImmediate(target);}
 }
 static string Digest(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
 static bool ReplyStored(string folder){foreach(string path in Directory.GetFiles(folder,"state.bin",SearchOption.AllDirectories))try{string data=Encoding.UTF8.GetString(File.ReadAllBytes(path));if(data.Contains("starter-llm-reply")&&data.Contains("reply-as-data"))return true;}catch(IOException){}return false;}
 static bool AudioStored(string folder,string key){foreach(string path in Directory.GetFiles(folder,"state.bin",SearchOption.AllDirectories))try{if(Encoding.UTF8.GetString(File.ReadAllBytes(path)).Contains(key))return true;}catch(IOException){}return false;}
 static JObject ShaderBoundaries(SpatialWallpaper.Sdk.SceneDefinition definition,string folder){
  var script=definition.GetComponent<ShaderPrefabSample>();var foreign=new GameObject("foreign reference negative");bool foreignDenied=false,keywordDenied=false;
  try{script.clone=foreign;try{ScriptSceneExporter.Export(definition.contentRoot.gameObject,definition.authorFrame,new JArray());}catch(InvalidDataException error){foreignDenied=error.Message=="export:foreign-node-reference";}}finally{script.clone=null;UnityEngine.Object.DestroyImmediate(foreign);}
  Need(foreignDenied,"foreign-nonnull-node-reference-accepted");
  try{script.surface.EnableKeyword("SDK_UNDECLARED_VARIANT");try{ScriptSceneExporter.Export(definition.contentRoot.gameObject,definition.authorFrame,new JArray());}catch(InvalidDataException error){keywordDenied=error.Message=="export:material-keywords-not-supported";}}finally{script.surface.DisableKeyword("SDK_UNDECLARED_VARIANT");}
  Need(keywordDenied,"undeclared-shader-keyword-accepted");
  var original=definition.contentRoot;try{definition.contentRoot=original.Find("Content");Need(definition.contentRoot,"legacy-sample-content-root-missing");ScenePackageExporter.Export(definition,Path.Combine(folder,"legacy.spatial"),ExportMode.LocalValidation);}finally{definition.contentRoot=original;}
  return new JObject{{"foreign_nonnull_node_refused",true},{"undeclared_shader_keyword_refused",true},{"legacy_shader_export",true}};
 }
 public static async void Run(){var samples=new JArray();try{
  output=Environment.GetEnvironmentVariable("SDK_G_OUTPUT");controller=typeof(ScriptSceneExporter).Assembly.GetType("SpatialWallpaper.Sdk.Editor.SandboxPreviewController");Need(controller!=null,"installed-sdk-preview-controller-missing");
  PlayerSettings.colorSpace=ColorSpace.Linear;
  string selected=Environment.GetEnvironmentVariable("SDK_G_SAMPLE");
  foreach(string name in new[]{"InputStorage","Audio","DynamicResources","ShaderPrefab","LlmReply"}.Where(name=>string.IsNullOrEmpty(selected)||name==selected)){
   string folder=Path.Combine(output,name);Directory.CreateDirectory(folder);var definition=SdkSamples.Create(name);Need(definition!=null,"sample-scene-not-created:"+name);
   var authorProfile=AuthorProfile(folder);var boundaries=name=="ShaderPrefab"?ShaderBoundaries(definition,folder):null;
   string cache=Path.Combine(Environment.GetEnvironmentVariable("SDK_G_CACHE"),name);Call("Prepare",definition,cache);bool beforeDenied=false;try{Call("Export",Path.Combine(folder,"before.spatial"));}catch(TargetInvocationException error){beforeDenied=error.InnerException is InvalidOperationException;}Need(beforeDenied,"sample-export-before-preview:"+name);
   await (Task)Call("StartAsync",Environment.GetEnvironmentVariable("SDK_G_RUNTIME"),Environment.GetEnvironmentVariable("SDK_G_ATTEMPTS"),640,360,.53f,.298125f);
   double deadline=EditorApplication.timeSinceStartup+45;while(Get<string>("State")!="Running"){Need(Get<string>("Error")==null,"sample-preview-error:"+name+":"+Get<string>("Error"));Need(EditorApplication.timeSinceStartup<deadline,"sample-preview-deadline:"+name);await Task.Delay(25);}
   var receipt=Get<JObject>("LastReceipt");Need(receipt!=null,"sample-preview-no-frame:"+name);
   if(name=="LlmReply"){deadline=EditorApplication.timeSinceStartup+15;while(!ReplyStored(cache)){Need(EditorApplication.timeSinceStartup<deadline,"clean-sample-llm-reply-not-stored");await Task.Delay(25);}}
   if(name=="Audio"&&Environment.GetEnvironmentVariable("SDK_G_AUDIO_FIXTURE")=="1"){
    deadline=EditorApplication.timeSinceStartup+20;while(!AudioStored(cache,"audio-selected-heard")){Need(EditorApplication.timeSinceStartup<deadline,"clean-sample-selected-audio-not-heard");await Task.Delay(25);}File.WriteAllText(Path.Combine(output,"audio-heard"),"current author stored actual nonzero selected band");
    deadline=EditorApplication.timeSinceStartup+15;while(!AudioStored(cache,"audio-selected-quiet")){Need(EditorApplication.timeSinceStartup<deadline,"clean-sample-ended-audio-not-quiet");await Task.Delay(25);}
   }
   string first=Path.Combine(folder,"sample.spatial"),same=Path.Combine(folder,"same.spatial");Call("Export",first);definition.displayName+=" changed after snapshot";Call("Export",same);Need(Digest(first)==Digest(same),"sample-export-did-not-retain-previewed-bytes:"+name);
   await (Task)Call("StopAsync");Need(Get<bool>("ResourcesReleased")&&Get<string>("Error")==null,"sample-preview-cleanup-unproven:"+name);
   samples.Add(new JObject{{"name",name},{"cache",cache},{"receipt",receipt},{"package_sha256",Digest(first)},{"author_profile",authorProfile},{"boundaries",boundaries},{"export_before_preview_refused",true},{"same_snapshot_exported",true},{"resources_released",true},{"deterministic_reply_stored",name=="LlmReply"}});
   File.WriteAllText(Path.Combine(output,"partial-results.json"),samples.ToString());
  }
  File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"samples",samples},{"scope","copied UPM SDK, imported Starter Kit, generated selected procedural scenes, real immutable snapshot and native sandbox preview/export; audio fixture is a wiring check, not continuous-silence or audio long-run acceptance; not interactive Editor UI or all sample behavior acceptance"}}.ToString());EditorApplication.Exit(0);
 }catch(Exception error){if(output!=null)File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());Debug.LogException(error);try{if(controller!=null)await (Task)Call("StopAsync");}catch{}EditorApplication.Exit(1);}}
}
