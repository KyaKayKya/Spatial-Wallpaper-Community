using System;using System.IO;using UnityEditor;using UnityEditor.SceneManagement;using UnityEditor.Build.Reporting;using UnityEngine;using SpatialWallpaper.Sdk.Hosting;using Newtonsoft.Json.Linq;
public static class SdkWorkerBuild {
 public static void Run(){
  AssetDatabase.DeleteAsset("Assets/Generated");Directory.CreateDirectory("Assets/Generated");
  var renderReceipt=SpatialWallpaper.Sdk.Editor.TrustedRenderProfileBuilder.Install("Assets/Generated");
  PlayerSettings.SetScriptingBackend(BuildTargetGroup.Standalone,ScriptingImplementation.Mono2x);
  PlayerSettings.enableFrameTimingStats=true;PlayerSettings.companyName="SpatialWallpaper";PlayerSettings.productName="SpatialWallpaper.Worker";PlayerSettings.runInBackground=true;
  var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);new GameObject("SDK Worker").AddComponent<WorkerRuntime>();
  foreach(string shaderName in new[]{"Universal Render Pipeline/Unlit","Universal Render Pipeline/Lit"}){
   var shader=Shader.Find(shaderName);if(!shader)throw new Exception("missing-pinned-shader");var material=new Material(shader);AssetDatabase.CreateAsset(material,"Assets/Generated/"+shaderName.Substring(shaderName.LastIndexOf('/')+1)+".mat");
   var retained=new GameObject("Builtin shader retention");retained.SetActive(false);retained.AddComponent<MeshRenderer>().sharedMaterial=material;
  }
  EditorSceneManager.SaveScene(scene,"Assets/Generated/Worker.unity");string output=Environment.GetEnvironmentVariable("SDK_WORKER_OUTPUT");
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Generated/Worker.unity"},target=BuildTarget.StandaloneWindows64,locationPathName=Path.Combine(output,"player/Worker.exe"),options=BuildOptions.None});
  File.WriteAllText(Path.Combine(output,"worker-build.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_WORKER_RUN")},{"result",report.summary.result.ToString()},{"errors",report.summary.totalErrors},{"render_profile",renderReceipt},{"frame_protocol",WorkerFrameCodec.Version},{"projection_protocol",WorkerProjectionCodec.Version}}.ToString());
  if(report.summary.result!=BuildResult.Succeeded)throw new Exception("SDK-worker-build");
 }
}
