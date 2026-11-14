using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public static class LimpoolBake {
 static bool finished,failed;static string failure;static double started;static ftRenderLightmap baker;
 public static void Bake(){
  EditorSceneManager.OpenScene("Assets/Limpool/Scenes/Limpool.unity");
  LimpoolBuilder.RepairLeftJoint();
  foreach(var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))if(t.name=="DynamicWaterLightReceivers"||t.name=="SkylightVolume"||t.name=="WaterSurface"||t.name=="SeaLightColumns")t.gameObject.SetActive(true);
  LimpoolLook.Apply();
  GameObject.Find("SeaLightColumns").SetActive(false);
  var pipeline=AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Limpool/Settings/LimpoolURP.asset");QualitySettings.renderPipeline=pipeline;GraphicsSettings.defaultRenderPipeline=pipeline;
  GameObject.Find("DynamicWaterLightReceivers").SetActive(false);GameObject.Find("SkylightVolume").SetActive(false);GameObject.Find("WaterSurface").SetActive(false);
  var sun=GameObject.Find("LimpoolSun").GetComponent<Light>();sun.transform.rotation=Quaternion.LookRotation(LimpoolLook.SunDirection);var direct=sun.GetComponent<BakeryDirectLight>();if(!direct)direct=sun.gameObject.AddComponent<BakeryDirectLight>();direct.color=sun.color;direct.intensity=sun.intensity;direct.samples=64;direct.shadowSpread=.02f;direct.bakeToIndirect=false;direct.indirectIntensity=1;
  var sky=UnityEngine.Object.FindObjectOfType<BakerySkyLight>();if(!sky)sky=new GameObject("BakedCoolSky").AddComponent<BakerySkyLight>();sky.transform.SetParent(GameObject.Find("LimpoolContent").transform,false);sky.color=new Color(.6f,.78f,.9f);sky.intensity=1.1f;sky.samples=64;sky.hemispherical=false;sky.bakeToIndirect=true;
  baker=ScriptableObject.CreateInstance<ftRenderLightmap>();baker.LoadRenderSettings();ftRenderLightmap.scenePath=Path.GetFullPath("../.local-assets/limpool-bakery");Directory.CreateDirectory(ftRenderLightmap.scenePath);ftBuildGraphics.scenePath=ftRenderLightmap.scenePath;ftRenderLightmap.scenePathQuoted="\""+ftRenderLightmap.scenePath+"\"";
  baker.userRenderMode=ftRenderLightmap.RenderMode.Indirect;ftRenderLightmap.renderDirMode=ftRenderLightmap.RenderDirMode.DominantDirection;baker.texelsPerUnit=ftBuildGraphics.texelsPerUnit=1500;ftRenderLightmap.bounces=4;baker.giSamples=64;baker.denoise=true;baker.fixSeams=true;baker.exeMode=true;baker.unloadScenesInDeferredMode=false;ftBuildGraphics.autoAtlas=true;ftBuildGraphics.minAutoResolution=512;ftBuildGraphics.maxAutoResolution=2048;ftBuildGraphics.overwriteWarning=false;ftRenderLightmap.useScenePath=false;ftRenderLightmap.outputPath="Limpool/Lighting";ftRenderLightmap.clientMode=false;ftRenderLightmap.unityInBatchMode=Application.isBatchMode;ftRenderLightmap.suppressPopups=true;ftLightmaps.GetProjectSettings().autoRenderRefProbes=false;ftLightmaps.GetProjectSettings().optimizedLODs=false;
  ftRenderLightmap.rtxMode=true;ftRenderLightmap.denoiserType=ftGlobalStorage.DenoiserType.OpenImageDenoise;
  baker.SaveRenderSettings();baker.LoadRenderSettings();EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();started=EditorApplication.timeSinceStartup;ftRenderLightmap.OnFinishedFullRender+=Done;Application.logMessageReceived+=Log;EditorApplication.update+=Watch;baker.RenderButton(false);
 }
 static void Done(object sender,EventArgs args){finished=true;}
 static void Log(string message,string stack,LogType type){if(type==LogType.Error||type==LogType.Exception){failed=true;failure=message+stack;}}
 static void Watch(){
  if(failed||EditorApplication.timeSinceStartup-started>1800){File.WriteAllText("../Validation/Limpool/bake-result.txt","FAILED "+failure);EditorApplication.Exit(1);return;}
  if(!finished||ftRenderLightmap.bakeInProgress)return;
  if(!LightmapSettings.lightmaps.Any(x=>x.lightmapColor&&x.lightmapDir)){failure="No directional lightmaps";failed=true;return;}
  foreach(var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))if(t.name=="DynamicWaterLightReceivers"||t.name=="SkylightVolume"||t.name=="WaterSurface"||t.name=="SeaLightColumns")t.gameObject.SetActive(true);
  EditorSceneManager.SaveOpenScenes();AssetDatabase.SaveAssets();File.WriteAllText("../Validation/Limpool/bake-result.txt","Succeeded maps="+LightmapSettings.lightmaps.Length+"; indirect-only sun, directional maps, stable cool sky");EditorApplication.Exit(0);
 }
}
