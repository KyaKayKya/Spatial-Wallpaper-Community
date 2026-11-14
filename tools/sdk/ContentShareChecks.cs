using System;using System.IO;using System.Linq;using System.Reflection;using System.Security.Cryptography;using Newtonsoft.Json.Linq;using SpatialWallpaper.Content;using SpatialWallpaper.Sdk;using SpatialWallpaper.Sdk.Hosting;
static class ContentShareChecks {
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static string Hash(string path){using(var stream=File.OpenRead(path))using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-","").ToLowerInvariant();}
 static void Reject(Action action){try{action();}catch(InvalidDataException){return;}throw new Exception("expected-rejection");}
 static void Main(string[] args){
  Need(typeof(ContentStore).GetMethod("Export",new[]{typeof(string),typeof(string)})!=null,"content-share-api-missing");
  string root=args[0],stage=Path.Combine(root,"staging");Directory.CreateDirectory(stage);var store=new ContentStore(Path.Combine(root,"library"),stage);
  JObject Import(string path){string id=Guid.NewGuid().ToString("D");File.Copy(path,Path.Combine(stage,id+".spatial"));return store.Import(id);}
  JObject Export(string work,string revision){return (JObject)typeof(ContentStore).GetMethod("Export").Invoke(store,new object[]{work,revision});}
  void Refuse(string work,string revision){try{Export(work,revision);}catch(TargetInvocationException e)when(e.InnerException is InvalidDataException){return;}throw new Exception("expected-export-rejection");}
  var denied=Import(args[1]);string before=store.Snapshot.ToString();Refuse((string)denied["work_id"],(string)denied["revision_id"]);Need(store.Snapshot.ToString()==before,"denied-share-mutated-library");
  var allowed=Import(args[2]);string work=(string)allowed["work_id"],revision=(string)allowed["revision_id"];store.RecordApplied("primary",work,revision);before=store.Snapshot.ToString();
  var result=Export(work,revision);string path=Path.Combine(root,"exports",ContentStore.Id((string)result["export_id"])+".spatial");Need(Hash(path)==Hash(store.BuildPath((string)allowed["build_id"])),"share-bytes-changed");Need(store.Snapshot.ToString()==before,"share-changed-applied-or-revision");Need(PackageValidator.Validate(path).BuildId==(string)allowed["build_id"],"share-not-reimportable");
  Refuse(work,(string)denied["revision_id"]);Refuse("../outside",revision);
  // Controlled authenticated-inspection receipt: checks store copying and digest
  // binding only, never claims restricted Validator or Worker admission coverage.
  string id=Guid.NewGuid().ToString("D"),modern=Path.Combine(stage,id+".spatial");File.WriteAllBytes(modern,new byte[]{1,2,3,4});string digest=Hash(modern);var item=store.ImportSandbox(id,new RuntimePackageInspection(new string('a',64),digest,new string[0]),"Controlled archive");
  Refuse((string)item["work_id"],(string)item["revision_id"]);
  File.WriteAllText(store.BuildPath(new string('a',64)),"changed");Refuse((string)item["work_id"],(string)item["revision_id"]);
  Need(Directory.GetFiles(Path.Combine(root,"exports")).Length==1,"rejected-export-left-files");
  File.WriteAllText(Path.Combine(root,"result.json"),new JObject{{"status","passed-core-share"},{"legacy_permissions_enforced",true},{"exact_archive_roundtrip",true},{"library_and_applied_unchanged",true},{"cross_work_and_path_rejected",true},{"sandbox_without_sharing_permission_refused",true},{"scope","Core store; controlled sandbox inspection; no IPC/UI/Worker claim"}}.ToString());
 }
}
