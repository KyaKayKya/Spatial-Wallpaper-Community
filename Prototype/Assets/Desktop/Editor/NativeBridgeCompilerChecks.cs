#if UNITY_EDITOR
using System;
using System.IO;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEditor.SceneManagement;
using SpatialWallpaper.Sdk.Editor;

namespace SpatialWallpaper.Desktop.Editor {
 public static class NativeBridgeCompilerChecks {
  public static void Run(){
   string output=Environment.GetEnvironmentVariable("SPATIAL_SCENE_COMMAND_CHECK_OUTPUT");
   if(string.IsNullOrEmpty(output))throw new InvalidOperationException("explicit-native-compiler-check-output-required");Directory.CreateDirectory(output);
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var root=new GameObject("Owned transient compiler fixture");var mesh=new Mesh();
   try{
    var covered=root.AddComponent<NativeBridgeCompilerProbe>();covered.target=root.transform;
    var program=CilboxProgramCompiler.CompileForNativeBridge(root).Program;
    File.WriteAllText(Path.Combine(output,"covered-program.json"),program.ToString());
    UnityEngine.Object.DestroyImmediate(covered);
    var unsupported=root.AddComponent<NativeBridgeUnsupportedProbe>();unsupported.shape=mesh;
    // This is a valid OLD SDK call, so a generic converter failure is not enough.
    CilboxProgramCompiler.Compile(root);
    bool rejected=false;
    try{CilboxProgramCompiler.CompileForNativeBridge(root);}catch(InvalidOperationException failure){
     if(!failure.Message.StartsWith("native-export-missing-binding:",StringComparison.Ordinal)||!failure.Message.Contains("Mesh::Clear"))throw;rejected=true;
    }
    if(!rejected)throw new InvalidOperationException("native-export-accepted-uncovered-mesh-member");
    File.WriteAllText(Path.Combine(output,"compiler-checks.json"),new JObject{{"covered_transform",true},{"legacy_mesh_compiles",true},{"native_mesh_export_rejected",true},{"scene_saved",false}}.ToString());
    Debug.Log("Native bridge author compiler checks passed");
   }finally{UnityEngine.Object.DestroyImmediate(root);UnityEngine.Object.DestroyImmediate(mesh);}
  }
 }
}
#endif
