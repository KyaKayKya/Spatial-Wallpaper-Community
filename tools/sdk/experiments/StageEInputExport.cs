using System;using System.IO;using UnityEngine;using UnityEditor.SceneManagement;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Security;
public static class StageEInputExport {
 public static void Run(){
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=new GameObject("Original input author");bool runtime=Environment.GetEnvironmentVariable("SDK_E_INPUT_RUNTIME")=="1";
  bool continuity=Environment.GetEnvironmentVariable("SDK_E_INPUT_CONTINUITY")=="1";
  if(continuity)root.AddComponent<StageEInputContinuityAuthor>();else if(runtime)root.AddComponent<StageEInputRuntimeAuthor>();else root.AddComponent<StageEInputAuthor>();
  var license=new JArray(new JObject{{"name","Input capability verification"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}});
  string output=Environment.GetEnvironmentVariable("SDK_E_OUTPUT");byte[] package=ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),license);var build=Admission.Admit(package,"windows-x86_64");File.WriteAllBytes(Path.Combine(output,"scene.spatial"),package);
  if(string.Join(",",build.CapabilityTags)!=(runtime||continuity?"input,private-storage":"input"))throw new Exception("input-author-capability-closure");
  File.WriteAllText(Path.Combine(output,"author.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_E_RUN")},{"build",build.BuildId},{"capabilities",new JArray(build.CapabilityTags)},{"engine",Application.unityVersion},{"fixture",continuity?"input-continuity-14-v1":runtime?"input-runtime":"input-basic"}}.ToString());
 }
}
