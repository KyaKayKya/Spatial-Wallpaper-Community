using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Security;
class ContractChecks {
 static int Main(string[] args) {
  string output=args.Length>1?args[1]:null;
  try {
   byte[] bytes=File.ReadAllBytes(args[0]);var build=Admission.Admit(bytes,"windows-x86_64");
   if(args.Length>2){
    var observation=JObject.Parse(File.ReadAllText(args[2]));
    var check=typeof(RenderProfiles).GetMethod("RequireObservation");
    if(check==null)throw new Exception("render-profile-observation-unchecked");
    try{check.Invoke(null,new object[]{build.Manifest["render_profile"],observation});}
    catch(System.Reflection.TargetInvocationException error){throw error.InnerException;}
   }
   // Mutating the caller buffer and returned JSON/bytes must not mutate the admitted build.
   Array.Clear(bytes,0,bytes.Length);
   var program=build.Program;program.RemoveAll();
   if(build.Program.Count==0)throw new Exception("mutable-program");
   var original=build.Read("program.json");byte first=original[0];original[0]^=255;
   if(build.Read("program.json")[0]!=first)throw new Exception("mutable-file");
   var contentCopy=build.Content;contentCopy.RemoveAll();if(build.Content.Count==0)throw new Exception("mutable-content");
   var manifestCopy=build.Manifest;manifestCopy.RemoveAll();if(build.Manifest.Count==0)throw new Exception("mutable-manifest");
   var tags=build.CapabilityTags;if(tags.Length>0){string tag=tags[0];tags[0]="forged";if(build.CapabilityTags[0]!=tag)throw new Exception("mutable-tags");}
   var expectedTags=new HashSet<string>();
   foreach(var cls in (JArray)build.Program["classes"])foreach(var method in (JArray)cls["methods"])foreach(var instruction in (JArray)method["body"])if((string)instruction["op"]=="call"){
    string tag=(string)RuleCatalog.Current.Member((string)instruction["value"])["capability"];if(tag.Length>0)expectedTags.Add(tag);
   }
   var content=build.Content;bool shaderView=((JArray)content["resources"]).Any(resource=>(string)resource["kind"]=="material"||(string)resource["kind"]=="shader");
   var graphs=new List<JArray>{(JArray)content["nodes"]};graphs.AddRange(((JArray)content["prefabs"]).Select(prefab=>(JArray)prefab["nodes"]));
   if(shaderView||graphs.Any(graph=>graph.Any(node=>((JArray)node["components"]).Any(component=>(string)component["kind"]=="mesh-renderer"))))expectedTags.Add("view-data");
   if(!expectedTags.OrderBy(x=>x,StringComparer.Ordinal).SequenceEqual(build.CapabilityTags))throw new Exception("wrong-capability-closure");
   var manifest=build.Manifest;var resources=new Dictionary<string,byte[]>();
   foreach(var file in ((JObject)manifest["files"]).Properties())if(file.Name.StartsWith("resources/",StringComparison.Ordinal))resources[file.Name]=build.Read(file.Name);
   var exported=PackageWriter.Export(build.Program,build.Content,resources,(JArray)manifest["licenses"],(JObject)manifest["render_profile"]);
   var roundtrip=Admission.Admit(exported,"windows-x86_64");
   if(roundtrip.Program.ToString()!=build.Program.ToString())throw new Exception("export-program-drift");
   if(!JToken.DeepEquals(roundtrip.Manifest["render_profile"],manifest["render_profile"]))throw new Exception("export-render-profile-drift");
   var result=new JObject{{"accepted",true},{"build_id",build.BuildId},{"rules",build.RuleDigest},{"tags",new JArray(build.CapabilityTags)},{"immutable",true},{"roundtrip",true}};
   if(output!=null)File.WriteAllText(output,result.ToString());
   else Console.WriteLine(result.ToString());
   return 0;
  }catch(Exception e){
   var result=new JObject{{"accepted",false},{"diagnostic",e.GetType().Name+":"+e.Message}};
   if(output!=null)File.WriteAllText(output,result.ToString());
   else Console.Error.WriteLine(result.ToString());
   return 1;
  }
 }
}
