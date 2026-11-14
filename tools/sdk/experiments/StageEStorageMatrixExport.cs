using System;using System.IO;using UnityEngine;using UnityEditor.SceneManagement;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Security;
public static class StageEStorageMatrixExport {
 public static void Run(){
  string output=Environment.GetEnvironmentVariable("SDK_E_OUTPUT");
  for(int role=1;role<=3;role++){
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=new GameObject("Storage matrix "+role);root.AddComponent<StageEStorageMatrixAuthor>().role=role;
   var license=new JArray(new JObject{{"name","Storage matrix verification"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}});
   var bytes=ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),license);var admitted=Admission.Admit(bytes,"windows-x86_64");string directory=Path.Combine(output,role.ToString());Directory.CreateDirectory(directory);
   File.WriteAllBytes(Path.Combine(directory,"scene.spatial"),bytes);File.WriteAllText(Path.Combine(directory,"author.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_E_RUN")},{"build",admitted.BuildId},{"role",role},{"engine",Application.unityVersion}}.ToString());
  }
 }
}
