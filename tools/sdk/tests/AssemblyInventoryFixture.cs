// Emits PE metadata only; no candidate code is executed by the inventory tests.
using System;using System.IO;using Mono.Cecil;
static class AssemblyInventoryFixture {
 static TypeDefinition Add(ModuleDefinition module,string name){var type=new TypeDefinition("",name,TypeAttributes.Public|TypeAttributes.Class,module.TypeSystem.Object);module.Types.Add(type);return type;}
 static void Nested(ModuleDefinition module,TypeDefinition parent,string name){parent.NestedTypes.Add(new TypeDefinition("",name,TypeAttributes.NestedPrivate|TypeAttributes.Class,module.TypeSystem.Object));}
 static void Main(string[] args){string output=args[0],host=args[1],mutation=args[2];Directory.CreateDirectory(output);
  using(var sdk=AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("SpatialWallpaper.Sdk",new Version(1,0)),"SpatialWallpaper.Sdk",ModuleKind.Dll))sdk.Write(Path.Combine(output,"SpatialWallpaper.Sdk.dll"));
  using(var assembly=AssemblyDefinition.CreateAssembly(new AssemblyNameDefinition("Assembly-CSharp",new Version(1,0)),"Assembly-CSharp",ModuleKind.Dll)){
   var module=assembly.MainModule;Add(module,"StageCVisuals");var worker=Add(module,host);
   if(host=="StageDWorker"&&mutation!="missing-callback"||mutation=="unexpected-callback")Nested(module,worker,"<>c");
   var generated=Add(module,"UnitySourceGeneratedAssemblyMonoScriptTypes_v1");Nested(module,generated,"MonoScriptData");var details=Add(module,"<PrivateImplementationDetails>");Nested(module,details,"__StaticArrayInitTypeSize=37");Nested(module,details,"__StaticArrayInitTypeSize=63");
   if(mutation=="extra")Add(module,"UnreviewedWorkerType");if(mutation=="author")Add(module,"StageCAuthor");if(mutation=="cilbox")module.Types.Add(new TypeDefinition("Cilbox","Injected",TypeAttributes.Public|TypeAttributes.Class,module.TypeSystem.Object));
   assembly.Write(Path.Combine(output,"Assembly-CSharp.dll"));
  }
 }
}
