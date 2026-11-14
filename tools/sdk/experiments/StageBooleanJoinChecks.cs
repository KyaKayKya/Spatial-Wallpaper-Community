using System;using System.IO;using System.Linq;using System.Reflection;using System.Collections.Generic;
using UnityEngine;using UnityEditor.SceneManagement;using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Security;using SpatialWallpaper.Sdk.Execution;
public static class StageBooleanJoinChecks {
 public static void Run(){
  string output=Environment.GetEnvironmentVariable("SDK_BOOL_OUTPUT");var observations=new JArray();
  for(int bits=0;bits<8;bits++){
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var ordinaryRoot=new GameObject("Ordinary boolean control");var ordinary=ordinaryRoot.AddComponent<StageBooleanJoinAuthor>();ordinary.a=(bits&1)!=0;ordinary.b=(bits&2)!=0;ordinary.c=(bits&4)!=0;ordinary.Start();
   var expected=typeof(StageBooleanJoinAuthor).GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly).ToDictionary(f=>f.Name,f=>f.GetValue(ordinary));UnityEngine.Object.DestroyImmediate(ordinaryRoot);
   var root=new GameObject("Converted boolean control");var author=root.AddComponent<StageBooleanJoinAuthor>();author.a=(bits&1)!=0;author.b=(bits&2)!=0;author.c=(bits&4)!=0;
   var compiled=CilboxProgramCompiler.Compile(root);File.WriteAllText(Path.Combine(output,"program-"+bits+".json"),compiled.Program.ToString());
   var license=new JArray(new JObject{{"name","Boolean join regression"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}});
   var bytes=ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),license);var build=Admission.Admit(bytes,"windows-x86_64");File.WriteAllBytes(Path.Combine(output,"boolean-"+bits+".spatial"),bytes);
   using(var domain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new UnityOwnedHost(build,domain)){
    var runtime=new VmRuntime(build,domain,host,new ExecutionQuota(10000,32,1000));host.Bind(runtime);runtime.Start();
    foreach(var pair in expected){var actual=runtime.Field(0,compiled.FieldId(typeof(StageBooleanJoinAuthor),pair.Key));if(!Equals(actual,pair.Value))throw new Exception("boolean-join-mismatch:"+bits+":"+pair.Key+":"+actual+":"+pair.Value);}
    if((int)expected["effects"]!=1||(int)expected["mask"]!=-1||(int)expected["xor"]!=-3)throw new Exception("ordinary-control-invalid");runtime.Stop();
   }
   observations.Add(new JObject{{"inputs",bits},{"expected",JObject.FromObject(expected)},{"vm_matches",true}});
  }
  File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_BOOL_RUN")},{"cases",observations}}.ToString());
 }
}
