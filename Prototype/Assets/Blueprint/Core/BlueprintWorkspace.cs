using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Blueprint {
 public sealed class BlueprintWorkRevision {
  public string WorkId,RevisionId,Name,SourceHash;public BlueprintSettings Settings;
 }
 // Built-in template data uses immutable revisions like ContentStore. Source
 // blueprints are local user data, separate from redistributable SDK packages.
 public sealed class BlueprintWorkspace {
  readonly string root,index;
  public BlueprintWorkspace(string directory){root=Path.GetFullPath(directory);index=Path.Combine(root,"index.json");Directory.CreateDirectory(Path.Combine(root,"sources"));}
  JObject Read(){if(!File.Exists(index))return new JObject{["schema"]=1,["works"]=new JObject(),["revisions"]=new JObject()};if(new FileInfo(index).Length>16*1024*1024)throw new BlueprintImportException("workspace-index-budget");try{var value=JObject.Parse(File.ReadAllText(index));if((int?)value["schema"]!=1||!(value["works"] is JObject)||!(value["revisions"] is JObject))throw new BlueprintImportException("workspace-index-invalid");return value;}catch(JsonException){throw new BlueprintImportException("workspace-index-invalid");}}
  void Write(JObject state){string temp=index+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var file=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(file)){writer.Write(state.ToString(Formatting.None));writer.Flush();file.Flush(true);}if(File.Exists(index))File.Replace(temp,index,null);else File.Move(temp,index);}finally{if(File.Exists(temp))File.Delete(temp);}}
  static string Id(string value){if(!Guid.TryParseExact(value,"N",out var id)||id.ToString("N")!=value)throw new BlueprintImportException("workspace-id-invalid");return value;}
  public string SourcePath(string hash){if(hash==null||hash.Length!=64||hash.Any(c=>!(c>='0'&&c<='9'||c>='a'&&c<='f')))throw new BlueprintImportException("workspace-source-invalid");return Path.Combine(root,"sources",hash+".blueprint");}
  public BlueprintWorkRevision[] List(){var state=Read();return ((JObject)state["works"]).Properties().Select(p=>From(state,p.Name,(string)p.Value["latest"])).ToArray();}
  static BlueprintWorkRevision From(JObject state,string work,string revision){Id(work);Id(revision);var value=state["revisions"][revision] as JObject;var record=state["works"][work] as JObject;if(value==null||record==null||(string)value["work"]!=work)throw new BlueprintImportException("workspace-revision-missing");return new BlueprintWorkRevision{WorkId=work,RevisionId=revision,Name=(string)record["name"],SourceHash=(string)value["source"],Settings=BlueprintSettings.ReadJson(value["settings"].ToString(Formatting.None))};}
  public BlueprintWorkRevision Revision(string work,string revision)=>From(Read(),work,revision);
  public BlueprintWorkRevision Create(string name,string hash,byte[] source,BlueprintSettings settings){string path=SourcePath(hash);if(source==null||source.Length==0||source.Length>NbtInput.MaxInputBytes)throw new BlueprintImportException("workspace-source-budget");if(!File.Exists(path)){using(var stream=new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.None)){stream.Write(source,0,source.Length);stream.Flush(true);}}return Add(Read(),null,name,hash,settings);}
  public BlueprintWorkRevision SaveAs(BlueprintWorkRevision source,string name,BlueprintSettings settings){if(!File.Exists(SourcePath(source.SourceHash)))throw new BlueprintImportException("workspace-source-missing");return Add(Read(),null,name,source.SourceHash,settings);}
  public BlueprintWorkRevision Save(string work,string expected,BlueprintSettings settings,string name=null){Id(work);Id(expected);var state=Read();var previous=From(state,work,expected);if((string)state["works"][work]["latest"]!=expected)throw new BlueprintImportException("workspace-revision-conflict");return Add(state,work,name??previous.Name,previous.SourceHash,settings);}
  BlueprintWorkRevision Add(JObject state,string work,string name,string hash,BlueprintSettings settings){if(string.IsNullOrWhiteSpace(name)||name.Length>128)throw new BlueprintImportException("workspace-name-invalid");settings.Validate();work=work??Guid.NewGuid().ToString("N");string revision=Guid.NewGuid().ToString("N");state["works"][work]=new JObject{["name"]=name,["latest"]=revision};state["revisions"][revision]=new JObject{["work"]=work,["source"]=hash,["settings"]=JObject.Parse(settings.ToJson())};Write(state);return From(state,work,revision);}
 }
}
