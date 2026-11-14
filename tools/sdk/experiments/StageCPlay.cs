using System;using System.IO;using UnityEditor;using UnityEngine;
[InitializeOnLoad]
public static class StageCPlay {
 static StageCPlay(){EditorApplication.update+=Observe;}
 static void Observe(){
  if(!SessionState.GetBool("SDK_C_PLAY_REQUESTED",false))return;
  string output=Environment.GetEnvironmentVariable("SDK_C_OUTPUT");
  if(File.Exists(Path.Combine(output,"play-error.txt"))){Debug.LogError(File.ReadAllText(Path.Combine(output,"play-error.txt")));EditorApplication.Exit(31);}
  if(File.Exists(Path.Combine(output,"play-result.json")))EditorApplication.Exit(0);
 }
 public static void Run(){
  StageCPipeline.Configure();var root=StageCChecks.CreateAuthor();var view=StageCVisuals.Create(root.transform);
  var observer=root.AddComponent<StageCPlayObserver>();observer.author=root.GetComponent<StageCAuthor>();observer.view=view;
  SessionState.SetBool("SDK_C_PLAY_REQUESTED",true);EditorApplication.EnterPlaymode();
 }
}
