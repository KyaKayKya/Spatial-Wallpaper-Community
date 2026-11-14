using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk;
namespace SpatialWallpaper.Content {
 public sealed class ValidatedPackage {
  readonly JObject scene,manifest;readonly Dictionary<string,byte[]> files;
  public string BuildId{get;}public string TemplateId{get;}
  public JObject Scene=>(JObject)scene.DeepClone();public JObject Manifest=>(JObject)manifest.DeepClone();
  internal ValidatedPackage(JObject manifest,JObject scene,Dictionary<string,byte[]> files){this.manifest=manifest;this.scene=scene;this.files=files;BuildId=(string)manifest["build_id"];TemplateId=(string)manifest["template_id"];}
  public byte[] Read(string name){JsonData.Require(files.ContainsKey(name),"unregistered-resource");return (byte[])files[name].Clone();}
  // Repack only the immutable bytes admitted by Validate, never re-read a
  // mutable staging path after validation when publishing to the local store.
  public void WriteArchive(Stream output){using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true))foreach(var item in files.OrderBy(p=>p.Key,StringComparer.Ordinal))using(var stream=zip.CreateEntry(item.Key).Open())stream.Write(item.Value,0,item.Value.Length);}
 }
 public static class PackageValidator {
  // STEX has a fixed 44-byte header in addition to its bounded pixel payload.
  public const long MaxTotal=1024L*1024*1024;public const int MaxFile=64*1024*1024,MaxTextureFile=128*1024*1024+44;
  static int FileLimit(string name)=>name.EndsWith(".stex",StringComparison.Ordinal)?MaxTextureFile:MaxFile;
  public static void CheckPath(string path){
   JsonData.Require(!string.IsNullOrEmpty(path)&&path.Length<=200&&!path.StartsWith("/",StringComparison.Ordinal),"package-path");
   foreach(char c in path)JsonData.Require(c>32&&c<127&&"\\:%<>\"|?*".IndexOf(c)<0,"package-path-character");
   foreach(string part in path.Split('/')){
    JsonData.Require(part.Length>0&&part!="."&&part!=".."&&!part.EndsWith(".",StringComparison.Ordinal),"package-path-segment");
    string stem=part.Split('.')[0].ToUpperInvariant();
    JsonData.Require(!new[]{"CON","PRN","AUX","NUL","CLOCK$"}.Contains(stem)&&!(stem.Length==4&&(stem.StartsWith("COM",StringComparison.Ordinal)||stem.StartsWith("LPT",StringComparison.Ordinal))&&stem[3]>='0'&&stem[3]<='9'),"package-reserved-path");
   }
  }
  public static string Digest(byte[] bytes){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
  static JToken Sort(JToken token){if(token is JObject o)return new JObject(o.Properties().OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>new JProperty(p.Name,Sort(p.Value))));if(token is JArray a)return new JArray(a.Select(Sort));return token.DeepClone();}
  public static string Identity(JObject manifest){var copy=(JObject)manifest.DeepClone();copy.Remove("build_id");return Digest(new UTF8Encoding(false,true).GetBytes(Sort(copy).ToString(Formatting.None)));}
  public static ValidatedPackage Validate(string path){
   try{
    var info=new FileInfo(path);JsonData.Require(info.Exists&&info.Length>0&&info.Length<=MaxTotal&&(info.Attributes&FileAttributes.ReparsePoint)==0,"package-file");
    for(var parent=info.Directory;parent!=null;parent=parent.Parent)JsonData.Require((parent.Attributes&FileAttributes.ReparsePoint)==0,"package-ancestor-link");
    using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read))using(var zip=new ZipArchive(input,ZipArchiveMode.Read)){
     JsonData.Require(zip.Entries.Count>1&&zip.Entries.Count<=8192,"package-entry-count");
     var files=new Dictionary<string,byte[]>(StringComparer.Ordinal);var aliases=new HashSet<string>(StringComparer.OrdinalIgnoreCase);long total=0;
     foreach(var entry in zip.Entries){
      CheckPath(entry.FullName);JsonData.Require(aliases.Add(entry.FullName),"package-alias");
      uint attrs=unchecked((uint)entry.ExternalAttributes);uint kind=(attrs>>16)&0xF000;
      JsonData.Require((kind==0||kind==0x8000)&&(attrs&0x410)==0,"package-link-or-directory");
      JsonData.Require(entry.Length>0&&entry.Length<=FileLimit(entry.FullName)&&entry.Length<=MaxTotal-total,"package-size:"+entry.FullName+":"+entry.Length);total+=entry.Length;
      using(var source=entry.Open())using(var memory=new MemoryStream()){
       var block=new byte[65536];int n;long read=0;while((n=source.Read(block,0,block.Length))>0){read+=n;JsonData.Require(read<=entry.Length,"package-expansion");memory.Write(block,0,n);}
       JsonData.Require(read==entry.Length,"package-short-file");files.Add(entry.FullName,memory.ToArray());
      }
     }
     JsonData.Require(files.ContainsKey("manifest.json"),"missing-manifest");
     var utf8=new UTF8Encoding(false,true);var manifest=JsonData.Parse(utf8.GetString(files["manifest.json"]));
     JsonData.Fields(manifest,"schema","template_id","name","build_id","engine","pipeline","sdk","platform","color_space","capabilities","entry","files","license","editable","reshare");
     JsonData.Require(JsonData.Integer(manifest["schema"],2,2)==2&&JsonData.Text(manifest["engine"])=="2022.3.62f3"&&JsonData.Text(manifest["pipeline"])=="14.0.12"&&JsonData.Text(manifest["sdk"])=="0.2.0"&&JsonData.Text(manifest["platform"])=="windows-x86_64"&&JsonData.Text(manifest["color_space"])=="linear","unsupported-build");
     Guid template;JsonData.Require(Guid.TryParseExact(JsonData.Text(manifest["template_id"]),"D",out template),"template-id");JsonData.Text(manifest["name"],128);
     JsonData.Require(JsonData.Text(manifest["build_id"])==Identity(manifest),"build-identity");
     var caps=JsonData.Array(manifest["capabilities"],8);var capNames=caps.Select(c=>JsonData.Text(c)).ToArray();bool controlled=capNames.SequenceEqual(new[]{"static-data-v1","shader-only-2022.3.62f3-v1","controlled-scene-v1"});JsonData.Require(controlled||capNames.SequenceEqual(new[]{"static-data-v1","shader-only-2022.3.62f3-v1"}),"unsupported-capability");
     JsonData.Require(manifest["editable"]?.Type==JTokenType.Boolean&&manifest["reshare"]?.Type==JTokenType.Boolean,"permissions");
     var license=manifest["license"] as JObject;JsonData.Fields(license,"mode","records");string mode=JsonData.Text(license["mode"]);JsonData.Require(mode=="local-validation"||mode=="share","license-mode");JsonData.Require(JsonData.Array(license["records"],4096).Count>0,"license-records");
     foreach(var item in (JArray)license["records"]){var record=item as JObject;JsonData.Fields(record,"name","source","license","redistribution","attribution");foreach(var p in record.Properties())JsonData.Text(p.Value,8192);if(mode=="share")JsonData.Require((string)record["redistribution"]=="verified","unverified-sharing-rights");}
     var list=manifest["files"] as JObject;JsonData.Require(list!=null&&list.Count==files.Count-1,"unlisted-or-missing-file");
     foreach(var p in list.Properties()){
      CheckPath(p.Name);JsonData.Require(p.Name!="manifest.json"&&files.ContainsKey(p.Name),"missing-dependency");var record=p.Value as JObject;JsonData.Fields(record,"size","sha256");
      JsonData.Require(JsonData.Integer(record["size"],1,FileLimit(p.Name))==files[p.Name].Length&&JsonData.Text(record["sha256"])==Digest(files[p.Name]),"resource-digest");
      JsonData.Require(p.Name.EndsWith(".json",StringComparison.Ordinal)||p.Name.EndsWith(".smesh",StringComparison.Ordinal)||p.Name.EndsWith(".stex",StringComparison.Ordinal)||p.Name.EndsWith(".shaderbundle",StringComparison.Ordinal),"resource-type");
      if(p.Name.EndsWith(".shaderbundle",StringComparison.Ordinal)){JsonData.Require(p.Name=="custom.shaderbundle","shader-resource-alias");ShaderBundleGate.Validate(files[p.Name]);}
      else if(p.Name.EndsWith(".smesh",StringComparison.Ordinal)){JsonData.Require(p.Name=="meshes/"+Digest(files[p.Name])+".smesh","mesh-resource-alias");ResourceData.ValidateMesh(files[p.Name]);}
      else if(p.Name.EndsWith(".stex",StringComparison.Ordinal)){JsonData.Require(p.Name=="textures/"+Digest(files[p.Name])+".stex","texture-resource-alias");ResourceData.ValidateTexture(files[p.Name]);}
      else JsonData.Require(p.Name=="scene.json","extra-data-file");
     }
     string entryName=JsonData.Text(manifest["entry"]);JsonData.Require(entryName=="scene.json"&&files.ContainsKey(entryName),"scene-entry");
     var scene=SceneData.Validate(JsonData.Parse(utf8.GetString(files[entryName])));
     JsonData.Require(controlled==scene.ContainsKey("effects"),"controlled-capability-mismatch");
     foreach(string group in new[]{"meshes","textures"})foreach(var item in (JArray)scene[group]){string name=JsonData.Text(item);JsonData.Require(files.ContainsKey(name)&&name.EndsWith(group=="meshes"?".smesh":".stex",StringComparison.Ordinal),"scene-resource-reference");}
     if(controlled)foreach(JObject effect in (JArray)scene["effects"])if((string)effect["kind"]=="instances"){
      int submeshes=BitConverter.ToInt32(files[(string)scene["meshes"][(int)effect["mesh"]]],8);var indices=(JArray)effect["materials"];
      JsonData.Require(indices.Count==submeshes,"instance-material-count");foreach(int index in indices)JsonData.Require((bool)scene["materials"][index]["instancing"],"instance-material-disabled");
     }
     foreach(JArray lightmap in (JArray)scene["lightmaps"])foreach(var item in lightmap){int index=(int)item;if(index>=0)JsonData.Require(BitConverter.ToInt32(files[(string)scene["textures"][index]],28)==1,"cubemap-cannot-be-lightmap");}
     foreach(JObject material in (JArray)scene["materials"]){string bundle=(string)material["bundle"];JsonData.Require(bundle==""||files.ContainsKey(bundle)&&bundle.EndsWith(".shaderbundle",StringComparison.Ordinal),"shader-reference");if(bundle=="")JsonData.Require(new[]{"Universal Render Pipeline/Lit","Universal Render Pipeline/Unlit"}.Contains((string)material["shader"]),"unknown-platform-shader");}
     return new ValidatedPackage(manifest,scene,files);
    }
   }catch(InvalidDataException){throw;}catch(Exception e)when(e is IOException||e is JsonException||e is ArgumentException||e is OverflowException||e is DecoderFallbackException){throw new InvalidDataException("invalid-package",e);}
  }
 }
}
