using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
using UnityEngine;
using UnityEngine.Rendering;
public static class PoolReferenceBuild {
 public static void Run(){
  EditorSceneManager.OpenScene("Assets/Library/Scenes/pool.unity");foreach(var root in UnityEngine.SceneManagement.SceneManager.GetActiveScene().GetRootGameObjects())foreach(var t in root.GetComponentsInChildren<Transform>(true))if(GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(t.gameObject)!=0)throw new Exception("reference-scene-missing-component:"+t.name);
  var pipeline=UnityEngine.Object.FindObjectOfType<SceneRenderSettings>().pipeline;GraphicsSettings.defaultRenderPipeline=pipeline;QualitySettings.renderPipeline=pipeline;UnityEngine.Object.FindObjectsOfType<Camera>().Single(c=>c.name=="DisplayCamera").gameObject.AddComponent<PoolReferenceCapture>();
  PlayerSettings.companyName="SpatialWallpaperValidation";PlayerSettings.productName="PoolReference";PlayerSettings.colorSpace=ColorSpace.Linear;PlayerSettings.runInBackground=true;PlayerSettings.SetApiCompatibilityLevel(BuildTargetGroup.Standalone,ApiCompatibilityLevel.NET_Unity_4_8);PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
  Directory.CreateDirectory("Assets/Generated");EditorSceneManager.SaveScene(UnityEngine.SceneManagement.SceneManager.GetActiveScene(),"Assets/Generated/PoolReference.unity");var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Generated/PoolReference.unity"},target=BuildTarget.StandaloneWindows64,locationPathName=Environment.GetEnvironmentVariable("CONTENT_REFERENCE_BUILD"),options=BuildOptions.Development});if(result.summary.result!=BuildResult.Succeeded)throw new Exception("reference-player-build-failed");
 }
}
