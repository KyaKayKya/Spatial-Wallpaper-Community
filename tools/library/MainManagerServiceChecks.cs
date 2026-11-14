using System;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using Newtonsoft.Json.Linq;using SpatialWallpaper.Content;using SpatialWallpaper.Sdk.Capabilities;
namespace SpatialWallpaper.Content {
 public sealed class RuntimeContentPresentation:ISandboxContentPresentation {
  public Func<string,SoundMix> SoundForWork;public string DisplayId=>"display-1";public bool Ready=>true;public JObject Metrics=>new JObject();public string SoundError=>null;public JObject SelectedAudioSource;
  public string LastMixWork;public void RestoreAudioSource(JObject source){SelectedAudioSource=source;}public Task RefreshSoundStateAsync()=>Task.CompletedTask;public Task SetSoundMixAsync(string work,SoundMix mix){LastMixWork=work;return Task.CompletedTask;}
  public Task<SpatialWallpaper.Sdk.Hosting.RuntimePackageInspection> InspectSandboxAsync(string path)=>throw new NotSupportedException();public Task UninstallInstallationAsync(Guid work)=>Task.CompletedTask;
  public Task HoldStaticAsync()=>Task.CompletedTask;public Task UnloadAsync()=>Task.CompletedTask;public void ReleaseStatic(){}public Task LoadAsync(ValidatedPackage package,JObject revision,CancellationToken token)=>Task.CompletedTask;public Task FirstFrameAsync(CancellationToken token)=>Task.CompletedTask;public Task LoadSandboxAsync(SandboxContentBuild package,JObject revision,CancellationToken token)=>Task.CompletedTask;
 }
}
class MainManagerServiceChecks {
 static void Need(bool value,string message){if(!value)throw new Exception(message);}
 static void Main(string[] args){Run(args).GetAwaiter().GetResult();}
 static async Task Run(string[] args){
  string root=args[0];Directory.CreateDirectory(root);var presentation=new RuntimeContentPresentation();var service=new ContentLibraryService(root,presentation);
  string id=Guid.NewGuid().ToString("D");File.Copy(args[1],Path.Combine(root,"staging",id+".spatial"));var imported=await service.ExecuteAsync("ImportContent",new JObject{{"staging_id",id}},CancellationToken.None);string work=(string)imported["work_id"];
  var mix=new SoundMix(.37f,.21f,true,false);await service.ExecuteAsync("SetSoundMix",new JObject{{"work_id",work},{"mix",mix.Json}},CancellationToken.None);Need(presentation.LastMixWork==work,"work identity lost in sound command");
  var restored=new ContentLibraryService(root,new RuntimeContentPresentation());Need(restored.Sound(work).Music==mix.Music&&restored.Sound(work).Effects==mix.Effects&&restored.Sound(work).MusicMuted,"work mix did not survive service restart");
  string path=Path.Combine(root,"sound-mixes.json");byte[] saved=File.ReadAllBytes(path);using(var held=new FileStream(path,FileMode.Open,FileAccess.ReadWrite,FileShare.Read)){bool refused=false;try{await service.ExecuteAsync("SetSoundMix",new JObject{{"work_id",work},{"mix",new SoundMix(.9f,.8f,false,true).Json}},CancellationToken.None);}catch(IOException){refused=true;}Need(refused,"locked persisted mix must reject");}
  Need(saved.SequenceEqual(File.ReadAllBytes(path))&&service.Sound(work).Music==mix.Music,"failed mix save changed durable or live settings");Need(!Directory.GetFiles(root,"*.tmp").Any(),"failed save left temporary settings");
  string marker=Path.Combine(root,"main-manager-migration.json");File.Delete(marker);new ContentLibraryService(root,new RuntimeContentPresentation());var backup=JObject.Parse(File.ReadAllText(marker));Need(backup["files"]["library/index.json"]!=null&&backup["files"]["sound-mixes.json"]!=null,"existing index/settings backup missing");string backupRoot=(string)backup["backup"];Need(saved.SequenceEqual(File.ReadAllBytes(Path.Combine(backupRoot,"sound-mixes.json"))),"backup altered prior sound settings");byte[] markerBytes=File.ReadAllBytes(marker);new ContentLibraryService(root,new RuntimeContentPresentation());Need(markerBytes.SequenceEqual(File.ReadAllBytes(marker)),"migration backup must be idempotent");
  Console.WriteLine("PASS original content service: work mix restart, locked-save rollback, temporary cleanup and idempotent pre-integration backup");
 }
}
