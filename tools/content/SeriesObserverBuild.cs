using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.Build.Reporting;
public static class SeriesObserverBuild {
 public static void Run(){EditorSceneManager.OpenScene("Assets/Generated/Runtime.unity");var result=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Generated/Runtime.unity"},target=BuildTarget.StandaloneWindows64,locationPathName=Environment.GetEnvironmentVariable("CONTENT_SERIES_BUILD"),options=BuildOptions.Development});if(result.summary.result!=BuildResult.Succeeded)throw new Exception("series-observer-build-failed");}
}
