using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Content;
using SpatialWallpaper.Sdk.Security;
namespace SpatialWallpaper.Sdk.Native {
 public sealed class NativePackage {
  readonly JObject manifest;readonly byte[] script;
  public string BuildId=>(string)manifest["build_id"];
  public string WorkId=>(string)manifest["work_id"];
  public string TemplateId=>WorkId;
  public string Revision=>(string)manifest["revision"];
  public string SceneId=>(string)manifest["scene"]["id"];
  public string SceneSourceDigest=>(string)manifest["scene"]["source_sha256"];
  public string LibraryDigest=>(string)manifest["scene"]["library_digest"];
  public JObject Manifest=>(JObject)manifest.DeepClone();
  public byte[] ScriptDefinition=>(byte[])script.Clone();
  NativePackage(JObject value,byte[] program){manifest=value;script=program;}
  static void Need(bool yes,string message){if(!yes)throw new InvalidDataException("native-package:"+message);}
  static string Text(JToken value,int max=256){Need(value?.Type==JTokenType.String,"text");string s=(string)value;Need(s.Length>0&&s.Length<=max,"text-length");return s;}
  static string Digest(JToken value){string s=Text(value,64);Need(s.Length==64&&s.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f'),"sha256");return s;}
  public void RequireTrustedScene(string expectedLibraryDigest,string expectedSourceDigest){Need(LibraryDigest==expectedLibraryDigest&&SceneSourceDigest==expectedSourceDigest,"untrusted-scene-identity");}
  public void WriteArchive(Stream output){using(var zip=new ZipArchive(output,ZipArchiveMode.Create,true)){foreach(var pair in new[]{new KeyValuePair<string,byte[]>("manifest.json",Encoding.UTF8.GetBytes(manifest.ToString(Newtonsoft.Json.Formatting.None))),new KeyValuePair<string,byte[]>("script.json",script)})using(var entry=zip.CreateEntry(pair.Key).Open())entry.Write(pair.Value,0,pair.Value.Length);}}
  public static NativePackage Validate(string path){
   var file=new FileInfo(path);Need(file.Exists&&file.Length>0&&file.Length<=SceneCommandCatalog.MaxScriptBytes+1048576&&(file.Attributes&FileAttributes.ReparsePoint)==0,"file");for(var p=file.Directory;p!=null;p=p.Parent)Need((p.Attributes&FileAttributes.ReparsePoint)==0,"ancestor-link");
   var data=new Dictionary<string,byte[]>(StringComparer.Ordinal);
   using(var input=file.OpenRead())using(var zip=new ZipArchive(input,ZipArchiveMode.Read)){
    Need(zip.Entries.Count==2,"entry-count");foreach(var item in zip.Entries){Need((item.FullName=="manifest.json"||item.FullName=="script.json")&&!data.ContainsKey(item.FullName),"entry-path");uint attr=unchecked((uint)item.ExternalAttributes);Need(((attr>>16)&0xf000)==0||((attr>>16)&0xf000)==0x8000,"entry-type");Need((attr&0x410)==0,"entry-link");Need(item.Length>0&&item.Length<=(item.FullName=="manifest.json"?1048576:SceneCommandCatalog.MaxScriptBytes),"entry-size");using(var stream=item.Open())using(var bytes=new MemoryStream()){var buffer=new byte[8192];int count;while((count=stream.Read(buffer,0,buffer.Length))>0){Need(bytes.Length+count<=item.Length,"entry-expansion");bytes.Write(buffer,0,count);}Need(bytes.Length==item.Length,"entry-truncated");data.Add(item.FullName,bytes.ToArray());}}
   }
   var utf8=new UTF8Encoding(false,true);var m=JsonData.Parse(utf8.GetString(data["manifest.json"]));JsonData.Fields(m,"format","schema","sdk","command_abi","catalog_digest","work_id","revision","name","scene","node_paths","resources","license","files","build_id");
   Need(Text(m["format"])=="spatial-native-wallpaper"&&(int?)m["schema"]==5&&Text(m["sdk"])==SceneCommandCatalog.TargetSdk&&(int?)m["command_abi"]==SceneCommandCatalog.Abi&&Digest(m["catalog_digest"])==SceneCommandCatalog.Digest,"identity");Guid id;Need(Guid.TryParseExact(Text(m["work_id"]),"D",out id)&&id!=Guid.Empty&&id.ToString("D")==Text(m["work_id"]),"work-id");Text(m["revision"],128);Text(m["name"],128);Need(Digest(m["build_id"])==PackageValidator.Identity(m),"build-digest");
   var scene=m["scene"] as JObject;JsonData.Fields(scene,"id","source_sha256","library_digest");string sceneId=Text(scene["id"],64);Need(sceneId.All(c=>c>='a'&&c<='z'||c>='0'&&c<='9'||c=='-'||c=='_'),"scene-id");Digest(scene["source_sha256"]);Digest(scene["library_digest"]);
   var paths=m["node_paths"] as JArray;Need(paths!=null&&paths.Count>0&&paths.Count<=SceneCommandCatalog.MaxObjects,"node-paths");foreach(var item in paths){Need(item.Type==JTokenType.String,"node-path");string p=(string)item;Need(p.Length<=512&&!p.Contains('\\')&&!p.Split('/').Any(s=>s==".."||s=="."||p.Length>0&&s.Length==0),"node-path");}
   var resources=m["resources"] as JArray;Need(resources!=null&&resources.Count<=SceneCommandCatalog.MaxObjects,"resources");var names=new HashSet<string>(StringComparer.Ordinal);foreach(JObject r in resources){JsonData.Fields(r,"name","asset_key","kind","source_sha256");Need(names.Add(Text(r["name"],128)),"resource-alias");Text(r["asset_key"],128);Need(new[]{"mesh","material","texture","cubemap","sound","prefab"}.Contains(Text(r["kind"])),"resource-kind");Digest(r["source_sha256"]);}
   var license=m["license"] as JObject;JsonData.Fields(license,"mode","records");Need(new[]{"local-validation","share"}.Contains(Text(license["mode"])),"license-mode");var records=license["records"] as JArray;Need(records!=null&&records.Count>0&&records.Count<=4096,"license-records");foreach(JObject r in records){JsonData.Fields(r,"name","source","license","redistribution","attribution");foreach(var p in r.Properties())Text(p.Value,8192);if((string)license["mode"]=="share")Need((string)r["redistribution"]=="verified","unverified-redistribution");}
   var files=m["files"] as JObject;Need(files!=null&&files.Count==1&&files["script.json"] is JObject,"files");var record=(JObject)files["script.json"];JsonData.Fields(record,"size","sha256");Need((long?)record["size"]==data["script.json"].Length&&Digest(record["sha256"])==PackageValidator.Digest(data["script.json"]),"script-digest");var admitted=NativeScriptDefinition.Admit(data["script.json"]);Need(((JArray)admitted.Content["nodes"]).Count==paths.Count,"node-binding-count");foreach(JObject r in (JArray)admitted.Content["resources"])Need(resources.Any(x=>(string)x["name"]==(string)r["id"]&&(string)x["kind"]==(string)r["kind"]),"script-resource-binding");return new NativePackage(m,data["script.json"]);
  }
 }
}
