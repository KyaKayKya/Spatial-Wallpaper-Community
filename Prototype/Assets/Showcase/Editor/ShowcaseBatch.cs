using UnityEditor;
using UnityEditor.SceneManagement;
using System.IO;
public static class ShowcaseBatch {
 public static void InstallPinlightsAndBuild(){
  UnityEditor.AssetDatabase.Refresh();EditorSceneManager.OpenScene("Assets/Showcase/Scenes/Showcase.unity");
  BakedStudyBuilder.Install();BakedStudyBuilder.Reflection();
  File.WriteAllText("../Validation/Showcase/unity/pinlight-checks.txt",PinlightChecks.Run());
  VerifyAndBuild();
 }
 public static void VerifyAndBuild(){
  EditorSceneManager.OpenScene("Assets/Showcase/Scenes/Showcase.unity");
  foreach(var r in UnityEngine.Object.FindObjectsOfType<UnityEngine.MeshRenderer>())GameObjectUtility.SetStaticEditorFlags(r.gameObject,GameObjectUtility.GetStaticEditorFlags(r.gameObject)&~StaticEditorFlags.BatchingStatic);
  EditorSceneManager.MarkSceneDirty(UnityEngine.SceneManagement.SceneManager.GetActiveScene());EditorSceneManager.SaveOpenScenes();
  var checks=ShowcaseChecks.Run();var build=ShowcaseBuilder.Build();
  File.WriteAllText("../Validation/Showcase/unity/build-checks.txt",checks+"\n"+build+"\n");
  if(!checks.StartsWith("PASS")||!build.StartsWith("Succeeded"))throw new System.Exception(checks+"; "+build);
 }
}
