using System;using System.IO;using System.Reflection;using System.Security.Cryptography;using Newtonsoft.Json.Linq;using SpatialWallpaper.Content;using SpatialWallpaper.Sdk.Hosting;
static class ContentRevisionChecks {
 static void Need(bool value,string why){if(!value)throw new Exception(why);}
 static JObject Call(ContentStore store,string method,params object[] arguments){try{return (JObject)typeof(ContentStore).GetMethod(method).Invoke(store,arguments);}catch(TargetInvocationException e){throw e.InnerException;}}
 static void Refuse(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("revision-expected-rejection");}
 static string Digest(string path){using(var file=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(file)).Replace("-","").ToLowerInvariant();}
 static void Main(string[] args){
  Need(typeof(ContentStore).GetMethod("Update")!=null&&typeof(ContentStore).GetMethod("UpdateSandbox")!=null,"content-revision-update-API-missing");
  string data=args[0],staging=Path.Combine(data,"staging");Directory.CreateDirectory(staging);var store=new ContentStore(Path.Combine(data,"library"),staging);
  string Stage(string file){string id=Guid.NewGuid().ToString("D");File.Copy(file,Path.Combine(staging,id+".spatial"));return id;}
  var initial=store.Import(Stage(args[1]));string work=(string)initial["work_id"],old=(string)initial["revision_id"];
  var neighbor=store.Import(Stage(args[2]));store.RecordApplied("primary",work,old);var applied=store.Snapshot["applied"].DeepClone();
  var updated=Call(store,"Update",Stage(args[2]),work,old);string fresh=(string)updated["revision_id"];
  Need((string)updated["work_id"]==work&&fresh!=old,"revision-lost-installation-identity");Need(store.Latest(work)==fresh&&((JObject)store.Snapshot["works"]).Count==2,"revision-created-another-work");
  Need(JToken.DeepEquals(store.Snapshot["applied"],applied),"revision-changed-current-playback-record");Need((string)store.Revision(work,old)["build_id"]==(string)initial["build_id"],"revision-destroyed-history");
  Need((string)store.Revision(work,fresh)["build_id"]==(string)neighbor["build_id"],"revision-wrong-build");
  string before=store.Snapshot.ToString();Refuse(()=>Call(store,"Update",Stage(args[1]),work,old));Need(before==store.Snapshot.ToString(),"stale-revision-mutated-store");
  var duplicate=Call(store,"Update",Stage(args[2]),work,fresh);Need((bool)duplicate["duplicate"]&&(string)duplicate["revision_id"]==fresh&&before==store.Snapshot.ToString(),"same-build-added-revision");
  Refuse(()=>Call(store,"Update",Stage(args[1]),"../other",fresh));Refuse(()=>Call(store,"Update",Stage(args[1]),(string)neighbor["work_id"],fresh));
  string bad=Guid.NewGuid().ToString("D");File.WriteAllText(Path.Combine(staging,bad+".spatial"),"not a package");Refuse(()=>Call(store,"Update",bad,work,fresh));Need(before==store.Snapshot.ToString(),"invalid-package-mutated-store");
  string modern=Guid.NewGuid().ToString("D"),modernPath=Path.Combine(staging,modern+".spatial");File.WriteAllBytes(modernPath,new byte[]{1,2,3,4});
  var inspection=new RuntimePackageInspection(new string('a',64),Digest(modernPath),new string[0]);
  var sandbox=store.ImportSandbox(modern,inspection,"Controlled sandbox");string sandboxWork=(string)sandbox["work_id"],sandboxOld=(string)sandbox["revision_id"];
  string next=Guid.NewGuid().ToString("D"),nextPath=Path.Combine(staging,next+".spatial");File.WriteAllBytes(nextPath,new byte[]{4,3,2,1});
  var nextInspection=new RuntimePackageInspection(new string('b',64),Digest(nextPath),new string[0]);
  var sandboxUpdated=Call(store,"UpdateSandbox",next,nextInspection,"Controlled next",sandboxWork,sandboxOld);
  Need((string)sandboxUpdated["work_id"]==sandboxWork&&(string)store.Revision(sandboxWork,(string)sandboxUpdated["revision_id"])["build_id"]==new string('b',64),"sandbox-update-identity-or-build");
  Need(store.Revision(sandboxWork,sandboxOld)!=null,"sandbox-history-lost");before=store.Snapshot.ToString();
  string repacked=Guid.NewGuid().ToString("D"),repackedPath=Path.Combine(staging,repacked+".spatial");File.WriteAllBytes(repackedPath,new byte[]{9,8,7});
  var sameBuild=new RuntimePackageInspection(new string('b',64),Digest(repackedPath),new string[0]);var same=Call(store,"UpdateSandbox",repacked,sameBuild,"Same admitted build",sandboxWork,(string)sandboxUpdated["revision_id"]);
  Need((bool)same["duplicate"]&&before==store.Snapshot.ToString()&&Digest(store.BuildPath(new string('b',64)))==nextInspection.ArchiveSha256,"same-build-repacked-update-changed-installed-archive");
  File.WriteAllText(nextPath,"changed");Refuse(()=>Call(store,"UpdateSandbox",next,nextInspection,"Controlled next",sandboxWork,(string)sandboxUpdated["revision_id"]));Need(before==store.Snapshot.ToString(),"changed-inspected-package-mutated-store");
  var reopened=new ContentStore(Path.Combine(data,"library"),staging);Need(JToken.DeepEquals(reopened.Snapshot,store.Snapshot),"revision-not-durable");
  File.WriteAllText(Path.Combine(data,"result.json"),new JObject{{"status","passed-core-revision-update"},{"same_work_identity",true},{"old_revision_and_applied_retained",true},{"stale_and_cross_work_rejected",true},{"same_build_idempotent",true},{"invalid_archive_rejected",true},{"durable_reopen",true},{"scope","Actual core store; sandbox receipts controlled, not Validator/Worker or live storage acceptance"}}.ToString());
 }
}
