using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;

public static class SdkBuild {
 public static void Run(){
  string output=Environment.GetEnvironmentVariable("SDK_BUILD_OUTPUT");Directory.CreateDirectory("Assets/Generated");
  PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{UnityEngine.Rendering.GraphicsDeviceType.Direct3D11});
  var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();AssetDatabase.CreateAsset(renderer,"Assets/Generated/Renderer.asset");var pipeline=UniversalRenderPipelineAsset.Create(renderer);AssetDatabase.CreateAsset(pipeline,"Assets/Generated/Pipeline.asset");UnityEngine.Rendering.GraphicsSettings.renderPipelineAsset=pipeline;QualitySettings.renderPipeline=pipeline;
  Player(output);
 }
 static void Player(string output){
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var camera=new GameObject("DisplayCamera").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.025f,.035f,.045f);camera.gameObject.AddComponent<UniversalAdditionalCameraData>();camera.gameObject.AddComponent<WindowProjection>();
  var bootstrap=new GameObject("ContentPlayer").AddComponent<ContentPlayerBootstrap>();bootstrap.displayCamera=camera;
  Directory.CreateDirectory("Assets/Resources");var material=new Material(Shader.Find("Universal Render Pipeline/Lit")){enableInstancing=true};material.EnableKeyword("LIGHTMAP_ON");AssetDatabase.CreateAsset(material,"Assets/Resources/PlatformLit.mat");AssetDatabase.CreateAsset(new Material(Shader.Find("Universal Render Pipeline/Unlit")),"Assets/Resources/PlatformUnlit.mat");
  EditorSceneManager.SaveScene(scene,"Assets/Generated/Player.unity");PlayerSettings.companyName="SpatialWallpaper";PlayerSettings.productName="ContentPlayer";PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,ScriptingImplementation.Mono2x);PlayerSettings.stripEngineCode=false;
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Generated/Player.unity"},target=BuildTarget.StandaloneWindows64,locationPathName=Path.Combine(output,"player/ContentPlayer.exe"),options=BuildOptions.Development});
  File.WriteAllText(Path.Combine(output,"player-build-result.txt"),report.summary.result+" errors="+report.summary.totalErrors);if(report.summary.result!=BuildResult.Succeeded)throw new InvalidOperationException("Player-build-failed");
 }
}
