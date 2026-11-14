using System;
using System.IO;
using System.IO.Compression;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Content;
namespace SpatialWallpaper.Sdk.Security {
 public sealed class AdmittedBuild {
  readonly JObject manifest,program,content;readonly Dictionary<string,byte[]> files;
  public string BuildId{get;} public RuleCatalog Rules{get;}public string RuleDigest=>Rules.Digest;
  public bool IsNativeScript { get; }
  readonly string[] tags;readonly Dictionary<string,int> stackLimits;readonly Dictionary<string,long> shaderDecoded,materialParsing;
  public long SnapshotManagedBytes{get;}public long ProgramCloningBytes{get;}public long ContentCloningBytes{get;}
  public int MethodStack(string id)=>stackLimits[id];
  public int FileSize(string path){JsonData.Require(files.ContainsKey(path),"unregistered-file");return files[path].Length;}
  public long ShaderDecodedBytes(string path){JsonData.Require(shaderDecoded.ContainsKey(path),"unregistered-shader");return shaderDecoded[path];}
  internal static long TreeCost(JObject tree){
   long cost=256;foreach(var token in tree.DescendantsAndSelf()){cost=checked(cost+160);if(token is JProperty property)cost=checked(cost+32+property.Name.Length*2L);if(token is JValue value&&value.Type==JTokenType.String)cost=checked(cost+32+((string)value).Length*2L);}
   return cost;
  }
  public string[] CapabilityTags=>(string[])tags.Clone();
  public JObject Manifest=>(JObject)manifest.DeepClone();
  public JObject Program=>(JObject)program.DeepClone();
  public JObject Content=>(JObject)content.DeepClone();
  public byte[] Read(string path){JsonData.Require(files.ContainsKey(path),"unregistered-file");return (byte[])files[path].Clone();}
  // Only the trusted SDK implementation can borrow this immutable snapshot.
  // Public callers and author bindings never receive its backing arrays.
  internal byte[] BorrowFile(string path){JsonData.Require(files.ContainsKey(path),"unregistered-file");return files[path];}
  internal long MaterialParsingBytes(string path)=>materialParsing[path];
  internal AdmittedBuild(JObject m,JObject p,JObject c,Dictionary<string,byte[]> f,string[] caps,Dictionary<string,int> stacks,Dictionary<string,long> decoded,Dictionary<string,long> parsing,bool nativeScript=false,RuleCatalog rules=null){
   IsNativeScript=nativeScript;
   Rules=rules??RuleCatalog.Current;
   manifest=m;program=p;content=c;files=f;tags=caps;BuildId=(string)m["build_id"];stackLimits=new Dictionary<string,int>(stacks,StringComparer.Ordinal);
   shaderDecoded=new Dictionary<string,long>(decoded,StringComparer.Ordinal);materialParsing=new Dictionary<string,long>(parsing,StringComparer.Ordinal);ProgramCloningBytes=TreeCost(p);ContentCloningBytes=TreeCost(c);
   SnapshotManagedBytes=checked(TreeCost(m)+ProgramCloningBytes+ContentCloningBytes+f.Sum(pair=>(long)pair.Value.Length+pair.Key.Length*2L+192)+stacks.Count*512L+shaderDecoded.Count*512L+materialParsing.Count*512L+256);
  }
 }
 public static class Admission {
  // Parser bounds for candidate rules, not the D-stage measured execution policy.
  public static readonly int MaxArchive=RuleCatalog.Current.Limit("archive"),MaxExpanded=RuleCatalog.Current.Limit("expanded"),MaxEntries=RuleCatalog.Current.Limit("entries");
  static readonly UTF8Encoding Utf8=new UTF8Encoding(false,true);
  static void Need(bool yes,string code)=>JsonData.Require(yes,code);
  // Program/field metadata only. This does not admit native assets or a wallpaper
  // archive; callers must validate their native envelope before entering here.
  public static AdmittedBuild AdmitProgramMetadata(JObject identity,JObject sourceProgram,JObject sourceContent,RuleCatalog rules=null){
   if(identity==null||sourceProgram==null||sourceContent==null)throw new ArgumentNullException();
   var manifest=JsonData.Parse(identity.ToString(Newtonsoft.Json.Formatting.None));
   Need(manifest["build_id"]?.Type==JTokenType.String&&((string)manifest["build_id"]).Length==64,"native-program-identity");
   rules=rules??RuleCatalog.Current;var program=JsonData.Parse(sourceProgram.ToString(Newtonsoft.Json.Formatting.None));var contract=new ProgramContract(program,rules);
   var content=JsonData.Parse(sourceContent.ToString(Newtonsoft.Json.Formatting.None));JsonData.Fields(content,"frame","resources","nodes","prefabs");JsonData.Vector(content["frame"],3,.001,10000);
   var types=new Dictionary<string,string>(StringComparer.Ordinal);
   foreach(JObject resource in JsonData.Array(content["resources"],RuleCatalog.Current.Limit("resources"))){
    JsonData.Fields(resource,"id","kind");string id=ProgramContract.Name(resource["id"]),kind=JsonData.Text(resource["kind"]);
    Need(new[]{"mesh","texture","cubemap","material","sound"}.Contains(kind)&&!types.ContainsKey(id),"native-program-resource-metadata");types.Add(id,kind);
   }
   var prefabs=JsonData.Array(content["prefabs"],RuleCatalog.Current.Limit("prefabs"));var names=new HashSet<string>(StringComparer.Ordinal);
   foreach(JObject prefab in prefabs){JsonData.Fields(prefab,"id","nodes");Need(names.Add(ProgramContract.Name(prefab["id"])),"duplicate-prefab");}
   var graphs=new List<JArray>{JsonData.Array(content["nodes"],RuleCatalog.Current.Limit("nodes"))};graphs.AddRange(prefabs.Select(p=>JsonData.Array(p["nodes"],RuleCatalog.Current.Limit("prefab_nodes"))));
   foreach(var graph in graphs){
    foreach(var node in graph)foreach(var component in JsonData.Array(node["components"],RuleCatalog.Current.Limit("components")))Need(new[]{"transform","light","script"}.Contains((string)component["kind"]),"native-program-no-render-resource-config");
    bool prefab=!ReferenceEquals(graph,graphs[0]);
    if(prefab)Need(graph.Count>0&&graph.Count(n=>n["parent"]?.Type==JTokenType.Integer&&(long)n["parent"]==-1)==1,"prefab-root");
    ValidateGraph(graph,contract,types,names,prefab);
   }
   return new AdmittedBuild(manifest,program,content,new Dictionary<string,byte[]>(),contract.Capabilities,contract.StackLimits,new Dictionary<string,long>(),new Dictionary<string,long>(),true,rules);
  }
  public static AdmittedBuild Admit(byte[] input,string platform){
   Need(input!=null&&input.Length>0&&input.Length<=MaxArchive,"archive-size");
   byte[] snapshot=(byte[])input.Clone();
   try{
    var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);var aliases=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    using(var memory=new MemoryStream(snapshot,false))using(var zip=new ZipArchive(memory,ZipArchiveMode.Read)){
     Need(zip.Entries.Count>=3&&zip.Entries.Count<=MaxEntries,"entry-count");long total=0;
     foreach(var entry in zip.Entries){
      PackageValidator.CheckPath(entry.FullName);Need(aliases.Add(entry.FullName),"path-alias");
      uint attr=unchecked((uint)entry.ExternalAttributes),kind=(attr>>16)&0xf000;
      Need((kind==0||kind==0x8000)&&(attr&0x410)==0,"entry-kind");
      Need(entry.Length>0&&entry.Length<=RuleCatalog.Current.Limit("file_bytes")&&entry.Length<=MaxExpanded-total,"entry-size");total+=entry.Length;
      // Reserve bounded output once, never unbounded CopyTo/decompression into a growing stream.
      byte[] bytes=new byte[(int)entry.Length];
      using(var stream=entry.Open()){int offset=0,n;while(offset<bytes.Length&&(n=stream.Read(bytes,offset,bytes.Length-offset))>0)offset+=n;Need(offset==bytes.Length&&stream.ReadByte()==-1,"entry-expanded-size");}
      files.Add(entry.FullName,bytes);
     }
    }
    Need(files.ContainsKey("manifest.json")&&files.ContainsKey("program.json")&&files.ContainsKey("content.json"),"package-entries");
    var manifest=JsonData.Parse(Utf8.GetString(files["manifest.json"]));
    Need(manifest["schema"]?.Type==JTokenType.Integer&&(long)manifest["schema"]==4,"manifest-schema");
    Need(manifest.ContainsKey("render_profile"),"render-profile-required");
    JsonData.Fields(manifest,"schema","sdk","vm","rules","rules_sha256","engine","pipeline","backend","platform","build_id","files","licenses","render_profile");
    RenderProfiles.Require(manifest["render_profile"]);
    var catalog=RuleCatalog.Current.Snapshot;
    JsonData.Integer(manifest["schema"],4,4);
    foreach(string key in new[]{"sdk","vm","engine","pipeline","backend"})Need(JsonData.Text(manifest[key])==(string)catalog[key],"incompatible-"+key);
    Need(platform=="windows-x86_64"&&JsonData.Text(manifest["platform"])==platform,"platform-not-verified");
    Need(JsonData.Text(manifest["rules"])==RuleCatalog.Current.Id&&JsonData.Text(manifest["rules_sha256"])==RuleCatalog.Current.Digest,"incompatible-rules");
    Need(JsonData.Text(manifest["build_id"])==PackageValidator.Identity(manifest),"build-identity");
    var records=manifest["files"] as JObject;Need(records!=null&&records.Count==files.Count-1,"file-manifest");
    foreach(var property in records.Properties()){
     Need(property.Name!="manifest.json"&&files.ContainsKey(property.Name),"missing-file");
     var record=property.Value as JObject;JsonData.Fields(record,"size","sha256");
     Need(JsonData.Integer(record["size"],1,RuleCatalog.Current.Limit("file_bytes"))==files[property.Name].Length&&JsonData.Text(record["sha256"])==PackageValidator.Digest(files[property.Name]),"file-digest");
     Need(property.Name=="program.json"||property.Name=="content.json"||property.Name.StartsWith("resources/",StringComparison.Ordinal),"unexpected-payload");
    }
    var licenses=JsonData.Array(manifest["licenses"],4096);Need(licenses.Count>0,"licenses-required");
    foreach(JObject license in licenses){JsonData.Fields(license,"name","source","license","attribution");foreach(var field in license.Properties())Need(JsonData.Text(field.Value,8192).Length>0,"license-field");}
    var program=JsonData.Parse(Utf8.GetString(files["program.json"]));var contract=new ProgramContract(program);
    var content=JsonData.Parse(Utf8.GetString(files["content.json"]));
    Dictionary<string,long> decoded,parsing;ValidateContent(content,contract,files,out decoded,out parsing);
    var capabilities=new HashSet<string>(contract.Capabilities,StringComparer.Ordinal);
    foreach(var resource in (JArray)content["resources"])foreach(string tag in RuleCatalog.Current.ContentCapabilities("resources",(string)resource["kind"]))capabilities.Add(tag);
    var graphs=new List<JArray>{(JArray)content["nodes"]};graphs.AddRange(((JArray)content["prefabs"]).Select(prefab=>(JArray)prefab["nodes"]));
    foreach(var graph in graphs)foreach(var node in graph)foreach(var component in (JArray)node["components"])foreach(string tag in RuleCatalog.Current.ContentCapabilities("components",(string)component["kind"]))capabilities.Add(tag);
    return new AdmittedBuild(manifest,program,content,files,capabilities.OrderBy(tag=>tag,StringComparer.Ordinal).ToArray(),contract.StackLimits,decoded,parsing);
   }catch(InvalidDataException){throw;}
   catch(Exception error)when(error is IOException||error is ArgumentException||error is OverflowException||error is InvalidCastException||error is NullReferenceException||error is DecoderFallbackException){
    throw new InvalidDataException("invalid-contract",error);
   }
  }
  static void ValidateContent(JObject content,ProgramContract program,Dictionary<string,byte[]> files,out Dictionary<string,long> shaderDecoded,out Dictionary<string,long> materialParsing){
   materialParsing=new Dictionary<string,long>(StringComparer.Ordinal);
   shaderDecoded=new Dictionary<string,long>(StringComparer.Ordinal);var shaderCache=new Dictionary<string,ShaderBundleGate.ShaderDescription[]>(StringComparer.Ordinal);
   JsonData.Fields(content,"frame","resources","nodes","prefabs");JsonData.Vector(content["frame"],3,.001,10000);
   var resources=JsonData.Array(content["resources"],RuleCatalog.Current.Limit("resources"));
   var shaders=new Dictionary<string,ShaderBundleGate.ShaderDescription[]>(StringComparer.Ordinal);
   var materials=new List<JObject>();var resourceTypes=new Dictionary<string,string>(StringComparer.Ordinal);
   foreach(JObject resource in resources){
    JsonData.Fields(resource,"id","kind","file");string id=ProgramContract.Name(resource["id"]),kind=JsonData.Text(resource["kind"]),file=JsonData.Text(resource["file"]);
    Need(!resourceTypes.ContainsKey(id),"duplicate-resource");resourceTypes.Add(id,kind);
    Need(files.ContainsKey(file)&&file.StartsWith("resources/",StringComparison.Ordinal),"resource-file");
    Need(file=="resources/"+PackageValidator.Digest(files[file])+(kind=="mesh"?".smesh":(kind=="texture"||kind=="cubemap")?".stex":kind=="shader"?".shaderbundle":kind=="sound"?".saudio":".json"),"resource-path");
    if(kind=="mesh")ResourceData.ValidateMesh(files[file]);
    else if(kind=="sound")new SoundClipData(files[file]);
    else if(kind=="texture"||kind=="cubemap"){ResourceData.ValidateTexture(files[file]);Need(BitConverter.ToInt32(files[file],28)==(kind=="texture"?1:6),"texture-subtype");}
    else if(kind=="shader"){
     if(!shaderCache.TryGetValue(file,out var inspected)){long expanded;inspected=ShaderBundleGate.Inspect(files[file],out expanded);shaderCache.Add(file,inspected);shaderDecoded.Add(file,expanded);}shaders.Add(id,inspected);
    }
    else if(kind=="material")materials.Add(resource);
    else throw new InvalidDataException("resource-kind");
   }
   foreach(var resource in materials){
     string file=(string)resource["file"];var material=JsonData.Parse(Utf8.GetString(files[file]));JsonData.Fields(material,"shader","bundle","properties");
     materialParsing[file]=checked(4096+AdmittedBuild.TreeCost(material)*2+files[file].Length*4L);
     string shader=JsonData.Text(material["shader"]),bundle=JsonData.Text(material["bundle"]);
     var description=bundle.Length==0?RuleCatalog.Current.BuiltinShader(shader):(shaders.TryGetValue(bundle,out var descriptions)?descriptions.SingleOrDefault(s=>s.Name==shader):null);
     Need(bundle.Length==0?shader=="Universal Render Pipeline/Lit"||shader=="Universal Render Pipeline/Unlit":description!=null,"material-shader");

     var props=material["properties"] as JObject;Need(props!=null&&props.Count<=64,"material-properties");
     foreach(var property in props.Properties()){
      Need(property.Name.Length>0&&property.Name.Length<=128,"property-name");
      var value=property.Value as JObject;JsonData.Fields(value,"type","value");string type=JsonData.Text(value["type"]);
      if(description!=null){Need(description.Properties.TryGetValue(property.Name,out var shaderType),"material-property-missing");
       Need(type=="f32"&&(shaderType==2||shaderType==3)||type=="color"&&shaderType==0||type=="texture"&&shaderType==4,"material-property-type");
      }
      if(type=="f32")JsonData.Number(value["value"]);
      else if(type=="color")JsonData.Vector(value["value"],4,-16,16);
      else if(type=="texture"){
       string textureId=JsonData.Text(value["value"]);
       Need(resourceTypes.TryGetValue(textureId,out var textureType)&&(textureType=="texture"||textureType=="cubemap"),"material-texture");
       if(description!=null)Need(description.TextureDimensions[property.Name]==(textureType=="texture"?2:4),"material-texture-dimension");
      }
      else throw new InvalidDataException("material-property-type");
     }
   }
   var used=new HashSet<string>(resources.Cast<JObject>().Select(r=>(string)r["file"]),StringComparer.Ordinal);
   Need(files.Keys.Where(f=>f.StartsWith("resources/",StringComparison.Ordinal)).All(used.Contains),"unreferenced-resource");
   var prefabs=JsonData.Array(content["prefabs"],RuleCatalog.Current.Limit("prefabs"));var prefabIds=new HashSet<string>(StringComparer.Ordinal);
   foreach(JObject prefab in prefabs){JsonData.Fields(prefab,"id","nodes");Need(prefabIds.Add(ProgramContract.Name(prefab["id"])),"duplicate-prefab");}
   ValidateGraph(JsonData.Array(content["nodes"],RuleCatalog.Current.Limit("nodes")),program,resourceTypes,prefabIds);
   foreach(JObject prefab in prefabs){
    var graph=JsonData.Array(prefab["nodes"],RuleCatalog.Current.Limit("prefab_nodes"));
    Need(graph.Count>0&&graph.OfType<JObject>().Count(n=>n["parent"]?.Type==JTokenType.Integer&&(long)n["parent"]==-1)==1,"prefab-root");
    ValidateGraph(graph,program,resourceTypes,prefabIds,true);
   }
  }
  static void ValidateGraph(JArray nodes,ProgramContract program,Dictionary<string,string> resources,HashSet<string> prefabs,bool prefab=false){
   var kinds=new List<HashSet<string>>();
   for(int i=0;i<nodes.Count;i++){
    var node=nodes[i] as JObject;JsonData.Fields(node,"id","parent","components");
    Need(ProgramContract.Name(node["id"])== "n"+i,"canonical-node-id");JsonData.Integer(node["parent"],-1,i-1);
    var set=new HashSet<string>(StringComparer.Ordinal);
    foreach(JObject component in JsonData.Array(node["components"],RuleCatalog.Current.Limit("components"))){
     JsonData.Fields(component,"kind","config","fields","callbacks");string kind=JsonData.Text(component["kind"]);
     Need(set.Add(kind),"duplicate-component");Need(((JObject)RuleCatalog.Current.Snapshot["components"]).ContainsKey(kind),"component-not-allowed");
     if(kind=="presentation")Need(!prefab&&i==0&&(int)node["parent"]==-1,"presentation-only-on-scene-root");
    }
    Need(set.Contains("transform"),"transform-required");kinds.Add(set);
   }
   Action<string,JToken> reference=(type,value)=>{
    if(type=="node"||type=="light"){int index=JsonData.Integer(value,0,nodes.Count-1);Need(type=="node"||kinds[index].Contains("light"),"component-reference");return;}
    if(type=="prefab"){Need(prefabs.Contains(JsonData.Text(value)),"prefab-reference");return;}
    Need(resources.TryGetValue(JsonData.Text(value),out var actual)&&actual==type,"resource-reference");
   };
   Func<int,string> scriptType=index=>{
    var script=((JArray)nodes[index]["components"]).Cast<JObject>().SingleOrDefault(c=>(string)c["kind"]=="script");return script==null?null:(string)script["config"]["class"];
   };
   Action<string,JToken> validate=(type,value)=>program.Value(type,value,reference,scriptType,nodes.Count);
   foreach(JObject node in nodes)foreach(JObject component in (JArray)node["components"]){
    string kind=(string)component["kind"];var config=component["config"] as JObject;
    var schema=(JObject)RuleCatalog.Current.Snapshot["components"][kind];
    JsonData.Fields(config,schema.Properties().Select(p=>p.Name).ToArray());
    foreach(var field in schema.Properties())ValueContract.Check(config[field.Name],(JObject)field.Value,reference);
    if(kind=="light")Need((double)config["innerSpot"]<=(double)config["spot"],"light-inner-spot-exceeds-spot");
    if(kind=="presentation")Need(!(bool)config["ambient"]||!(bool)config["gradient"],"presentation-ambient-conflict");
    if(kind=="script")program.Binding((string)config["class"],component["fields"] as JObject,component["callbacks"] as JArray,validate);
    else{JsonData.Fields(component["fields"] as JObject);Need(JsonData.Array(component["callbacks"],0).Count==0,"callbacks-only-on-script");}
   }
  }
 }
}
