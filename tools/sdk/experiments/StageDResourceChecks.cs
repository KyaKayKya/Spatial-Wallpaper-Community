using System;using System.IO;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;
[InitializeOnLoad]public static class StageDResourceChecks{
 static StageDResourceChecks(){EditorApplication.update+=Observe;}
 static void Observe(){
  if(!SessionState.GetBool("SDK_D_RESOURCE",false))return;
  string output=Environment.GetEnvironmentVariable("SDK_D_OUTPUT");
  if(File.Exists(Path.Combine(output,"error.txt"))){Debug.LogError(File.ReadAllText(Path.Combine(output,"error.txt")));EditorApplication.Exit(31);}
  if(File.Exists(Path.Combine(output,"result.json")))EditorApplication.Exit(0);
 }
 public static void Run(){EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);StageCPipeline.Configure();new GameObject("ResourceProbe").AddComponent<StageDResourceObserver>();SessionState.SetBool("SDK_D_RESOURCE",true);EditorApplication.EnterPlaymode();}
}
