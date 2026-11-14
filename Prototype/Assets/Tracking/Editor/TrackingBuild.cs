using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

public static class TrackingBuild {
 public static void VerifyAndBuild(){
  EditorSceneManager.OpenScene("Assets/Showcase/Scenes/Showcase.unity");
  string output=Path.GetFullPath("../Validation/Integration");Directory.CreateDirectory(output);
  string projection=ShowcaseChecks.Run();string lights=PinlightChecks.Run();
  File.WriteAllText(Path.Combine(output,"unity-checks.txt"),projection+"\n"+lights+"\n");
  string result=ShowcaseBuilder.Build();
  File.WriteAllText(Path.Combine(output,"build.txt"),result+"\n");
  if(!projection.StartsWith("PASS")||!lights.StartsWith("PASS")||!result.StartsWith("Succeeded"))throw new Exception("Tracking build validation failed");
 }
}
