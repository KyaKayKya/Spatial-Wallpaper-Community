using System;using System.IO;using System.Linq;using Mono.Cecil;using Newtonsoft.Json.Linq;
class AssemblyInventory {
 static JObject Identity(AssemblyDefinition assembly){var identity=new JObject();foreach(var attribute in assembly.CustomAttributes.Where(a=>a.AttributeType.FullName=="System.Reflection.AssemblyMetadataAttribute")){string key=(string)attribute.ConstructorArguments[0].Value;if(new[]{"sdk","engine","pipeline","vm","backend","rules_sha256","policy_id"}.Contains(key)){if(identity.ContainsKey(key))throw new InvalidDataException("duplicate-assembly-identity:"+key);identity[key]=(string)attribute.ConstructorArguments[1].Value;}}return identity;}
 static int Main(string[] args){
  try{
   if(args.Length>=2&&args[0]=="--identity"){var identities=new JObject();foreach(string path in args.Skip(1)){using(var assembly=AssemblyDefinition.ReadAssembly(path)){identities[Path.GetFullPath(path)]=Identity(assembly);}}Console.WriteLine(identities);return 0;}
   string host=args.Length==1?"StageCWorker":args.Length==2&&(args[1]=="StageDWorker"||args[1]=="SdkWorker")?args[1]:throw new InvalidDataException("unknown-Worker-inventory-profile");
   var fixtureTypes=new[]{"<Module>","StageCVisuals",host,"UnitySourceGeneratedAssemblyMonoScriptTypes_v1","UnitySourceGeneratedAssemblyMonoScriptTypes_v1/MonoScriptData","<PrivateImplementationDetails>","<PrivateImplementationDetails>/__StaticArrayInitTypeSize=37","<PrivateImplementationDetails>/__StaticArrayInitTypeSize=63"};
   // The pinned StageD diagnostic callback emits this exact nested type.
   if(host=="StageDWorker")fixtureTypes=fixtureTypes.Concat(new[]{"StageDWorker/<>c"}).ToArray();
   var result=new JObject();
   foreach(string file in Directory.GetFiles(args[0],"*.dll")){
    using(var assembly=AssemblyDefinition.ReadAssembly(file)){
     var types=assembly.MainModule.GetTypes().Select(t=>t.FullName).ToArray();
     if(types.Any(n=>n=="StageCAuthor"||n=="StageCPeer"||n=="StageCData"||n=="StageCSpawn"||n=="NegativeAuthor"||n.StartsWith("Cilbox.",StringComparison.Ordinal)))throw new InvalidDataException("author-or-Cilbox-type:"+Path.GetFileName(file));
     if(host=="SdkWorker"&&Path.GetFileName(file)=="Assembly-CSharp.dll")throw new InvalidDataException("production-Worker-must-not-contain-Assembly-CSharp");
     if(Path.GetFileName(file)=="Assembly-CSharp.dll"&&!types.OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(fixtureTypes.OrderBy(x=>x,StringComparer.Ordinal)))throw new InvalidDataException("unexpected-Worker-type");
     var identity=Identity(assembly);
     result[Path.GetFileName(file)]=new JObject{{"type_count",types.Length},{"types",Path.GetFileName(file)=="Assembly-CSharp.dll"?new JArray(types):null},{"identity",identity}};
    }
   }
   if(host!="SdkWorker"&&!result.ContainsKey("Assembly-CSharp.dll")||!result.ContainsKey("SpatialWallpaper.Sdk.dll"))throw new InvalidDataException("missing-Worker-core");
   Console.WriteLine(result);return 0;
  }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}
 }
}
