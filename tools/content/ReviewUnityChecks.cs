using System;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Content;
using SpatialWallpaper.Sdk;
using TMPro;
using UnityEngine;
using UnityEditor;
using SpatialWallpaper.Sdk.Editor;
public static class ReviewUnityChecks {
 public static void Run(){
  var cases=new JArray();void Check(string name,Action test){try{test();cases.Add(new JObject{["name"]=name,["passed"]=true});}catch(Exception e){cases.Add(new JObject{["name"]=name,["passed"]=false,["error"]=e.ToString()});}}
  Check("signal-directional-independent-of-window-scale",()=>Signal(LightType.Directional,1));Check("signal-point-inverse-square-window-scale",()=>Signal(LightType.Point,.25f));
  Check("author-fully-baked-light-not-recreated-as-realtime",()=>AuthorLight(false));Check("author-rejects-signal-on-fully-baked-light",()=>AuthorLight(true));
  Check("manager-static-recovery-does-not-claim-playing",()=>{
   var owner=new GameObject("Review Manager");var view=owner.AddComponent<ContentLibraryView>();typeof(ContentLibraryView).GetField("ui",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(view,new CalibrationWidgets(null));
   try{typeof(ContentLibraryView).GetMethod("Build",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(view,null);string work=Guid.NewGuid().ToString(),revision=Guid.NewGuid().ToString();var state=new JObject{["works"]=new JObject{[work]=new JObject{["name"]="Review work",["default_build"]=new string('a',64),["latest_revision"]=revision}},["applied"]=new JObject{["primary"]=new JObject{["work_id"]=work,["revision_id"]=revision}}};var reply=new JObject{["state"]=state,["presentation_ready"]=false,["static_held"]=true,["startup_error"]=null};var render=typeof(ContentLibraryView).GetMethod("Render",BindingFlags.NonPublic|BindingFlags.Instance);render.Invoke(view,new object[]{render.GetParameters()[0].Name=="state"?state:reply});var canvas=(RectTransform)typeof(ContentLibraryView).GetField("canvas",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(view);var texts=canvas.GetComponentsInChildren<TMP_Text>();if(texts.Any(t=>t.text=="正在播放")||!texts.Any(t=>t.text.Contains("静态画面")))throw new Exception("Persisted applied record was presented as live playback");UnityEngine.Object.DestroyImmediate(canvas.gameObject);}
   finally{UnityEngine.Object.DestroyImmediate(owner);}
  });
  string path=Environment.GetEnvironmentVariable("CONTENT_REVIEW_REPORT");bool passed=cases.All(c=>(bool)c["passed"]);File.WriteAllText(path,new JObject{["passed"]=passed,["scope"]="Actual Unity SDK signal components and Manager Render method; no native drag/button input",["cases"]=cases}.ToString());if(!passed)throw new Exception("review-unity-checks-failed");
 }
 static void Signal(LightType type,float ratio){var root=new GameObject("Review scale");root.SetActive(false);var lamp=new GameObject("Review light").AddComponent<Light>();lamp.type=type;var signal=root.AddComponent<SignalBindings>();signal.scaleRoot=root.transform;signal.lights=new[]{new LightSignalBinding{light=lamp,intensity=4}};try{signal.Apply(2.5f);float full=lamp.intensity;root.transform.localScale=Vector3.one*.5f;signal.Apply(2.5f);if(Mathf.Abs(lamp.intensity-full*ratio)>.00001f)throw new Exception("Wrong same-time intensity ratio: "+lamp.intensity/full);}finally{UnityEngine.Object.DestroyImmediate(lamp.gameObject);UnityEngine.Object.DestroyImmediate(root);}}
 static void AuthorLight(bool signal){
  var root=new GameObject("Baked source light");var light=root.AddComponent<Light>();light.bakingOutput=new LightBakingOutput{isBaked=true,lightmapBakeType=LightmapBakeType.Baked};var definition=new GameObject("Author definition").AddComponent<SceneDefinition>();definition.contentRoot=root.transform;var maps=LightmapSettings.lightmaps;LightmapSettings.lightmaps=Array.Empty<LightmapData>();
  if(signal)root.AddComponent<SignalBindings>().lights=new[]{new LightSignalBinding{light=light,intensity=1}};
  try{string path=Path.Combine(Path.GetDirectoryName(Environment.GetEnvironmentVariable("CONTENT_REVIEW_REPORT")),"../baked-author-check.spatial");try{ScenePackageExporter.Export(definition,path,ExportMode.LocalValidation);if(signal)throw new Exception("Baked signal was accepted");var package=PackageValidator.Validate(path);if(package.Scene.ContainsKey("effects"))throw new Exception("Fully baked source was recreated as runtime light");}catch(InvalidDataException e){if(!signal||e.Message!="baked-light-signal-unsupported")throw;}}
  finally{LightmapSettings.lightmaps=maps;UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(definition.gameObject);}
 }
}
