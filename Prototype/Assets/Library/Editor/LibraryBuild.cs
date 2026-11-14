using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpatialWallpaper.Library;
public static class LibraryBuild {
 public static void Build(){
  string targetName=Environment.GetEnvironmentVariable("SPATIAL_BUILD_TARGET")??"windows-x86_64";
  BuildTarget target;
  if(targetName=="windows-x86_64")target=BuildTarget.StandaloneWindows64;
  else if(targetName=="macos-universal")target=BuildTarget.StandaloneOSX;
  else throw new ArgumentException("Unsupported SPATIAL_BUILD_TARGET: "+targetName);
  PlayerSettings.SetUseDefaultGraphicsAPIs(target,false);
  PlayerSettings.SetGraphicsAPIs(target,new[]{target==BuildTarget.StandaloneOSX?UnityEngine.Rendering.GraphicsDeviceType.Metal:UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
  if(target==BuildTarget.StandaloneOSX){PlayerSettings.SetArchitecture(BuildTargetGroup.Standalone,2);PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,ScriptingImplementation.Mono2x);}
  CalibrationUiAssets.Prepare();
  BlueprintBuild.Create();
  Directory.CreateDirectory("Assets/Library/Scenes");Directory.CreateDirectory("../Validation/Library");
  File.WriteAllText("../Validation/Library/build-settings.json",new Newtonsoft.Json.Linq.JObject{
   ["unity_version"]=Application.unityVersion,["target"]=target.ToString(),
   ["backend"]=PlayerSettings.GetScriptingBackend(BuildTargetGroup.Standalone).ToString(),
   ["architecture"]=PlayerSettings.GetArchitecture(BuildTargetGroup.Standalone),
   ["graphics_apis"]=new Newtonsoft.Json.Linq.JArray(PlayerSettings.GetGraphicsAPIs(target).Select(api=>api.ToString()))
  }.ToString());
  var artworks=ArtworkCatalog.Builtins.Concat(new[]{ArtworkCatalog.BlueprintTemplate}).ToArray();
  string[] sources={"Assets/Showcase/Scenes/Showcase.unity","Assets/Pool/Scenes/Pool.unity","Assets/Train/Scenes/Train.unity","Assets/Parking/Scenes/Parking.unity","Assets/MiniShotGame/Scenes/MiniShotGame.unity","Assets/Limpool/Scenes/Limpool.unity","Assets/RainyCafe/Scenes/RainyCafe.unity","Assets/Mita/Generated/Mita.unity","Assets/Blueprint/Scenes/Blueprint.unity"};
  for(int i=0;i<sources.Length;i++){
   var scene=EditorSceneManager.OpenScene(sources[i]);
   foreach(var driver in UnityEngine.Object.FindObjectsOfType<PreviewEyeDriver>())UnityEngine.Object.DestroyImmediate(driver);
   foreach(var rig in UnityEngine.Object.FindObjectsOfType<PhysicalDisplayRig>())UnityEngine.Object.DestroyImmediate(rig);
   foreach(var rig in UnityEngine.Object.FindObjectsOfType<SpatialWallpaper.RainyCafe.RainyCafeDisplayRig>())UnityEngine.Object.DestroyImmediate(rig);
   foreach(var capture in UnityEngine.Object.FindObjectsOfType<LimpoolCapture>())UnityEngine.Object.DestroyImmediate(capture);
   foreach(var capture in UnityEngine.Object.FindObjectsOfType<BlueprintCapture>())UnityEngine.Object.DestroyImmediate(capture);
   foreach(var check in UnityEngine.Object.FindObjectsOfType<SpatialWallpaper.Blueprint.BlueprintEditorCheck>())UnityEngine.Object.DestroyImmediate(check);
   foreach(var check in UnityEngine.Object.FindObjectsOfType<SpatialWallpaper.Blueprint.BlueprintFramingCheck>())UnityEngine.Object.DestroyImmediate(check);
   foreach(var check in UnityEngine.Object.FindObjectsOfType<SpatialWallpaper.Blueprint.BlueprintDropCheck>())UnityEngine.Object.DestroyImmediate(check);
   foreach(var capture in UnityEngine.Object.FindObjectsOfType<SpatialWallpaper.RainyCafe.RainyCafeCapture>())UnityEngine.Object.DestroyImmediate(capture);
   foreach(var driver in UnityEngine.Object.FindObjectsOfType<TrackingEyeDriver>())UnityEngine.Object.DestroyImmediate(driver);
   foreach(var cadence in UnityEngine.Object.FindObjectsOfType<AdaptiveRenderCadence>())cadence.adaptive=false;
   var projection=UnityEngine.Object.FindObjectOfType<WindowProjection>();projection.viewportManagedExternally=true;
   if(artworks[i].Id=="mita"){
    projection.screenFrame=new GameObject("PhysicalScreenFrame").transform;
    var behaviour=UnityEngine.Object.FindObjectOfType<SpatialWallpaper.Mita.MitaBehaviour>();behaviour.managed=true;
    var content=GameObject.Find("ContentFrame");content.name="SceneModel";
    GameObject.Find("OriginalMenuBackdrop").transform.SetParent(content.transform,true);
    foreach(var component in behaviour.GetComponents<MonoBehaviour>()){
     var name=component.GetType().Name;
     if(new[]{"MitaTracking","MitaDisplayRig","MitaCapture","MitaGazeAudit","MitaRecording","MitaBenchmark","MitaLifecycle","MitaGestureAudit","MitaInteractionAudit"}.Contains(name))UnityEngine.Object.DestroyImmediate(component);
    }
    behaviour.GetComponent<SpatialWallpaper.Mita.MitaDesktop>().managed=true;
    behaviour.gameObject.AddComponent<MitaLibraryContent>();
    behaviour.gameObject.AddComponent<SceneRenderSettings>().pipeline=null;
   }
   if(artworks[i].Id=="rainycafe"){
    var atmosphere=UnityEngine.Object.FindObjectOfType<SpatialWallpaper.RainyCafe.RainyCafeAtmosphere>();
    var nearWall=GameObject.Find("LeftNearWallReturn").GetComponent<Renderer>();var surfaces=atmosphere.staticSurfaces.ToList();
    // Extend the room facade outside the authored aperture. Flat faces at Z=0
    // avoid overlapping the real floor or ceiling with a cube's end caps.
    foreach(int side in new[]{-1,1}){
     var wall=GameObject.CreatePrimitive(PrimitiveType.Quad);wall.name=side<0?"LowerNearWallReturn":"UpperNearWallReturn";
     UnityEngine.Object.DestroyImmediate(wall.GetComponent<Collider>());wall.transform.SetParent(atmosphere.transform,false);
     wall.transform.localPosition=new Vector3(0,side*(3.0375f*.5f+4),0);
     wall.transform.localRotation=Quaternion.FromToRotation(wall.GetComponent<MeshFilter>().sharedMesh.normals[0],Vector3.back);
     wall.transform.localScale=new Vector3(40,8,1);var renderer=wall.GetComponent<Renderer>();renderer.sharedMaterial=nearWall.sharedMaterial;renderer.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;surfaces.Add(renderer);
    }
    atmosphere.staticSurfaces=surfaces.ToArray();
   }
   if(!UnityEngine.Object.FindObjectOfType<DisplaySceneVolume>()){
    var content=GameObject.Find(i==1?"PoolContent":"SceneModel").transform;
    var reference=new GameObject("Content aperture reference").transform;reference.SetParent(content,false);reference.position=projection.screenFrame.position;reference.rotation=projection.screenFrame.rotation;
    var volume=reference.gameObject.AddComponent<DisplaySceneVolume>();float scale=reference.lossyScale.x;volume.size=new Vector3(.53f/scale,.298125f/scale,projection.contentDepth/scale);volume.showGuide=false;
   }
   if(UnityEngine.Object.FindObjectsOfType<WindowProjection>().Length!=1)throw new Exception("Preview must have one projection");
   EditorSceneManager.SaveScene(scene,artworks[i].ScenePath);
  }
  var manager=EditorSceneManager.OpenScene("Assets/Calibration/Scenes/CalibrationDemo.unity");
  var runtime=UnityEngine.Object.FindObjectOfType<CalibrationRuntime>();
  var smoke=runtime.GetComponent<CalibrationSmoke>();if(smoke)UnityEngine.Object.DestroyImmediate(smoke);
  var library=runtime.gameObject.AddComponent<LibraryPreviewController>();library.Runtime=runtime;
  var tests=runtime.gameObject.AddComponent<LibrarySmoke>();tests.Library=library;
  if(Environment.GetEnvironmentVariable("SPATIAL_DESKTOP_BUILD")=="true"){
   runtime.gameObject.AddComponent<SpatialWallpaper.Desktop.DesktopRuntime>();runtime.gameObject.AddComponent<SpatialWallpaper.Desktop.DesktopManager>();runtime.gameObject.AddComponent<DesktopSmoke>();runtime.gameObject.AddComponent<MainManagerSmoke>();
  }
  const string entry="Assets/Library/Scenes/Library.unity";EditorSceneManager.SaveScene(manager,entry);
  PlayerSettings.productName="Spatial Wallpaper";PlayerSettings.defaultScreenWidth=1440;PlayerSettings.defaultScreenHeight=960;PlayerSettings.fullScreenMode=FullScreenMode.Windowed;PlayerSettings.resizableWindow=true;PlayerSettings.runInBackground=true;
  AssetDatabase.SaveAssets();
  var scenes=new[]{SpatialWallpaper.Desktop.Editor.NativeEntryBuild.Prepare(),entry}.Concat(artworks.Select(x=>x.ScenePath)).ToArray();
  var report=BlueprintBuild.BuildPlayer(scenes,target==BuildTarget.StandaloneOSX?"Builds/Library-macos/SpatialWallpaper.app":"Builds/Library/SpatialWallpaper.exe",target);
  File.WriteAllText("../Validation/Library/build-result.txt",report.summary.result+" errors="+report.summary.totalErrors);
  if(report.summary.result!=BuildResult.Succeeded||report.summary.totalErrors!=0)throw new Exception("Library build failed");
 }
}
