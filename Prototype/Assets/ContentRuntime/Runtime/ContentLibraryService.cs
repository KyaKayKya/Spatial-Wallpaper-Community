using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk;
using SpatialWallpaper.Sdk.Capabilities;

namespace SpatialWallpaper.Content {
 // Content commands have no window, Tracker, control server or tray ownership.
 public sealed class ContentLibraryService {
  readonly string root;
  readonly RuntimeContentPresentation presentation;
  readonly ContentCoordinator coordinator;
  public ContentStore Store {get;}
  JObject mixes=new JObject();
  JObject bundled=new JObject();
  string BundledPath=>Path.Combine(root,"bundled-catalog.json");
  public string SoundError {get;private set;}
  string MixPath=>Path.Combine(root,"sound-mixes.json");
  public ContentLibraryService(string root,RuntimeContentPresentation presentation){
   this.root=Path.GetFullPath(root);this.presentation=presentation;
   BackupBeforeIntegration();
   Store=new ContentStore(Path.Combine(root,"library"),Path.Combine(root,"staging"));coordinator=new ContentCoordinator(Store,presentation);Store.NativeExporter=Store.ExportNative;
   if(File.Exists(MixPath)){
    if(new FileInfo(MixPath).Length>1048576)throw new InvalidDataException("sound-settings-size");
    mixes=JObject.Parse(File.ReadAllText(MixPath));if(mixes.Count>4096)throw new InvalidDataException("sound-settings-count");
    foreach(var item in mixes.Properties()){ContentStore.Id(item.Name);SoundMix.Parse(item.Value);}
   }
   presentation.SoundForWork=Sound;
   string audio=Path.Combine(root,"audio-source.json");if(File.Exists(audio)){if(new FileInfo(audio).Length>1024)throw new InvalidDataException("audio-selection-settings-size");presentation.RestoreAudioSource(JObject.Parse(File.ReadAllText(audio)));}
   if(File.Exists(BundledPath))bundled=JObject.Parse(File.ReadAllText(BundledPath));
  }
  void BackupBeforeIntegration(){
   Directory.CreateDirectory(root);string marker=Path.Combine(root,"main-manager-migration.json");if(File.Exists(marker))return;
   var copies=new JObject();string directory=Path.Combine(root,"backups","main-manager-"+Guid.NewGuid().ToString("N"));
   foreach(string relative in new[]{"library/index.json","sound-mixes.json","audio-source.json","bundled-catalog.json"}){
    string source=Path.Combine(root,relative);if(!File.Exists(source))continue;string target=Path.Combine(directory,relative);Directory.CreateDirectory(Path.GetDirectoryName(target));File.Copy(source,target);
    using(var input=File.OpenRead(target))using(var sha=System.Security.Cryptography.SHA256.Create())copies[relative]=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();
   }
   // Installing bundles does not rewrite immutable archives or private saves.
   string temp=marker+".tmp";File.WriteAllText(temp,new JObject{{"format",1},{"backup",copies.Count==0?null:directory},{"files",copies},{"private_saves","kept in the original backend without writes"}}.ToString());File.Move(temp,marker);
  }
  public async Task InstallBundledAsync(string directory){
   string catalog=Path.Combine(directory,"catalog.json");if(!File.Exists(catalog))return;
   foreach(JObject item in JArray.Parse(File.ReadAllText(catalog))){
    string original=JsonData.Text(item["original_id"],64);var previous=bundled[original] as JObject;
    string updatingWork=null,updatingRevision=null;
    if(previous!=null){
     if((string)previous["build_id"]==(string)item["build_id"])continue;
     updatingWork=(string)previous["work_id"];
     // Respect uninstall and user-installed revisions. A bundled refresh only
     // advances an unchanged bundled work; application remains explicit.
     if(Store.Snapshot["works"][updatingWork]==null)continue;
     updatingRevision=Store.Latest(updatingWork);
     // Recover a completed store update whose alias marker was interrupted.
     if((string)Store.Revision(updatingWork,updatingRevision)["build_id"]==(string)item["build_id"]){previous["build_id"]=item["build_id"].DeepClone();SaveBundled();continue;}
     if((string)Store.Revision(updatingWork,updatingRevision)["build_id"]!=(string)previous["build_id"])continue;
    }
    string name=JsonData.Text(item["file"],128);if(Path.GetFileName(name)!=name||Path.GetExtension(name)!=".spatial")throw new InvalidDataException("bundled-package-path");
    string source=Path.Combine(directory,name),id=Guid.NewGuid().ToString("D"),staged=Path.Combine(root,"staging",id+".spatial");
    using(var input=File.OpenRead(source))using(var sha=System.Security.Cryptography.SHA256.Create())if(BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant()!=(string)item["sha256"])throw new InvalidDataException("bundled-package-digest");
    JObject result;try{File.Copy(source,staged);result=await ExecuteAsync(updatingWork==null?"ImportContent":"UpdateContent",updatingWork==null?new JObject{{"staging_id",id},{"name",item["name"].DeepClone()}}:new JObject{{"staging_id",id},{"work_id",updatingWork},{"expected_revision",updatingRevision}},CancellationToken.None);}finally{if(File.Exists(staged))File.Delete(staged);}
    if((string)result["build_id"]!=(string)item["build_id"])throw new InvalidDataException("bundled-build-identity");
    bundled[original]=new JObject{{"work_id",result["work_id"].DeepClone()},{"build_id",result["build_id"].DeepClone()},{"cover_id",item["cover_id"]?.DeepClone()??new JValue(original)}};
    SaveBundled();
   }
  }
  void SaveBundled(){string temporary=BundledPath+".tmp";File.WriteAllText(temporary,bundled.ToString());if(File.Exists(BundledPath))File.Replace(temporary,BundledPath,null);else File.Move(temporary,BundledPath);}
  public string Cover(string work){var alias=bundled.Properties().FirstOrDefault(p=>(string)p.Value["work_id"]==work);return alias==null?null:(string)alias.Value["cover_id"]??alias.Name;}
  public SoundMix Sound(string work)=>mixes[work]==null?new SoundMix():SoundMix.Parse(mixes[work]);
  void SaveMixes(JObject next){
   string temporary=MixPath+"."+Guid.NewGuid().ToString("N")+".tmp";
   try{using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None))using(var writer=new StreamWriter(stream)){writer.Write(next.ToString(Newtonsoft.Json.Formatting.None));writer.Flush();stream.Flush(true);}if(File.Exists(MixPath))File.Replace(temporary,MixPath,null);else File.Move(temporary,MixPath);mixes=next;SoundError=null;}
   finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
  public void SaveAudioSource(JObject source){string path=Path.Combine(root,"audio-source.json"),temp=path+"."+Guid.NewGuid().ToString("N")+".tmp";try{if(source==null){if(File.Exists(path))File.Delete(path);}else{File.WriteAllText(temp,source.ToString(Newtonsoft.Json.Formatting.None));if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);}}finally{if(File.Exists(temp))File.Delete(temp);}}
  public Task<JObject> ApplyAsync(string display,string work,CancellationToken token)=>coordinator.ApplyAsync(display,work,token);
  public async Task RestoreAsync(string display){var failure=await ContentStartupRecovery.RestoreAsync(Store,display,presentation);if(failure!=null)throw new IOException(failure);}
  public JObject Page(JObject payload){
   if(payload.Count==0)JsonData.Fields(payload);else JsonData.Fields(payload,"after","snapshot");
   var page=Store.Page(presentation.DisplayId,payload.Count==0?null:JsonData.Text(payload["after"],36),payload.Count==0?null:JsonData.Text(payload["snapshot"],32));
   page["runtime_ready"]=true;page["display_id"]=presentation.DisplayId;page["presentation_ready"]=presentation.Ready;page["presentation_metrics"]=presentation.Metrics;page["sound_error"]=presentation.SoundError??SoundError;page["audio_source"]=presentation.SelectedAudioSource;
   var selected=new JObject();foreach(var item in ((JObject)page["state"]["works"]).Properties())selected[item.Name]=Sound(item.Name).Json;page["sound_mixes"]=selected;page["bundled_catalog"]=bundled.DeepClone();return page;
  }
  public async Task<JObject> ExecuteAsync(string type,JObject payload,CancellationToken token){
   token.ThrowIfCancellationRequested();switch(type){
    case "GetContentState":await presentation.RefreshSoundStateAsync();return Page(payload);
    case "SetSoundMix":
     JsonData.Fields(payload,"work_id","mix");string work=ContentStore.Id((string)payload["work_id"]);Store.Latest(work);var mix=SoundMix.Parse(payload["mix"]);var next=(JObject)mixes.DeepClone();
     foreach(var item in mixes.Properties())if(Store.Snapshot["works"][item.Name]==null)next.Remove(item.Name);
     if(next[work]==null&&next.Count>=4096)throw new InvalidDataException("sound-settings-count");next[work]=mix.Json;SaveMixes(next);
     await presentation.SetSoundMixAsync(work,mix);return new JObject{{"work_id",work},{"mix",mix.Json},{"sound_error",presentation.SoundError}};
    case "ImportContent":case "UpdateContent":
     bool updating=type=="UpdateContent";
     if(updating)JsonData.Fields(payload,"staging_id","work_id","expected_revision");else if(payload.ContainsKey("name"))JsonData.Fields(payload,"staging_id","name");else JsonData.Fields(payload,"staging_id");
     string id=ContentStore.Id((string)payload["staging_id"]),path=Path.Combine(root,"staging",id+".spatial"),target=updating?ContentStore.Id((string)payload["work_id"]):null,expected=updating?ContentStore.Id((string)payload["expected_revision"]):null;
     if(updating&&Store.Latest(target)!=expected)throw new InvalidDataException("revision-conflict");
     if(await Task.Run(()=>ContentPackageFormat.IsNative(path))){var platform=NativeContentConfiguration.Platform();return await Task.Run(()=>Store.ImportNative(id,platform,target,expected));}
     if(await Task.Run(()=>ContentPackageFormat.IsLegacy(path)))return await Task.Run(()=>updating?Store.Update(id,target,expected):Store.Import(id));
     string name=updating?(string)Store.Snapshot["works"][target]["name"]:payload.ContainsKey("name")?JsonData.Text(payload["name"],128):"SDK 作品";
     var inspection=await presentation.InspectSandboxAsync(path);token.ThrowIfCancellationRequested();var imported=await Task.Run(()=>updating?Store.UpdateSandbox(id,inspection,name,target,expected):Store.ImportSandbox(id,inspection,name));
     if(!updating){bool changed=false;foreach(var alias in bundled.Properties())if((string)alias.Value["build_id"]==(string)imported["build_id"]&&Store.Snapshot["works"][(string)alias.Value["work_id"]]==null){alias.Value["work_id"]=imported["work_id"].DeepClone();changed=true;}if(changed){string temp=BundledPath+".tmp";File.WriteAllText(temp,bundled.ToString());File.Replace(temp,BundledPath,null);}}
     return imported;
    case "ExportContent":JsonData.Fields(payload,"work_id","revision_id");return await Task.Run(()=>Store.Export((string)payload["work_id"],(string)payload["revision_id"]));
    case "RemoveContent":
     JsonData.Fields(payload,"work_id");string removedWork=ContentStore.Id((string)payload["work_id"]);
     if(Store.IsNativeBuild((string)Store.Revision(removedWork,Store.Latest(removedWork))["build_id"])){var removedNative=await Task.Run(()=>Store.Remove(removedWork));removedNative["succeeded"]=true;removedNative["presentation_stopped"]=false;removedNative["storage_deleted"]=false;removedNative["private_saves_retained"]=true;if(mixes[removedWork]!=null){var remainingNative=(JObject)mixes.DeepClone();remainingNative.Remove(removedWork);SaveMixes(remainingNative);}return removedNative;}
     var removed=await coordinator.RemoveAsync(presentation.DisplayId,removedWork,presentation.UninstallInstallationAsync,token);
     if((bool?)removed["succeeded"]==true&&mixes[removedWork]!=null){var remaining=(JObject)mixes.DeepClone();remaining.Remove(removedWork);try{SaveMixes(remaining);}catch(Exception failure){SoundError=failure.GetBaseException().Message;removed["sound_settings_cleanup_error"]=SoundError;}}
     return removed;
    default:throw new InvalidDataException("unsupported-content-command");
   }
  }
 }
}
