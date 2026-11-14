using System;using System.IO;using System.IO.Compression;using System.Linq;using System.Text;using Newtonsoft.Json.Linq;using SpatialWallpaper.Content;using SpatialWallpaper.Sdk.Native;
static class NativeSceneIdentityTests {
 static int checks;static void Need(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
 static string Retag(string source,string target,string work,string revision){
  using(var input=ZipFile.OpenRead(source))using(var output=ZipFile.Open(target,ZipArchiveMode.Create))foreach(var entry in input.Entries){
   using(var bytes=new MemoryStream()){using(var stream=entry.Open())stream.CopyTo(bytes);var raw=bytes.ToArray();
    if(entry.FullName=="manifest.json"){var m=JObject.Parse(Encoding.UTF8.GetString(raw));m["work_id"]=work;m["revision"]=revision;m["build_id"]=PackageValidator.Identity(m);raw=Encoding.UTF8.GetBytes(m.ToString(Newtonsoft.Json.Formatting.None));}
    using(var stream=output.CreateEntry(entry.FullName).Open())stream.Write(raw,0,raw.Length);
   }
  }return target;
 }
 static JObject Import(ContentStore store,string stage,string package,string work=null,string revision=null){string id=Guid.NewGuid().ToString("D");File.Copy(package,Path.Combine(stage,id+".spatial"));return store.ImportNative(id,null,work,revision);}
 static void RefuseScene(ContentStore store,string stage,string package,string work,string revision){
  string before=store.Snapshot.ToString();var files=Directory.GetFiles(Path.GetDirectoryName(store.BuildPath(new string('a',64)))).OrderBy(p=>p).ToArray();
  try{Import(store,stage,package,work,revision);}catch(InvalidDataException error){Need(error.Message=="native-update-scene-identity","wrong-refusal:"+error.Message);Need(store.Snapshot.ToString()==before,"scene-refusal-mutated-store");Need(files.SequenceEqual(Directory.GetFiles(Path.GetDirectoryName(store.BuildPath(new string('a',64)))).OrderBy(p=>p)),"scene-refusal-installed-build");return;}throw new Exception("cross-scene-native-update-accepted");
 }
 static void Main(string[] args){try{Run(args);}catch(Exception error){for(var e=error;e!=null;e=e.InnerException)Console.Error.WriteLine(e.GetType().FullName+":"+e.Message);Environment.Exit(1);}}
 static void Run(string[] a){string root=a[0];Directory.CreateDirectory(root);var external=NativeInstalledPackage.Validate(a[1]);var showcase=NativeInstalledPackage.Validate(a[2]);var mini=NativeInstalledPackage.Validate(a[3]);Need(external.IsExternal&&!showcase.IsExternal&&!mini.IsExternal,"actual-format-fixtures-required");Need(showcase.SceneId!=mini.SceneId,"actual-distinct-platform-scenes-required");
  string platformSameGuid=Retag(a[2],Path.Combine(root,"platform-same-guid.spatial"),external.WorkId,"route-check-platform");Need(NativeInstalledPackage.Validate(platformSameGuid).WorkId==external.WorkId,"platform-probe-not-admitted");
  string miniSameGuid=Retag(a[3],Path.Combine(root,"mini-same-guid.spatial"),showcase.WorkId,"route-check-mini");Need(NativeInstalledPackage.Validate(miniSameGuid).WorkId==showcase.WorkId,"mini-probe-not-admitted");
  string stage=Path.Combine(root,"x-stage");var store=new ContentStore(Path.Combine(root,"x"),stage);var first=Import(store,stage,a[1]);string work=(string)first["work_id"],revision=(string)first["revision_id"];store.RecordApplied("offline",work,revision);RefuseScene(store,stage,platformSameGuid,work,revision);
  string externalUpdate=Retag(a[1],Path.Combine(root,"external-update.spatial"),external.WorkId,external.Revision+"-scene-check");var update=Import(store,stage,externalUpdate,work,revision);Need((string)update["revision_id"]!=revision,"same-external-scene-update-rejected");Need((string)store.Snapshot["applied"]["offline"]["revision_id"]==revision,"same-scene-update-switched-playing-revision");
  stage=Path.Combine(root,"y-stage");store=new ContentStore(Path.Combine(root,"y"),stage);first=Import(store,stage,platformSameGuid);RefuseScene(store,stage,a[1],(string)first["work_id"],(string)first["revision_id"]);
  stage=Path.Combine(root,"z-stage");store=new ContentStore(Path.Combine(root,"z"),stage);first=Import(store,stage,a[2]);RefuseScene(store,stage,miniSameGuid,(string)first["work_id"],(string)first["revision_id"]);
  string normal=Retag(a[2],Path.Combine(root,"platform-update.spatial"),showcase.WorkId,showcase.Revision+"-scene-check");update=Import(store,stage,normal,(string)first["work_id"],(string)first["revision_id"]);Need((string)update["revision_id"]!=(string)first["revision_id"],"same-platform-scene-update-rejected");
  var result=new JObject{{"passed",true},{"checks",checks},{"actual_packages",new JArray(a.Skip(1))},{"cross_scene_rejected",new JArray("external-to-platform","platform-to-external","showcase-to-minishot")},{"same_external_and_platform_scene_updates",true},{"scope","actual admitted packages and original Store transactions; no renderer, GUI, author execution or desktop change"}};File.WriteAllText(Path.Combine(root,"result.json"),result.ToString());Console.WriteLine(result);
 }
}
