using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
public static class StageABuild {
 public static void Run() {
  GraphicsSettings.renderPipelineAsset=null;QualitySettings.renderPipeline=null;
  AssetDatabase.DeleteAsset("Assets/Generated");
  Directory.CreateDirectory("Assets/Generated");
  var renderer=ScriptableObject.CreateInstance<UniversalRendererData>();
  AssetDatabase.CreateAsset(renderer,"Assets/Generated/Renderer.asset");
  var pipeline=UniversalRenderPipelineAsset.Create(renderer);
  AssetDatabase.CreateAsset(pipeline,"Assets/Generated/Pipeline.asset");
  GraphicsSettings.renderPipelineAsset=pipeline;QualitySettings.renderPipeline=pipeline;
  PlayerSettings.colorSpace=ColorSpace.Linear;
  PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64,false);
  PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64,new[]{GraphicsDeviceType.Direct3D11});
  PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,ScriptingImplementation.Mono2x);
  PlayerSettings.companyName="SpatialWallpaper";PlayerSettings.productName="SdkStageA";
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  var camera=new GameObject("Camera").AddComponent<Camera>();camera.transform.position=new Vector3(0,0,-4);
  camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.02f,.04f,.08f);
  camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
  var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);
  var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));
  material.color=new Color(.1f,.7f,.9f);AssetDatabase.CreateAsset(material,"Assets/Generated/Unlit.mat");
  cube.GetComponent<Renderer>().sharedMaterial=material;
  var box=new GameObject("Interpreter").AddComponent<StageABox>();
  var author=cube.AddComponent<StageAAuthor>();author.target=cube.transform;
  Cilbox.CilboxScenePostprocessor.OnPostprocessScene(scene);
  var probe=camera.gameObject.AddComponent<StageAPlayer>();probe.content=cube.transform;
  EditorSceneManager.SaveScene(scene,"Assets/Generated/Probe.unity");
  var output=Environment.GetEnvironmentVariable("SDK_STAGE_A_OUTPUT");
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Generated/Probe.unity"},target=BuildTarget.StandaloneWindows64,locationPathName=Path.Combine(output,"player/StageA.exe"),options=BuildOptions.Development});
  File.WriteAllText(Path.Combine(output,"build-result.json"),"{\"run_id\":\""+Environment.GetEnvironmentVariable("SDK_STAGE_A_BUILD_ID")+"\",\"result\":\""+report.summary.result+"\",\"errors\":"+report.summary.totalErrors+"}");
  if(report.summary.result!=BuildResult.Succeeded)throw new Exception("stage-a-build-failed");
 }
}
