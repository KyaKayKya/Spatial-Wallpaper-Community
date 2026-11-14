using System.IO;
using UnityEngine;
using UnityEditor;
using UnityEditor.SceneManagement;

namespace SpatialWallpaper.Desktop.Editor {
 public static class NativeEntryBuild {
  public static string Prepare(){
   if(File.Exists(NativeRenderEntry.ScenePath))return NativeRenderEntry.ScenePath;
   Directory.CreateDirectory(Path.GetDirectoryName(NativeRenderEntry.ScenePath));var scene=EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   new GameObject("Product role entry").AddComponent<NativeRenderEntry>();EditorSceneManager.SaveScene(scene,NativeRenderEntry.ScenePath);AssetDatabase.SaveAssets();return NativeRenderEntry.ScenePath;
  }
 }
}
