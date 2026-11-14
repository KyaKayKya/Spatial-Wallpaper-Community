using UnityEditor;using UnityEngine;using UnityEngine.Rendering;using UnityEngine.Rendering.Universal;
public static class StageCPipeline {
 public static void Configure(){
  System.IO.Directory.CreateDirectory("Assets/Generated");
  SpatialWallpaper.Sdk.Editor.TrustedRenderProfileBuilder.Install("Assets/Generated");
  PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,ScriptingImplementation.Mono2x);
  PlayerSettings.companyName="SpatialWallpaper";PlayerSettings.productName="SdkStageC";
 }
}
