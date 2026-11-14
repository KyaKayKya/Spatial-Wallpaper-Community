using System;using System.IO;using UnityEngine;using UnityEditor.SceneManagement;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Security;
public static class StageEStorageExport {
 public static void Run(){
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=new GameObject("Storage author");root.AddComponent<StageEStorageAuthor>();
  var license=new JArray(new JObject{{"name","Storage capability verification"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}});
  var bytes=ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),license);var admitted=Admission.Admit(bytes,"windows-x86_64");string output=Environment.GetEnvironmentVariable("SDK_E_OUTPUT");
  File.WriteAllBytes(Path.Combine(output,"scene.spatial"),bytes);File.WriteAllText(Path.Combine(output,"author.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_E_RUN")},{"build",admitted.BuildId},{"engine",Application.unityVersion}}.ToString());
 }
}
