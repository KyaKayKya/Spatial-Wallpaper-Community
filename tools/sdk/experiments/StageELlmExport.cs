using System;using System.IO;using UnityEngine;using UnityEditor.SceneManagement;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk;using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Security;
public static class StageELlmExport {
 public static void Run(){
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=new GameObject("Original LLM author");root.AddComponent<StageELlmAuthor>();
  var license=new JArray(new JObject{{"name","LLM capability verification"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}});
  string output=Environment.GetEnvironmentVariable("SDK_E_OUTPUT");byte[] package=ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),license);var build=Admission.Admit(package,"windows-x86_64");File.WriteAllBytes(Path.Combine(output,"scene.spatial"),package);
  File.WriteAllText(Path.Combine(output,"author.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_E_RUN")},{"build",build.BuildId},{"engine",Application.unityVersion}}.ToString());
 }
}
