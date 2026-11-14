using System;using System.IO;using System.Linq;using System.Collections.Generic;using Newtonsoft.Json.Linq;using UnityEditor;using UnityEditor.SceneManagement;using UnityEngine;using UnityEngine.Rendering;using SpatialWallpaper.Sdk;using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Security;
public static class CleanAuthorChecks {
 public static void Run(){string output=Environment.GetEnvironmentVariable("SDK_AUTHOR_OUTPUT");try{
  MiniShotSdkBuilder.Create();foreach(string scene in new[]{"Train","Showcase","Parking","RainyCafe"})WallpaperSceneBuilder.Create(scene);
  var paths=new[]{"Assets/MiniShotGameSdk/Generated/MiniShotGameSdk.unity"}.Concat(new[]{"Train","Showcase","Parking","RainyCafe"}.Select(s=>"Assets/WallpaperSdkScenes/Generated/"+s+"/"+s+"Sdk.unity")).ToArray();var cases=new JArray();
  foreach(string path in paths){EditorSceneManager.OpenScene(path);var definition=UnityEngine.Object.FindObjectOfType<SceneDefinition>();if(!definition)throw new InvalidDataException("missing-clean-scene:"+path);
   var licenses=new JArray(new JObject{{"name",definition.displayName},{"source",definition.licenseSource},{"license",definition.licenseName},{"attribution",definition.licenseAttribution}});
   var bytes=ScriptSceneExporter.Export(definition.contentRoot.gameObject,definition.authorFrame,licenses);var admitted=Admission.Admit(bytes,"windows-x86_64");cases.Add(new JObject{{"scene",path},{"build",admitted.BuildId},{"bytes",bytes.Length},{"sdk",admitted.Manifest["sdk"]},{"capabilities",JArray.FromObject(admitted.CapabilityTags)}});
  }
  var roots=paths.Concat(new[]{AssetDatabase.GetAssetPath(GraphicsSettings.defaultRenderPipeline)}).Where(p=>!string.IsNullOrEmpty(p)).ToArray();var dependencies=AssetDatabase.GetDependencies(roots,true).Where(p=>p.StartsWith("Assets/",StringComparison.Ordinal)).OrderBy(p=>p).ToArray();
  string package=UnityEditor.PackageManager.PackageInfo.FindForAssembly(typeof(SceneDefinition).Assembly).resolvedPath;
  EditorSceneManager.OpenScene(paths[0]);AssetDatabase.SaveAssets();File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"status","passed-clean-installed-five-author-build-export-admission-not-RC"},{"cases",cases},{"dependencies",JArray.FromObject(dependencies)},{"resolved_sdk",package},{"scope","fresh Library, installed final SDK tarball, regenerate and compile/export/admit five scenes; no LPAC replay or RC"}}.ToString());EditorApplication.Exit(0);
 }catch(Exception error){File.WriteAllText(Path.Combine(output,"error.txt"),error.ToString());Debug.LogException(error);EditorApplication.Exit(1);}}
}
