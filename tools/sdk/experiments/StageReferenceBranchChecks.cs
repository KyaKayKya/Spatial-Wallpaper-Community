using System;using System.IO;using System.Linq;using System.Collections.Generic;
using UnityEngine;using UnityEditor.SceneManagement;using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Security;using SpatialWallpaper.Sdk.Execution;
public static class StageReferenceBranchChecks {
 static void Setup(StageReferenceBranchAuthor author,int mode){author.value=mode==0?null:mode==1?"":"stored";author.items=mode==0?null:new int[0];author.list=mode==0?null:new List<int>();}
 public static void Run(){string output=Environment.GetEnvironmentVariable("SDK_REF_OUTPUT");var observations=new JArray();
  for(int mode=0;mode<3;mode++){
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
   var ordinaryRoot=new GameObject("Ordinary reference branch");var ordinary=ordinaryRoot.AddComponent<StageReferenceBranchAuthor>();Setup(ordinary,mode);ordinary.Start();
   var names=new[]{"copied","equal","unequal","arrayResult","listResult","present"};var expected=names.ToDictionary(n=>n,n=>typeof(StageReferenceBranchAuthor).GetField(n).GetValue(ordinary));UnityEngine.Object.DestroyImmediate(ordinaryRoot);
   var root=new GameObject("Converted reference branch");var author=root.AddComponent<StageReferenceBranchAuthor>();Setup(author,mode);
   var compiled=CilboxProgramCompiler.Compile(root);File.WriteAllText(Path.Combine(output,"program-"+mode+".json"),compiled.Program.ToString());
   var license=new JArray(new JObject{{"name","Reference branch regression"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}});
   var bytes=ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),license);var build=Admission.Admit(bytes,"windows-x86_64");File.WriteAllBytes(Path.Combine(output,"reference-"+mode+".spatial"),bytes);
   using(var domain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new UnityOwnedHost(build,domain)){
    var runtime=new VmRuntime(build,domain,host,new ExecutionQuota(10000,32,1000));host.Bind(runtime);runtime.Start();foreach(var pair in expected){var actual=runtime.Field(0,compiled.FieldId(typeof(StageReferenceBranchAuthor),pair.Key));if(!Equals(actual,pair.Value))throw new Exception("reference-branch-mismatch:"+mode+":"+pair.Key+":"+actual+":"+pair.Value);}runtime.Stop();
   }
   observations.Add(new JObject{{"mode",mode},{"expected",JObject.FromObject(expected)},{"vm_matches",true}});
  }
  File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_REF_RUN")},{"cases",observations}}.ToString());
 }
}
