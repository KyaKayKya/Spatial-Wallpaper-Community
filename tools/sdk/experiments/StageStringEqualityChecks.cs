using System;using System.IO;using System.Linq;using UnityEngine;using UnityEditor.SceneManagement;using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Editor;using SpatialWallpaper.Sdk.Security;using SpatialWallpaper.Sdk.Execution;
public static class StageStringEqualityChecks {
 static JArray SignatureNegatives(){var results=new JArray();
  foreach(string name in new[]{"return-type","instance","generic-method","parameter-type"}){
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var root=new GameObject("Malformed string metadata");root.AddComponent<StageStringEqualityAuthor>();CilboxProgramCompiler.Compile(root);
   var assembly=Cilbox.CilboxScenePostprocessor.LastCompiledAssembly;var token=assembly.metadata.First(t=>t.name=="op_Equality"&&t.typeDescriptor.typeName=="System.String");
   if(name=="return-type")token.methodFullSignature=token.methodFullSignature.Replace("Boolean","Int32");
   else if(name=="instance")token.isStatic=false;
   else if(name=="generic-method")token.methodGenericArguments=new[]{new Cilbox.SerializedTypeDescriptor{assemblyName="mscorlib",typeName="System.String"}};
   else token.methodParameters[0]=new Cilbox.SerializedTypeDescriptor{assemblyName="mscorlib",typeName="System.Int32"};
   var converterType=typeof(CilboxProgramCompiler).GetNestedType("Converter",System.Reflection.BindingFlags.NonPublic);var originals=new System.Collections.Generic.Dictionary<string,Type>{{typeof(StageStringEqualityAuthor).FullName,typeof(StageStringEqualityAuthor)}};var converter=Activator.CreateInstance(converterType,new object[]{assembly,originals});string reason=null;
   try{converterType.GetMethod("Convert").Invoke(converter,null);}catch(System.Reflection.TargetInvocationException error){if(error.InnerException is InvalidDataException)reason=error.InnerException.Message;else throw;}
   if(reason!="csharp-subset:string-operator-signature")throw new Exception("string-signature-not-rejected:"+name+":"+reason);results.Add(new JObject{{"case",name},{"rejected",reason}});
  }return results;
 }
 public static void Run(){string output=Environment.GetEnvironmentVariable("SDK_STRING_OUTPUT");var cases=new JArray();
  string[] left={null,null,"same",new string(new[]{'s','a','m','e'}),"same",""};string[] right={null,"same",null,new string(new[]{'s','a','m','e'}),"different",""};
  if(object.ReferenceEquals(left[3],right[3]))throw new Exception("equal-value-control-shares-instance");
  for(int i=0;i<left.Length;i++){
   EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var controlRoot=new GameObject("Ordinary string equality");var control=controlRoot.AddComponent<StageStringEqualityAuthor>();control.left=left[i];control.right=right[i];control.Start();var expected=new[]{"equal","unequal","returned","branch"}.ToDictionary(n=>n,n=>typeof(StageStringEqualityAuthor).GetField(n).GetValue(control));UnityEngine.Object.DestroyImmediate(controlRoot);
   var root=new GameObject("Converted string equality");var author=root.AddComponent<StageStringEqualityAuthor>();author.left=left[i];author.right=right[i];var compiled=CilboxProgramCompiler.Compile(root);File.WriteAllText(Path.Combine(output,"program-"+i+".json"),compiled.Program.ToString());
   var license=new JArray(new JObject{{"name","String value equality regression"},{"source","SpatialWallpaper tools/sdk"},{"license","MIT"},{"attribution","SpatialWallpaper contributors"}});var bytes=ScriptSceneExporter.Export(root,new Vector3(.6f,.3375f,.5f),license);File.WriteAllBytes(Path.Combine(output,"string-"+i+".spatial"),bytes);var build=Admission.Admit(bytes,"windows-x86_64");
   using(var domain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new UnityOwnedHost(build,domain)){var vm=new VmRuntime(build,domain,host,new ExecutionQuota(10000,32,1000));host.Bind(vm);if(i==3&&object.ReferenceEquals(vm.Field(0,compiled.FieldId(typeof(StageStringEqualityAuthor),"left")),vm.Field(0,compiled.FieldId(typeof(StageStringEqualityAuthor),"right"))))throw new Exception("equal-value-vm-shares-instance");vm.Start();foreach(var item in expected)if(!Equals(vm.Field(0,compiled.FieldId(typeof(StageStringEqualityAuthor),item.Key)),item.Value))throw new Exception("string-value-equality-mismatch:"+i+":"+item.Key);vm.Stop();}
   cases.Add(new JObject{{"left",left[i]},{"right",right[i]},{"expected",JObject.FromObject(expected)},{"vm_matches",true}});
  }
  EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);var bad=new GameObject("Non-string lookalike operator");bad.AddComponent(System.Reflection.Assembly.Load("SdkStringNegative").GetType("StringNegativeAuthor",true));bool rejected=false;string reason=null;
  try{CilboxProgramCompiler.Compile(bad);}catch(InvalidDataException error){reason=error.Message;rejected=reason.Contains("member:");}if(!rejected)throw new Exception("non-string-op-Equality-was-not-rejected:"+reason);
  File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"run_id",Environment.GetEnvironmentVariable("SDK_STRING_RUN")},{"cases",cases},{"distinct_equal_instances",true},{"lookalike_rejected",reason},{"signature_negatives",SignatureNegatives()}}.ToString());
 }
}
