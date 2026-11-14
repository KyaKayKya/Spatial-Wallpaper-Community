using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk;
using SpatialWallpaper.Sdk.Hosting;
using System.Security.Cryptography;
namespace SpatialWallpaper.Content {
 public sealed partial class ContentStore {
  readonly string root,staging,index;JObject state;string generation=Guid.NewGuid().ToString("N");
  public ContentStore(string root,string staging){
   this.root=Path.GetFullPath(root);this.staging=Path.GetFullPath(staging);Directory.CreateDirectory(this.root);Directory.CreateDirectory(this.staging);Safe(this.root);Safe(this.staging);Directory.CreateDirectory(Path.Combine(this.root,"builds"));index=Path.Combine(this.root,"index.json");
   state=File.Exists(index)?JsonData.Parse(File.ReadAllText(index)):new JObject{["schema"]=1,["builds"]=new JObject(),["works"]=new JObject(),["revisions"]=new JObject(),["applied"]=new JObject()};
   JsonData.Fields(state,"schema","builds","works","revisions","applied");JsonData.Integer(state["schema"],1,1);foreach(string group in new[]{"builds","works","revisions","applied"})JsonData.Require(state[group] is JObject,"invalid-store-index");
  }
  public Func<string,string,JObject> NativeExporter {get;set;}
  public JObject Snapshot=>(JObject)state.DeepClone();
  // Bounded transport page. A changed index invalidates continuation tokens;
  // the client restarts rather than combining different snapshots.
  public JObject Page(string display,string after=null,string snapshot=null){
   JsonData.Require(after==null&&snapshot==null||after!=null&&snapshot==generation,"content-page-stale");if(after!=null)Id(after);
   var works=new JObject();var builds=new JObject();var revisions=new JObject();var applied=new JObject();
   var candidates=((JObject)state["works"]).Properties().OrderBy(p=>p.Name,StringComparer.Ordinal).Where(p=>after==null||StringComparer.Ordinal.Compare(p.Name,after)>0).Take(17).ToArray();
   void AddRevision(string id){var revision=state["revisions"][id];revisions[id]=revision.DeepClone();string build=(string)revision["build_id"];builds[build]=state["builds"][build].DeepClone();}
   foreach(var work in candidates.Take(16)){works[work.Name]=work.Value.DeepClone();AddRevision((string)work.Value["latest_revision"]);}
   if(display!=null&&state["applied"][display] is JObject current){applied[display]=current.DeepClone();AddRevision((string)current["revision_id"]);}
   return new JObject{["state"]=new JObject{["schema"]=1,["works"]=works,["builds"]=builds,["revisions"]=revisions,["applied"]=applied},["page"]=new JObject{["snapshot"]=generation,["next"]=candidates.Length>16?candidates[15].Name:null}};
  }
  public static string Id(string value){Guid id;JsonData.Require(Guid.TryParseExact(value,"D",out id)&&id.ToString("D")==value,"invalid-content-id");return value;}
  static void Safe(string path){for(string p=Path.GetFullPath(path);p!=null;p=Path.GetDirectoryName(p))if(File.Exists(p)||Directory.Exists(p))JsonData.Require((File.GetAttributes(p)&FileAttributes.ReparsePoint)==0,"content-store-link");}
  void Publish(JObject next){Safe(root);Safe(index);string temp=index+"."+Guid.NewGuid().ToString("N")+".tmp";try{using(var stream=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(stream)){writer.Write(next.ToString());writer.Flush();stream.Flush(true);}if(File.Exists(index))File.Replace(temp,index,null);else File.Move(temp,index);state=next;generation=Guid.NewGuid().ToString("N");}finally{if(File.Exists(temp))File.Delete(temp);}}
  public JObject Import(string id)=>ImportCore(id,null,null);
  public JObject Update(string id,string work,string expectedRevision){RequireCurrent(work,expectedRevision);RequireNonNativeUpdate(work,expectedRevision);return ImportCore(id,work,expectedRevision);}
  void RequireNonNativeUpdate(string work,string revision){JsonData.Require((string)state["builds"][(string)Revision(work,revision)["build_id"]]?["kind"]!="native-v5","native-update-format-mismatch");}
  void RequireCurrent(string work,string expectedRevision){Id(expectedRevision);JsonData.Require(Latest(work)==expectedRevision,"revision-conflict");}
  JObject RecordImport(string build,JObject metadata,string name,string work,string expectedRevision){
   if(work!=null){RequireCurrent(work,expectedRevision);if((string)Revision(work,expectedRevision)["build_id"]==build)return new JObject{["build_id"]=build,["work_id"]=work,["revision_id"]=expectedRevision,["duplicate"]=true};}
   var next=Snapshot;string target=work??((string)metadata["kind"]=="native-v5"?(string)metadata["native_work_id"]:Guid.NewGuid().ToString("D")),revision=Guid.NewGuid().ToString("D");next["builds"][build]=metadata;
   if(work==null)next["works"][target]=new JObject{["name"]=name,["default_build"]=build,["latest_revision"]=revision};
   else{next["works"][target]["default_build"]=build;next["works"][target]["latest_revision"]=revision;}
   next["revisions"][revision]=new JObject{["work_id"]=target,["build_id"]=build,["parameters"]=new JObject()};Publish(next);
   return new JObject{["build_id"]=build,["work_id"]=target,["revision_id"]=revision,["duplicate"]=false};
  }
  JObject ImportCore(string id,string work,string expectedRevision){
   string path=Path.Combine(staging,Id(id)+".spatial");var package=PackageValidator.Validate(path);string build=package.BuildId;
   var existing=((JObject)state["works"]).Properties().FirstOrDefault(p=>(string)p.Value["default_build"]==build);
   if(work==null&&existing!=null)return new JObject{["build_id"]=build,["work_id"]=existing.Name,["revision_id"]=(string)existing.Value["latest_revision"],["duplicate"]=true};
   string target=BuildPath(build),temp=Path.Combine(Path.GetDirectoryName(target),Guid.NewGuid().ToString("N")+".tmp");Safe(target);
   if(!File.Exists(target))try{using(var output=new FileStream(temp,FileMode.CreateNew,FileAccess.Write,FileShare.None)){package.WriteArchive(output);output.Flush(true);}File.Move(temp,target);}finally{if(File.Exists(temp))File.Delete(temp);}
   return RecordImport(build,new JObject{["template_id"]=package.TemplateId,["name"]=(string)package.Manifest["name"]},(string)package.Manifest["name"],work,expectedRevision);
  }
  // Only an authenticated restricted-Validator result can enter this path.
  // No program/content JSON is parsed or executed in the library process.
  public JObject ImportSandbox(string id,RuntimePackageInspection inspection,string name)=>ImportSandboxCore(id,inspection,name,null,null);
  public JObject UpdateSandbox(string id,RuntimePackageInspection inspection,string name,string work,string expectedRevision){RequireCurrent(work,expectedRevision);RequireNonNativeUpdate(work,expectedRevision);return ImportSandboxCore(id,inspection,name,work,expectedRevision);}
  JObject ImportSandboxCore(string id,RuntimePackageInspection inspection,string name,string work,string expectedRevision){
   if(inspection==null)throw new ArgumentNullException(nameof(inspection));JsonData.Require(!string.IsNullOrWhiteSpace(name)&&name.Length<=128,"content-name");string path=Path.Combine(staging,Id(id)+".spatial");Safe(path);
   using(var source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
    JsonData.Require(source.Length>0&&source.Length<=RuntimePackageInspection.MaxArchiveBytes,"sandbox-archive-size");string digest;using(var sha=SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(source)).Replace("-","").ToLowerInvariant();JsonData.Require(digest==inspection.ArchiveSha256,"sandbox-inspection-archive-changed");
    string build=inspection.BuildId;var existing=((JObject)state["works"]).Properties().FirstOrDefault(p=>(string)p.Value["default_build"]==build);
    if(existing!=null){JsonData.Require((string)state["builds"][build]?["kind"]=="sandbox-v4","content-build-format-conflict");if(work==null)return new JObject{["build_id"]=build,["work_id"]=existing.Name,["revision_id"]=(string)existing.Value["latest_revision"],["duplicate"]=true};}
    var installed=state["builds"][build] as JObject;if(installed!=null)JsonData.Require((string)installed["kind"]=="sandbox-v4","content-build-format-conflict");
    string target=BuildPath(build),temporary=Path.Combine(Path.GetDirectoryName(target),Guid.NewGuid().ToString("N")+".tmp");Safe(target);
    if(installed!=null)JsonData.Require(File.Exists(target),"sandbox-installed-archive-missing");
    // The restricted Validator binds canonical build identity. A re-export can
    // have different ZIP timestamps/compression; retain the already installed
    // archive and its own digest instead of replacing its immutable record.
    if(File.Exists(target)){using(var saved=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read))using(var sha=SHA256.Create())JsonData.Require(BitConverter.ToString(sha.ComputeHash(saved)).Replace("-","").ToLowerInvariant()==(installed==null?digest:(string)installed["archive_sha256"]),"sandbox-existing-archive-conflict");}
    else try{source.Position=0;using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){source.CopyTo(output);output.Flush(true);}File.Move(temporary,target);}finally{if(File.Exists(temporary))File.Delete(temporary);}
    return RecordImport(build,installed==null?new JObject{["kind"]="sandbox-v4",["template_id"]=null,["name"]=name,["archive_sha256"]=digest,["capabilities"]=new JArray(inspection.Capabilities)}:(JObject)installed.DeepClone(),name,work,expectedRevision);
   }
  }
  public string BuildPath(string build){JsonData.Require(build!=null&&build.Length==64&&build.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f'),"invalid-build-id");string path=Path.Combine(root,"builds",build+".spatial");Safe(path);return path;}
  public bool IsSandboxBuild(string build){BuildPath(build);return (string)state["builds"][build]?["kind"]=="sandbox-v4";}
  public SandboxContentBuild OpenSandboxBuild(string work,string revisionId){
   var revision=Revision(work,revisionId);string build=(string)revision["build_id"];JsonData.Require(IsSandboxBuild(build),"not-sandbox-content-build");var record=state["builds"][build];string path=BuildPath(build);var source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);
   try{JsonData.Require(source.Length>0&&source.Length<=RuntimePackageInspection.MaxArchiveBytes,"sandbox-archive-size");string digest;using(var sha=SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(source)).Replace("-","").ToLowerInvariant();JsonData.Require(digest==(string)record["archive_sha256"],"sandbox-installed-archive-changed");source.Position=0;return new SandboxContentBuild(path,build,digest,Guid.ParseExact(work,"D"),revisionId,((JArray)record["capabilities"]).Select(t=>(string)t).ToArray(),source);}
   catch{source.Dispose();throw;}
  }
  public string Latest(string id){Id(id);JsonData.Require(state["works"][id]!=null,"unknown-work");return (string)state["works"][id]["latest_revision"];}
  public JObject Revision(string work,string revision){Id(work);Id(revision);var value=state["revisions"][revision] as JObject;JsonData.Require(value!=null&&(string)value["work_id"]==work,"unknown-work-revision");return (JObject)value.DeepClone();}
  public string Commit(string id,string revision,JObject parameters){
   JsonData.Require(Latest(id)==revision,"revision-conflict");JsonData.Require(parameters!=null&&parameters.Count==0,"unsupported-parameters");var next=Snapshot;string fresh=Guid.NewGuid().ToString("D");var value=Revision(id,revision);value["parameters"]=parameters.DeepClone();next["revisions"][fresh]=value;next["works"][id]["latest_revision"]=fresh;Publish(next);return fresh;
  }
  // Export the immutable package only. Local saves, audio selection and runtime
  // credentials never live in this archive or enter the export directory.
  public JObject Export(string work,string revision){
   var selected=Revision(work,revision);string build=(string)selected["build_id"],sourcePath=BuildPath(build);
   if((string)state["builds"][build]?["kind"]=="native-v5"){JsonData.Require(NativeExporter!=null,"native-exporter-unavailable");return NativeExporter(work,revision);}
   JsonData.Require(!IsSandboxBuild(build),"sandbox-sharing-permission-not-declared");
   using(var source=new FileStream(sourcePath,FileMode.Open,FileAccess.Read,FileShare.Read)){
     var package=PackageValidator.Validate(sourcePath);JsonData.Require(package.BuildId==build,"installed-package-changed");
     JsonData.Require((bool)package.Manifest["reshare"]&&(string)package.Manifest["license"]["mode"]=="share","content-sharing-not-permitted");
    string directory=Path.Combine(Path.GetDirectoryName(root),"exports");Safe(directory);Directory.CreateDirectory(directory);Safe(directory);
    string id=Guid.NewGuid().ToString("D"),target=Path.Combine(directory,id+".spatial");
    try{using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None)){source.CopyTo(output);output.Flush(true);}}
    catch{if(File.Exists(target))File.Delete(target);throw;}
    return new JObject{["export_id"]=id,["work_id"]=work,["revision_id"]=revision,["build_id"]=build};
   }
  }
  public void RecordApplied(string display,string work,string revision){JsonData.Require(!string.IsNullOrEmpty(display)&&display.Length<=256,"display-id");Revision(work,revision);var next=Snapshot;next["applied"][display]=new JObject{["work_id"]=work,["revision_id"]=revision};Publish(next);}
  // Call only after presentation release and installation storage removal.
  // Publish first: a locked index must never leave a retained work without its archive.
  public JObject Remove(string work){
   Latest(work);var next=Snapshot;
   var revisions=((JObject)next["revisions"]).Properties().Where(p=>(string)p.Value["work_id"]==work).ToArray();
   var candidates=revisions.Select(p=>(string)p.Value["build_id"]).Concat(new[]{(string)next["works"][work]["default_build"]}).Distinct(StringComparer.Ordinal).ToArray();
   foreach(string build in candidates)BuildPath(build);
   foreach(var revision in revisions)revision.Remove();((JObject)next["works"]).Remove(work);
   foreach(var applied in ((JObject)next["applied"]).Properties().Where(p=>(string)p.Value["work_id"]==work).ToArray())applied.Remove();
   var unused=candidates.Where(build=>!((JObject)next["works"]).Properties().Any(p=>(string)p.Value["default_build"]==build)&&!((JObject)next["revisions"]).Properties().Any(p=>(string)p.Value["build_id"]==build)).ToArray();
   foreach(string build in unused)((JObject)next["builds"]).Remove(build);
   Publish(next);
   var remaining=new JArray();var errors=new JArray();
   foreach(string build in unused)try{File.Delete(BuildPath(build));}catch(Exception error)when(error is IOException||error is UnauthorizedAccessException){remaining.Add(build);errors.Add(error.Message);}
   return new JObject{["removed"]=true,["work_id"]=work,["remaining_builds"]=remaining,["cleanup_error"]=errors.Count==0?null:string.Join("; ",errors.Values<string>())};
  }
  public bool HasApplied(string display){return display!=null&&state["applied"][display]!=null;}
  public void ClearApplied(string display){JsonData.Require(!string.IsNullOrEmpty(display)&&display.Length<=256,"display-id");var next=Snapshot;if(((JObject)next["applied"]).Remove(display))Publish(next);}
 }
}
