using System;
using System.Collections;
using System.IO;
using System.Threading;
using SpatialWallpaper.Desktop;
using System.Collections.Concurrent;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Content;
using UnityEngine;
// Runtime is this Player process; the Manager only owns staging and draft files.
public sealed class RuntimeContentHost:MonoBehaviour {
 public RuntimeContentPresentation Presentation;ContentStore store;ContentCoordinator coordinator;RuntimeTrackingHost host;SynchronizationContext main;string basePath,startupError;bool stopping,readyForCommands,quitAllowed,quitPending;Task commandInFlight=Task.CompletedTask;readonly CancellationTokenSource runtimeStop=new CancellationTokenSource();
 public static string DataRoot=>Path.Combine(RuntimePaths.ApplicationData,"SpatialWallpaper","Content");
 string audioError;string AudioSettings=>Path.Combine(basePath,"audio-source.json");
 JObject soundMixes=new JObject();string soundSettingsError;string SoundSettings=>Path.Combine(basePath,"sound-mixes.json");
 SpatialWallpaper.Sdk.Capabilities.SoundMix WorkSound(string work)=>soundMixes[work]==null?new SpatialWallpaper.Sdk.Capabilities.SoundMix():SpatialWallpaper.Sdk.Capabilities.SoundMix.Parse(soundMixes[work]);
 void SaveSoundMixes(JObject next){Directory.CreateDirectory(basePath);string temp=SoundSettings+"."+Guid.NewGuid().ToString("N")+".tmp";try{File.WriteAllText(temp,next.ToString(Newtonsoft.Json.Formatting.None));if(File.Exists(SoundSettings))File.Replace(temp,SoundSettings,null);else File.Move(temp,SoundSettings);}finally{if(File.Exists(temp))File.Delete(temp);}}
 void RestoreSoundMixes(){if(File.Exists(SoundSettings)){if(new FileInfo(SoundSettings).Length>1048576)throw new InvalidDataException("sound-settings-size");var values=JObject.Parse(File.ReadAllText(SoundSettings));if(values.Count>4096)throw new InvalidDataException("sound-settings-count");foreach(var item in values.Properties()){ContentStore.Id(item.Name);SpatialWallpaper.Sdk.Capabilities.SoundMix.Parse(item.Value);}soundMixes=values;}Presentation.SoundForWork=WorkSound;}
 ContentDesktopRuntime desktop;readonly ConcurrentQueue<string> desktopEvents=new ConcurrentQueue<string>();readonly DesktopPolicy desktopPolicy=new DesktopPolicy();Task desktopTransition=Task.CompletedTask;bool desktopPaused,desktopTrackingEnabled;
 Task viewportRecovery=Task.CompletedTask;string viewportRecoveryKey;
 void Update(){
  if(desktop!=null){desktop.SetWallpaperState(Presentation.HasContent||Presentation.StaticHeld||store.HasApplied(Presentation.DisplayId),desktopPolicy.Manual);if(!stopping&&readyForCommands&&commandInFlight.IsCompleted&&viewportRecovery.IsCompleted&&desktopTransition.IsCompleted&&desktopEvents.TryDequeue(out var message)){var eventDone=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);desktopTransition=eventDone.Task;_ = CompleteDesktopEvent(message,eventDone);}}
  if(!desktopTransition.IsCompleted)return;

  if(stopping||!readyForCommands||!commandInFlight.IsCompleted||!viewportRecovery.IsCompleted||!Presentation.ViewportRecoveryRequired)return;
  string display=Presentation.DisplayId;var applied=store.Snapshot["applied"][display] as JObject;if(applied==null)return;
  string key=Screen.width+"x"+Screen.height+"|"+display+"|"+(string)applied["work_id"]+"|"+(string)applied["revision_id"];if(viewportRecoveryKey==key)return;viewportRecoveryKey=key;readyForCommands=false;
  var done=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);viewportRecovery=done.Task;_ = RecoverViewport(display,done);
 }
 async Task RecoverViewport(string display,TaskCompletionSource<bool> done){try{await Presentation.UnloadAsync();runtimeStop.Token.ThrowIfCancellationRequested();if(!Presentation.Measure()||Presentation.DisplayId!=display)throw new InvalidOperationException("viewport-recovery-display-changed");startupError=await ContentStartupRecovery.RestoreAsync(store,display,Presentation);runtimeStop.Token.ThrowIfCancellationRequested();}catch(Exception failure){startupError="viewport-content-recovery-failed:"+failure.GetBaseException().Message;if(startupError.Length>2048)startupError=startupError.Substring(0,2048);}finally{readyForCommands=!stopping;done.TrySetResult(true);}}
 IEnumerator Start(){
  main=SynchronizationContext.Current;Application.wantsToQuit+=WantsToQuit;basePath=ContentPlayerBootstrap.Argument("--content-data-root")??DataRoot;store=new ContentStore(Path.Combine(basePath,"library"),Path.Combine(basePath,"staging"));coordinator=new ContentCoordinator(store,Presentation);
  try{if(File.Exists(AudioSettings)){if(new FileInfo(AudioSettings).Length>1024)throw new InvalidDataException("audio-selection-settings-size");Presentation.RestoreAudioSource(JObject.Parse(File.ReadAllText(AudioSettings)));}}catch(Exception failure){audioError="audio-selection-settings:"+failure.GetBaseException().Message;}
  try{RestoreSoundMixes();}catch(Exception failure){soundSettingsError=failure.GetBaseException().Message;Presentation.SoundForWork=WorkSound;}
  host=new RuntimeTrackingHost();try{host.StartManagerControl(Path.Combine(basePath,"runtime"),Dispatch);}catch(Exception e){Debug.LogError("Runtime ownership failed: "+e.GetType().Name);Application.Quit(1);yield break;}
  if(ContentPlayerBootstrap.Argument("--content-desktop")=="true"){
   Debug.Log("Content desktop startup: finding owned window");
   if(Application.platform!=RuntimePlatform.WindowsPlayer){startupError="content-desktop-platform-unverified";yield break;}
   IntPtr window=IntPtr.Zero;float until=Time.realtimeSinceStartup+10;while(window==IntPtr.Zero&&Time.realtimeSinceStartup<until){window=WindowsDesktopWindow.FindOwnWindow(true);if(window==IntPtr.Zero)yield return null;}
   if(window==IntPtr.Zero){startupError="content-desktop-window-unavailable";yield break;}
   try{var displays=WindowsDesktopWindow.Displays();string selected=ContentPlayerBootstrap.Argument("--content-display")??(displays.Length>0?displays[0].Id:null);desktop=new ContentDesktopRuntime(window,selected,value=>desktopEvents.Enqueue(value));DisplayViewport.RegisterRenderWindow(window);}
   catch(Exception failure){startupError=failure.GetBaseException().Message;Debug.LogError(startupError);yield break;}
   Debug.Log("Content desktop startup: attached");
   yield return null;yield return null;yield return null;
  }
  // Native window readiness is bounded and does not enable tracking.
  float deadline=Time.realtimeSinceStartup+10;while(Time.realtimeSinceStartup<deadline&&!Presentation.Measure())yield return new WaitForSecondsRealtime(.05f);if(Presentation.DisplayId==null){Debug.LogError("Runtime viewport unavailable");yield break;}
  var recovery=ContentStartupRecovery.RestoreAsync(store,Presentation.DisplayId,Presentation);while(!recovery.IsCompleted)yield return null;startupError=recovery.IsFaulted?"startup-recovery-failed":recovery.Result;
  Debug.Log("Content runtime startup: viewport and recovery complete");
  if(desktop!=null&&!Presentation.HasContent){SetDesktopTracking(false);desktop.Hide();}
  readyForCommands=true;
  Debug.Log("Content runtime startup: ready");
 }
 async Task CompleteDesktopEvent(string message,TaskCompletionSource<bool> done){
  try{
   switch(message){
    case "open":OpenManager();return;
    case "quit":Application.Quit();return;
    case "pause":desktopPolicy.Manual=!desktopPolicy.Manual;break;
    case "stop":await Presentation.UnloadAsync();store.ClearApplied(Presentation.DisplayId);Presentation.ReleaseStatic();desktopPolicy.Manual=false;desktopPaused=false;SetDesktopTracking(false);desktop.Hide();return;
    case "locked":desktopPolicy.Locked=true;break;case "unlocked":desktopPolicy.Locked=false;break;
    case "suspended":desktopPolicy.Suspended=true;break;case "resumed":desktopPolicy.Suspended=false;break;
    case "display-changed":case "explorer-changed":if(!desktop.Attached)desktop.Reattach();return;
    default:if(message.StartsWith("shell-error:",StringComparison.Ordinal))throw new IOException(message);return;
   }
   if(desktopPolicy.Paused&&!desktopPaused){await Presentation.HoldStaticAsync();await Presentation.UnloadAsync();SetDesktopTracking(false);desktopPaused=true;}
   else if(!desktopPolicy.Paused&&desktopPaused){SetDesktopTracking(true);desktop.Show();startupError=await ContentStartupRecovery.RestoreAsync(store,Presentation.DisplayId,Presentation);runtimeStop.Token.ThrowIfCancellationRequested();if(startupError!=null)throw new IOException(startupError);Presentation.ReleaseStatic();desktopPaused=false;}
  }catch(Exception failure){startupError="desktop-operation-failed:"+failure.GetBaseException().Message;Debug.LogWarning(startupError);}finally{done.TrySetResult(true);}
 }
 void SetDesktopTracking(bool active){var tracker=Presentation.DisplayCamera?Presentation.DisplayCamera.GetComponent<TrackingEyeDriver>():null;if(!tracker)return;if(!active){desktopTrackingEnabled|=tracker.enabled;tracker.StopTracking();tracker.enabled=false;}else if(desktopTrackingEnabled&&!tracker.enabled)tracker.enabled=true;}
 void OpenManager(){
  string image=Path.GetFullPath(Path.Combine(Application.dataPath,"../../manager/ContentManager.exe"));if(!File.Exists(image))throw new FileNotFoundException("content-manager-installation-missing",image);
  int owner=DesktopManagerLease.ActiveOwner(basePath);if(owner>0&&WindowsDesktopWindow.ActivateManager(owner,image))return;
  string arguments="--content-data-root "+QuoteLaunch(basePath)+" -logFile "+QuoteLaunch(Path.Combine(basePath,"manager.log"));
  foreach(string name in new[]{"--sdk-runtime","--sdk-attempts","--content-events"}){string value=ContentPlayerBootstrap.Argument(name);if(!string.IsNullOrEmpty(value))arguments+=" "+name+" "+QuoteLaunch(value);}
  using(var child=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(image,arguments){UseShellExecute=false,WindowStyle=System.Diagnostics.ProcessWindowStyle.Normal})){}
 }
 static string QuoteLaunch(string value){if(value.Contains("\"")||value.EndsWith("\\"))throw new IOException("invalid-launch-path");return "\""+value+"\"";}
 JObject DesktopSnapshot(){var snapshot=desktop?.Snapshot;if(snapshot!=null){snapshot["paused"]=desktopPaused;snapshot["pause_reason"]=desktopPolicy.Reason;}return snapshot;}
 Task<JObject> Dispatch(JObject command){var complete=new TaskCompletionSource<JObject>(TaskCreationOptions.RunContinuationsAsynchronously);main.Post(async _=>{try{if(stopping)throw new IOException("runtime-stopping");commandInFlight=complete.Task;complete.TrySetResult(await Execute(command));}catch(Exception e){complete.TrySetException(e);}},null);return complete.Task;}
 async Task<JObject> Execute(JObject command){
  var payload=(JObject)command["payload"];if((string)command["type"]!="GetContentState"){await desktopTransition;runtimeStop.Token.ThrowIfCancellationRequested();}if(!viewportRecovery.IsCompleted&&(string)command["type"]!="GetContentState"){await viewportRecovery;runtimeStop.Token.ThrowIfCancellationRequested();}if(!readyForCommands&&(string)command["type"]!="GetContentState")throw new InvalidOperationException("runtime-initializing");switch((string)command["type"]){
   case "GetContentState":
    if(readyForCommands&&desktopTransition.IsCompleted&&viewportRecovery.IsCompleted)await Presentation.RefreshSoundStateAsync();
    if(payload.Count==0)SpatialWallpaper.Sdk.JsonData.Fields(payload);else SpatialWallpaper.Sdk.JsonData.Fields(payload,"after","snapshot");
    var result=store.Page(Presentation.DisplayId,payload.Count==0?null:SpatialWallpaper.Sdk.JsonData.Text(payload["after"],36),payload.Count==0?null:SpatialWallpaper.Sdk.JsonData.Text(payload["snapshot"],32));result["display_id"]=Presentation.DisplayId;result["camera_started"]=Presentation.CameraStarted;result["tracking"]=Presentation.TrackingState;result["runtime_ready"]=readyForCommands;result["presentation_ready"]=Presentation.Ready;result["static_held"]=Presentation.StaticHeld;result["startup_error"]=startupError;result["render_errors"]=Presentation.RenderErrors;result["presentation_metrics"]=Presentation.Metrics;result["audio_source"]=Presentation.SelectedAudioSource;result["audio_error"]=audioError;result["desktop"]=DesktopSnapshot();var pageMixes=new JObject();foreach(var item in ((JObject)result["state"]["works"]).Properties())if(soundMixes[item.Name]!=null)pageMixes[item.Name]=soundMixes[item.Name].DeepClone();result["sound_mixes"]=pageMixes;result["sound_error"]=Presentation.SoundError??soundSettingsError;return result;
   case "SetSoundMix":
    SpatialWallpaper.Sdk.JsonData.Fields(payload,"work_id","mix");string soundWork=ContentStore.Id((string)payload["work_id"]);if(store.Snapshot["works"][soundWork]==null)throw new InvalidDataException("sound-work-missing");var soundMix=SpatialWallpaper.Sdk.Capabilities.SoundMix.Parse(payload["mix"]);var nextMixes=(JObject)soundMixes.DeepClone();foreach(var entry in soundMixes.Properties())if(store.Snapshot["works"][entry.Name]==null)nextMixes.Remove(entry.Name);if(nextMixes[soundWork]==null&&nextMixes.Count>=4096)throw new InvalidDataException("sound-settings-count");nextMixes[soundWork]=soundMix.Json;SaveSoundMixes(nextMixes);soundMixes=nextMixes;soundSettingsError=null;await Presentation.SetSoundMixAsync(soundWork,soundMix);return new JObject{{"work_id",soundWork},{"mix",soundMix.Json},{"sound_error",Presentation.SoundError}};
   case "SetAudioSource":
    SpatialWallpaper.Sdk.JsonData.Fields(payload,"source");var nextAudio=SpatialWallpaper.Sdk.Hosting.TrustedAudioSource.RequireLive(payload["source"]);
    if(desktopPaused||desktopPolicy.Locked||desktopPolicy.Suspended)throw new InvalidOperationException("resume-before-changing-audio-source");
    bool restoreAudio=store.HasApplied(Presentation.DisplayId);if(Presentation.HasContent)await Presentation.HoldStaticAsync();await Presentation.UnloadAsync();await Presentation.ConfigureAudioSourceAsync(nextAudio);runtimeStop.Token.ThrowIfCancellationRequested();
    string temporary=AudioSettings+"."+Guid.NewGuid().ToString("N")+".tmp";try{if(nextAudio==null){if(File.Exists(AudioSettings))File.Delete(AudioSettings);}else{File.WriteAllText(temporary,nextAudio.ToString(Newtonsoft.Json.Formatting.None));if(File.Exists(AudioSettings))File.Replace(temporary,AudioSettings,null);else File.Move(temporary,AudioSettings);}}finally{if(File.Exists(temporary))File.Delete(temporary);}
    audioError=null;if(restoreAudio){startupError=await ContentStartupRecovery.RestoreAsync(store,Presentation.DisplayId,Presentation);runtimeStop.Token.ThrowIfCancellationRequested();if(startupError!=null)throw new IOException(startupError);}Presentation.ReleaseStatic();return new JObject{{"source",Presentation.SelectedAudioSource},{"restored_applied",restoreAudio}};
   case "ImportContent":if(payload.ContainsKey("name"))SpatialWallpaper.Sdk.JsonData.Fields(payload,"staging_id","name");else SpatialWallpaper.Sdk.JsonData.Fields(payload,"staging_id");string importedName=payload.ContainsKey("name")?SpatialWallpaper.Sdk.JsonData.Text(payload["name"],128):"Imported sandbox content";if(string.IsNullOrWhiteSpace(importedName))throw new InvalidDataException("content-name-empty");string id=ContentStore.Id((string)payload["staging_id"]),path=Path.Combine(basePath,"staging",id+".spatial");if(await Task.Run(()=>ContentPackageFormat.IsLegacy(path)))return await Task.Run(()=>store.Import(id));var inspected=await Presentation.InspectSandboxAsync(path);runtimeStop.Token.ThrowIfCancellationRequested();return await Task.Run(()=>store.ImportSandbox(id,inspected,importedName));
   case "UpdateContent":
    SpatialWallpaper.Sdk.JsonData.Fields(payload,"staging_id","work_id","expected_revision");
    string updateWork=ContentStore.Id((string)payload["work_id"]),expectedRevision=ContentStore.Id((string)payload["expected_revision"]),updateId=ContentStore.Id((string)payload["staging_id"]),updatePath=Path.Combine(basePath,"staging",updateId+".spatial");
    SpatialWallpaper.Sdk.JsonData.Require(store.Latest(updateWork)==expectedRevision,"revision-conflict");
    if(await Task.Run(()=>ContentPackageFormat.IsLegacy(updatePath)))return await Task.Run(()=>store.Update(updateId,updateWork,expectedRevision));
    var updateInspection=await Presentation.InspectSandboxAsync(updatePath);runtimeStop.Token.ThrowIfCancellationRequested();return await Task.Run(()=>store.UpdateSandbox(updateId,updateInspection,"Imported sandbox content",updateWork,expectedRevision));
   case "ExportContent":
    SpatialWallpaper.Sdk.JsonData.Fields(payload,"work_id","revision_id");return await Task.Run(()=>store.Export((string)payload["work_id"],(string)payload["revision_id"]));
   case "RemoveContent":
    SpatialWallpaper.Sdk.JsonData.Fields(payload,"work_id");try{var removed=await coordinator.RemoveAsync(Presentation.DisplayId,(string)payload["work_id"],Presentation.UninstallInstallationAsync,runtimeStop.Token);if((bool?)removed["succeeded"]==true&&soundMixes[(string)payload["work_id"]]!=null){var remaining=(JObject)soundMixes.DeepClone();remaining.Remove((string)payload["work_id"]);try{SaveSoundMixes(remaining);soundMixes=remaining;}catch(Exception cleanup){soundSettingsError="sound-settings-cleanup:"+cleanup.GetBaseException().Message;removed["sound_settings_cleanup_error"]=soundSettingsError;}}return removed;}finally{if(desktop!=null&&!Presentation.HasContent&&!Presentation.StaticHeld){desktopPaused=false;desktopPolicy.Manual=false;SetDesktopTracking(false);desktop.Hide();}}
   case "ApplyContent":SpatialWallpaper.Sdk.JsonData.Fields(payload,"display_id","work_id");if((string)payload["display_id"]!=Presentation.DisplayId)throw new InvalidDataException("unknown-display-target");if(desktop!=null){if(desktopPolicy.Locked||desktopPolicy.Suspended)throw new InvalidOperationException("desktop-session-inactive");desktop.Show();desktopPolicy.Manual=false;desktopPaused=false;SetDesktopTracking(true);}try{return await coordinator.ApplyAsync((string)payload["display_id"],(string)payload["work_id"],runtimeStop.Token);}finally{if(desktop!=null&&!Presentation.HasContent&&!Presentation.StaticHeld){SetDesktopTracking(false);desktop.Hide();}}
   default:throw new InvalidDataException("unsupported-content-command");
  }
 }
 bool WantsToQuit(){if(quitAllowed)return true;if(!quitPending){quitPending=true;stopping=true;runtimeStop.Cancel();_ = FinishQuit();}return false;}
 async Task FinishQuit(){await Task.Yield();try{int code=0;var ownedStop=Presentation.StopSandboxRuntimeAsync();try{await commandInFlight;}catch{}try{await viewportRecovery;}catch{}try{await desktopTransition;}catch{}try{await ownedStop;}catch(Exception failure){if(!Presentation.SandboxResourcesReleased)throw;startupError="runtime-stopped-after-failure:"+failure.GetBaseException().Message;Debug.LogError(startupError);code=1;}await Presentation.UnloadAsync();var tracker=Presentation.DisplayCamera?Presentation.DisplayCamera.GetComponent<TrackingEyeDriver>():null;if(tracker)tracker.StopTracking();desktop?.Dispose();desktop=null;host?.Dispose();host=null;quitAllowed=true;Application.Quit(code);}catch(Exception failure){startupError="runtime-cleanup-unproven:"+failure.GetBaseException().Message;Debug.LogError(startupError);quitPending=false;}}
 void OnDestroy(){Application.wantsToQuit-=WantsToQuit;stopping=true;runtimeStop.Cancel();desktop?.Dispose();desktop=null;host?.Dispose();runtimeStop.Dispose();}
}
