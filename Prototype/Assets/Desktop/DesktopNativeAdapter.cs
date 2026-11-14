using System;
using System.IO;
using System.Linq;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Library;
using SpatialWallpaper.Content;
using SpatialWallpaper.Sdk.Capabilities;
using SpatialWallpaper.Sdk.Hosting;
using UnityEngine;
using Debug=UnityEngine.Debug;

namespace SpatialWallpaper.Desktop {
 public sealed partial class DesktopRuntime {
  sealed class NativePlayback {
   public NativeDesktopTarget Target;public NativeDesktopPose Pose;public DesktopDisplay Display;public bool Failed;
  }
  readonly Dictionary<string,NativePlayback> nativePlaybacks=new Dictionary<string,NativePlayback>(StringComparer.Ordinal);
  readonly Dictionary<string,string> nativeFailures=new Dictionary<string,string>(StringComparer.Ordinal);
  DesktopAppliedStore appliedStore;int nativePoseFrame=-1;
  bool UsesNative(Artwork artwork){
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
   return artwork.Source==ArtworkSource.SdkNative||ArtworkCatalog.Builtins.Any(a=>a.Id==artwork.Id);
#else
   return false;
#endif
  }
  bool NativeSelected=>active!=null&&UsesNative(ArtworkCatalog.Find(active.ArtworkId));
  bool NativeApplied(DesktopAppliedSelection selected)=>(selected.Source==ArtworkSource.BuiltinNative||selected.Source==ArtworkSource.SdkNative)&&UsesNative(ArtworkCatalog.Find(selected.ArtworkId));
  bool HasPlayback=>nativePlaybacks.Count>0||active!=null&&!NativeSelected&&!legacyTargetOffline;
  void InitializeAppliedStore(){
   if(appliedStore==null)appliedStore=new DesktopAppliedStore(AppliedPath,(work,rev)=>(string)contentService.Store.Revision(work,rev)["build_id"],work=>Guid.TryParseExact(work,"D",out var ignored)?contentService.Sound(work):new SoundMix());
  }
  DesktopAppliedSelection AppliedSelection(WallpaperRequest request,CalibrationProfile profile,string contentRevision){
   var artwork=ArtworkCatalog.Find(request.ArtworkId);string build=artwork.IsContent?(string)contentService.Store.Revision(request.ArtworkId,contentRevision)["build_id"]:null;
   DesktopAppliedSelection previous=null;if(appliedStore!=null)appliedStore.Snapshot.Targets.TryGetValue(request.DisplayId,out previous);bool paused=previous!=null?previous.Paused:policy.Manual;
   return new DesktopAppliedSelection(request.ArtworkId,artwork.Source,contentRevision,build,profile,artwork.IsContent?(previous!=null&&previous.ArtworkId==request.ArtworkId?previous.Sound:contentService.Sound(request.ArtworkId,request.DisplayId)):new SoundMix(),paused);
  }
  async Task<NativePlayback> OpenNative(DesktopAppliedSelection selected){
   if(nativePlaybacks.ContainsKey(selected.DisplayId))throw new InvalidOperationException("native-target-original-owner-retained");
   var display=WindowsDesktopWindow.Displays(true).SingleOrDefault(d=>d.Id==selected.DisplayId);if(display==null)throw new InvalidOperationException("native-target-display-offline");
   var pose=new NativeDesktopPose(selected.Profile,display,library.KeepHeightFor(selected.ArtworkId,display.Id));
   var config=new JObject{{"artwork",selected.ArtworkId},{"session",Guid.NewGuid().ToString("N")},{"generation",1},{"desktop",true},{"diagnostics",nativeDiagnostics},{"pose",pose.Advance(runtime.CurrentRaw,QpcClock.Now,0,true,selected.Sound)}};
   string player=System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName;
   if(selected.Source==ArtworkSource.SdkNative)NativeContentConfiguration.Configure(config,contentService.Store,selected.ArtworkId,selected.RevisionId,DataRoot,false);
   await NativeContentConfiguration.PrepareInputAsync(config,DataRoot,new[]{display.X,display.Y,display.Width,display.Height});
   var target=new NativeDesktopTarget(player,Path.Combine(DataRoot,"native-render"),display.Id,config);
   var playback=new NativePlayback{Target=target,Pose=pose,Display=display};
   try{await target.Ready;target.AttachReady();await Frames(3);if(!target.Verify()||target.Error!=null)throw new InvalidOperationException(target.Error??"native-target-attach-unverified");return playback;}
   catch{try{await target.CloseAsync();}catch(Exception cleanup){Debug.LogWarning("Native startup cleanup: "+cleanup);}if(!target.ResourcesReleased){playback.Failed=true;nativePlaybacks.Add(display.Id,playback);nativeFailures[display.Id]="native-target-cleanup-unproven";}throw;}
  }
  async Task CloseNative(string display){
   if(!nativePlaybacks.TryGetValue(display,out var playback))return;
   // A failed detach retains this exact owner. Never let the regular pose
   // publication re-enable an uncommitted candidate or a retiring instance.
   playback.Failed=true;try{playback.Target.PublishPose(playback.Pose.Advance(runtime.CurrentRaw,QpcClock.Now,0,true));}catch(Exception pauseFailure){Debug.LogWarning("Native close pause: "+pauseFailure.Message);}
   try{await playback.Target.CloseAsync();}
   catch(Exception failure){if(!playback.Target.ResourcesReleased)throw;Debug.LogWarning("Native target ended: "+failure.GetBaseException().Message);}
   if(!playback.Target.ResourcesReleased)throw new InvalidOperationException("native-target-cleanup-unproven");
   nativePlaybacks.Remove(display);
  }
  async Task CloseNativeTargets(){foreach(string display in nativePlaybacks.Keys.ToArray())await CloseNative(display);}
  async Task PauseAppliedTargets(JObject payload){
   InitializeAppliedStore();bool paused=(bool)payload["paused"];string display=(string)payload["display_id"];var before=appliedStore.Snapshot;
   var next=before.WithPause(display,paused);
   await Task.Run(()=>appliedStore.Save(next));policy.Manual=next.AllPaused;await SetPaused();
  }
  async Task<JObject> StopAppliedTarget(string display){
   InitializeAppliedStore();var before=appliedStore.Snapshot;if(!before.Targets.TryGetValue(display,out var selected))throw new InvalidOperationException("desktop-target-not-applied");
   bool isNative=NativeApplied(selected);try{
    if(isNative)await CloseNative(display);else{await CloseScene();native.Detach();native.Hide();}
    await Task.Run(()=>appliedStore.Save(before.Without(display)));
   }catch{
    try{if(isNative&&!nativePlaybacks.ContainsKey(display))nativePlaybacks[display]=await OpenNative(selected);else if(!isNative){var p=selected.Profile;await Present(new WallpaperRequest(selected.ArtworkId,display,p.id,p.revision),p,selected.RevisionId);}}
    catch(Exception recovery){nativeFailures[display]=recovery.GetBaseException().Message;}throw;
   }
   if(!isNative)contentService.Store.ClearApplied(display);nativeFailures.Remove(display);
   if(active?.DisplayId==display){var peer=appliedStore.Snapshot.Targets.Values.FirstOrDefault();if(peer==null){active=null;activeProfile=null;activeContentRevision=null;}else{var p=peer.Profile;active=new WallpaperRequest(peer.ArtworkId,peer.DisplayId,p.id,p.revision);activeProfile=p;activeContentRevision=peer.RevisionId;}}
   policy.Manual=appliedStore.Snapshot.AllPaused;revision++;await SetPaused();return Result(true,null);
  }
  async Task<JObject> ApplyNative(WallpaperRequest request,CalibrationProfile profile){
   InitializeAppliedStore();var before=appliedStore.Snapshot;before.Targets.TryGetValue(request.DisplayId,out var previous);
   if(!before.Targets.ContainsKey(request.DisplayId)&&before.Targets.Count>=RuntimeLimits.MaxInstances)throw new InvalidOperationException("desktop-target-limit");
   before.RequireRouting(request.DisplayId,true,NativeApplied);
   // The legacy parent has one global scene. It cannot impersonate a second
   // target; retain it unchanged when a competing target is requested.
   bool legacy=active!=null&&!NativeSelected;
   if(legacy&&active.DisplayId!=request.DisplayId)throw new InvalidOperationException("旧 SDK 或蓝图当前使用单场景，请先停止该目标再应用另一目标。");
   var oldRequest=active;var oldProfile=activeProfile;string oldRevision=activeContentRevision;bool changed=false;
   try{
    changed=true;await CloseNative(request.DisplayId);
    if(legacy){await CloseScene();native.Detach();native.Hide();}
    var selected=AppliedSelection(request,profile,ArtworkCatalog.Find(request.ArtworkId).Source==ArtworkSource.SdkNative?contentService.Store.Latest(request.ArtworkId):null);var playback=await OpenNative(selected);nativePlaybacks.Add(request.DisplayId,playback);
    if(legacy)ClearContentApplied();await Task.Run(()=>appliedStore.Save(before.With(selected)));
    policy.Preview=false;policy.Manual=appliedStore.Snapshot.AllPaused;active=request;activeProfile=profile.Copy();activeContentRevision=selected.RevisionId;nativeFailures.Remove(request.DisplayId);error=null;revision++;PublishNativePoses();return Result(true,null);
   }catch(Exception failure){
    error=failure.GetBaseException().Message;
    try{
     await CloseNative(request.DisplayId);
     if(changed&&previous!=null&&UsesNative(ArtworkCatalog.Find(previous.ArtworkId)))nativePlaybacks[previous.DisplayId]=await OpenNative(previous);
     else if(changed&&legacy&&oldRequest!=null)await Present(oldRequest,oldProfile,oldRevision);
    }catch(Exception recovery){nativeFailures[request.DisplayId]=recovery.GetBaseException().Message;error+="；恢复失败："+nativeFailures[request.DisplayId];}
    active=oldRequest;activeProfile=oldProfile;activeContentRevision=oldRevision;revision++;PublishNativePoses();return Result(false,error);
   }
  }
  bool NativePaused(DesktopAppliedSelection selection,NativePlayback playback)=>policy.Locked||policy.Suspended||policy.Preview||selection.Paused||policy.PauseWhenCovered&&playback.Target.Covered();
  void PublishNativePoses(){
   if(appliedStore==null)return;
   double delta=nativePoseFrame==Time.frameCount?0:Time.unscaledDeltaTime;nativePoseFrame=Time.frameCount;var raw=runtime.CurrentRaw;ulong now=QpcClock.Now;
   foreach(var entry in nativePlaybacks){
    if(!appliedStore.Snapshot.Targets.TryGetValue(entry.Key,out var selected))continue;
    try{entry.Value.Target.PublishPose(entry.Value.Pose.Advance(raw,now,delta,entry.Value.Failed||NativePaused(selected,entry.Value),selected.Sound));}
    catch(Exception failure){nativeFailures[entry.Key]=failure.GetBaseException().Message;}
   }
  }
  async Task ReconcileNativeTargets(){
   if(appliedStore==null)return;var selections=appliedStore.Snapshot.Targets.Values.Where(NativeApplied).ToArray();if(selections.Length==0){PublishNativePoses();return;}var displays=WindowsDesktopWindow.Displays(true);
   foreach(var selected in selections){
    try{
     var display=displays.SingleOrDefault(d=>d.Id==selected.DisplayId);
     if(display==null){await CloseNative(selected.DisplayId);nativeFailures[selected.DisplayId]="目标显示器离线；应用项保留";continue;}
     if(nativePlaybacks.TryGetValue(selected.DisplayId,out var playback)){
      if(playback.Failed||playback.Target.Error!=null)throw new InvalidOperationException(playback.Target.Error??"native-target-cleanup-unproven");
      if(playback.Display.X!=display.X||playback.Display.Y!=display.Y||playback.Display.Width!=display.Width||playback.Display.Height!=display.Height){await CloseNative(selected.DisplayId);playback=null;}
      else{playback.Pose.UpdateRefresh(display);playback.Target.Reconcile();}
     }
     if(!nativePlaybacks.ContainsKey(selected.DisplayId))nativePlaybacks[selected.DisplayId]=await OpenNative(selected);
     nativeFailures.Remove(selected.DisplayId);
    }catch(Exception failure){nativeFailures[selected.DisplayId]=failure.GetBaseException().Message;}
   }
   PublishNativePoses();
  }
  void AddNativeSnapshot(JObject state){
   state["applied_state"]=appliedStore?.Snapshot.Json;state["native_targets"]=new JArray(nativePlaybacks.Select(entry=>new JObject{{"display_id",entry.Key},{"attached",entry.Value.Target.Verify()},{"state",entry.Value.Target.State},{"error",nativeFailures.TryGetValue(entry.Key,out var failure)?failure:entry.Value.Target.Error}}));
   state["native_target_errors"]=JObject.FromObject(nativeFailures);
   if(!NativeSelected)return;
   if(nativeFailures.TryGetValue(active.DisplayId,out var targetFailure))state["error"]=targetFailure;
   if(appliedStore!=null&&appliedStore.Snapshot.Targets.TryGetValue(active.DisplayId,out var applied)&&applied.Paused){state["paused"]=true;state["pause_reason"]="manual";}
   state["attached"]=nativePlaybacks.TryGetValue(active.DisplayId,out var selected)&&selected.Target.Verify();state["scene_loaded"]=selected!=null&&(bool?)selected.Target.State["ready"]==true;
   if(selected!=null){var nativeState=selected.Target.State;state["render_width"]=nativeState["screen_width"];state["render_height"]=nativeState["screen_height"];state["native_presentation"]=nativeState;state["script_requires_reapply"]=(bool?)nativeState["script_requires_reapply"]==true;if((bool?)state["script_requires_reapply"]==true)state["error"]="脚本已停止，需重新应用："+(string)nativeState["script_error"];}
  }
 }
}
