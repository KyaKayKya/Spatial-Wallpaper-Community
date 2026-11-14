using System;using System.IO;using UnityEditor;using UnityEditor.SceneManagement;using UnityEditor.Build.Reporting;using UnityEngine;using Newtonsoft.Json.Linq;
public static class StageCBuild {
 public static void Run(){
  StageCPipeline.Configure();var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
  var host=new GameObject("Trusted test host");host.AddComponent<StageCWorker>();
  // Keep the exact fixture shader in the build without a hidden fallback.
  var material=new Material(Shader.Find("Universal Render Pipeline/Unlit"));AssetDatabase.CreateAsset(material,"Assets/Generated/Fixture.mat");
  var retained=new GameObject("Shader retention");retained.SetActive(false);retained.AddComponent<MeshRenderer>().sharedMaterial=material;
  EditorSceneManager.SaveScene(scene,"Assets/Generated/Worker.unity");string output=Environment.GetEnvironmentVariable("SDK_C_OUTPUT");
  var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Generated/Worker.unity"},target=BuildTarget.StandaloneWindows64,locationPathName=Path.Combine(output,"player/StageC.exe"),options=BuildOptions.Development});
  File.WriteAllText(Path.Combine(output,"worker-build.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_C_RUN")},{"result",report.summary.result.ToString()},{"errors",report.summary.totalErrors}}.ToString());
  if(report.summary.result!=BuildResult.Succeeded)throw new Exception("stage-C-worker-build");
 }
}
