using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk;
using SpatialWallpaper.Sdk.Native;
namespace SpatialWallpaper.Content {
 public sealed partial class ContentStore {
  public JObject ExportNative(string work,string revision){var selected=Revision(work,revision);string build=(string)selected["build_id"];var package=NativeInstalledPackage.Validate(BuildPath(build));JsonData.Require(package.BuildId==build&&(string)package.Manifest["license"]["mode"]=="share"&&(!package.IsExternal||(bool)package.External.Data.Manifest["reshare"]),"native-sharing-permission-not-declared");string directory=Path.Combine(Path.GetDirectoryName(root),"exports");Safe(directory);Directory.CreateDirectory(directory);string id=Guid.NewGuid().ToString("D");using(var output=new FileStream(Path.Combine(directory,id+".spatial"),FileMode.CreateNew,FileAccess.Write,FileShare.None)){package.WriteArchive(output);output.Flush(true);}return new JObject{{"export_id",id},{"work_id",work},{"revision_id",revision},{"build_id",build}};}
  public bool IsNativeBuild(string build){BuildPath(build);return (string)state["builds"][build]?["kind"]=="native-v5";}
  public NativeInstalledPackage OpenNativeBuild(string work,string revision,NativePlatformCatalog platform){var record=Revision(work,revision);string build=(string)record["build_id"];JsonData.Require(IsNativeBuild(build),"not-native-content-build");var package=NativeInstalledPackage.Validate(BuildPath(build),platform);JsonData.Require(package.BuildId==build&&(string)state["builds"][build]["native_work_id"]==package.WorkId,"native-installed-build-changed");return package;}
  public JObject ImportNative(string id,NativePlatformCatalog platform,string work=null,string expectedRevision=null){
   if(work!=null)RequireCurrent(work,expectedRevision);string staged=Path.Combine(staging,Id(id)+".spatial");Safe(staged);var package=NativeInstalledPackage.Validate(staged,platform);string build=package.BuildId;
   if(work!=null){var current=state["builds"][(string)Revision(work,expectedRevision)["build_id"]];JsonData.Require((string)current["native_work_id"]==package.WorkId,"native-update-work-identity");}
   var duplicate=((JObject)state["works"]).Properties().FirstOrDefault(p=>(string)p.Value["default_build"]==build);if(work==null&&duplicate!=null)return new JObject{{"build_id",build},{"work_id",duplicate.Name},{"revision_id",duplicate.Value["latest_revision"].DeepClone()},{"duplicate",true}};
   if(work==null)JsonData.Require(state["works"][package.WorkId]==null,"native-work-already-installed-use-explicit-update");
   JsonData.Require(!((JObject)state["builds"]).Properties().Any(item=>(string)item.Value["native_work_id"]==package.WorkId&&(string)item.Value["author_revision"]==package.Revision&&item.Name!=build),"native-published-revision-identity-conflict");
   var installed=state["builds"][build];JsonData.Require(installed==null||(string)installed["kind"]=="native-v5","native-build-format-conflict");string target=BuildPath(build),temporary=target+"."+Guid.NewGuid().ToString("N")+".tmp";
   Safe(target);if(File.Exists(target)){var existing=NativeInstalledPackage.Validate(target,platform);JsonData.Require(existing.BuildId==build,"native-existing-archive-conflict");}
   else try{using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None)){package.WriteArchive(output);output.Flush(true);}File.Move(temporary,target);}finally{if(File.Exists(temporary))File.Delete(temporary);}
   return RecordImport(build,new JObject{{"kind","native-v5"},{"native_work_id",package.WorkId},{"author_revision",package.Revision},{"scene_id",package.SceneId},{"name",package.Manifest["name"].DeepClone()},{"template_id",package.WorkId}},(string)package.Manifest["name"],work,expectedRevision);
  }
 }
}
