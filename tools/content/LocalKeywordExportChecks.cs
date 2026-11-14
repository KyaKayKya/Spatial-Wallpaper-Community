using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using SpatialWallpaper.Sdk;
using SpatialWallpaper.Sdk.Editor;
public static class LocalKeywordExportChecks {
 public static void Run(){
  var shader=Shader.Find("URPWater/Standard");if(!shader)throw new InvalidOperationException("actual-water-shader-missing");
  var author=new GameObject("Two keyword sets author").AddComponent<SceneDefinition>();var root=new GameObject("Content");author.contentRoot=root.transform;
  var a=new Material(shader);var b=new Material(shader);a.EnableKeyword("_CAUSTICS_ON");b.DisableKeyword("_CAUSTICS_ON");
  foreach(var material in new[]{a,b}){var cube=GameObject.CreatePrimitive(PrimitiveType.Cube);UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());cube.transform.SetParent(root.transform);cube.GetComponent<MeshRenderer>().sharedMaterial=material;}
  bool rejected=false;string error=null;string output=Environment.GetEnvironmentVariable("CONTENT_KEYWORD_CHECK_OUTPUT");
  try{ScenePackageExporter.Export(author,Path.Combine(output,"two-keywords.spatial"),ExportMode.LocalValidation);}
  catch(InvalidDataException failure){error=failure.Message;rejected=error=="multiple-local-keyword-combinations-unsupported";}
  finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(author.gameObject);UnityEngine.Object.DestroyImmediate(a);UnityEngine.Object.DestroyImmediate(b);}
  File.WriteAllText(Path.Combine(output,"result.json"),new JObject{["passed"]=rejected,["error"]=error,["scope"]="Actual author export with the same water shader and distinct local keyword sets; unsupported combinations must fail explicitly"}.ToString());
  if(!rejected)throw new InvalidOperationException("unsafe-local-keyword-combinations-accepted");
 }
}
