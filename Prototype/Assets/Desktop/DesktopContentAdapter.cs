using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Content;
using SpatialWallpaper.Library;
using SpatialTracking;
using UnityEngine;

namespace SpatialWallpaper.Desktop {
 public sealed partial class DesktopRuntime {
  RuntimeContentPresentation contentPresentation;
  ContentLibraryService contentService;
  string activeContentRevision;
  bool contentPaused;
  RuntimeLease contentLease;
  string AppliedPath=>Path.Combine(DataRoot,"applied.json");
  public static string ContentRoot=>CalibrationRuntime.Argument("--content-data-root",CalibrationRuntime.Argument("--desktop-data-root")==null?Path.Combine(RuntimePaths.ApplicationData,"SpatialWallpaper","Content"):Path.Combine(DataRoot,"content"));
  void InitializeContent(){
   string legacyRuntime=Path.Combine(ContentRoot,"runtime");Directory.CreateDirectory(legacyRuntime);contentLease=RuntimeLease.TryAcquire(Path.Combine(legacyRuntime,"runtime.lock"));
   if(contentLease==null)throw new IOException("旧 SDK 后台仍在运行，请完全退出旧版后重开主管理器。");
   contentPresentation=gameObject.AddComponent<RuntimeContentPresentation>();contentPresentation.DisplayCamera=runtime.projection.GetComponent<Camera>();
   // Desktop owns the complete native client. Unity's Screen backbuffer can
   // still be 300x300 on restart after Attach resized the HWND to the monitor.
   contentPresentation.ExternalViewport=true;contentPresentation.FullParentViewport=true;contentPresentation.ContentDataRoot=ContentRoot;
   if(DisplayViewport.TryMeasure(new Rect(0,0,Screen.width,Screen.height),.53,out var viewport,out var display))contentPresentation.SetExternalViewport(new Rect(0,0,Screen.width,Screen.height),viewport,display);
   contentService=new ContentLibraryService(ContentRoot,contentPresentation);ArtworkCatalog.Content=ContentArtworks;
  }
  Artwork[] ContentArtworks(){
   var state=contentService.Store.Snapshot;return ((JObject)state["works"]).Properties().Select(item=>{string rev=(string)item.Value["latest_revision"];return Artwork.FromContent(item.Name,(string)item.Value["name"],rev,(string)state["revisions"][rev]["build_id"],contentService.Cover(item.Name),contentService.Store.IsNativeBuild((string)state["revisions"][rev]["build_id"]));}).ToArray();
  }
  void SaveApplied(WallpaperRequest request,CalibrationProfile profile,string contentRevision){
   InitializeAppliedStore();appliedStore.Save(request==null?new DesktopAppliedState(appliedStore.Snapshot.SelectedDisplayId,Array.Empty<DesktopAppliedSelection>()):appliedStore.Snapshot.With(AppliedSelection(request,profile,contentRevision)));policy.Manual=appliedStore.Snapshot.AllPaused;
  }
  async Task RestoreApplied(){
   try{InitializeAppliedStore();}catch(Exception failure){error="读取应用项失败："+failure.GetBaseException().Message;Debug.LogWarning(error);return;}
   foreach(var selected in appliedStore.Snapshot.Targets.Values){
    try{
     var artwork=ArtworkCatalog.Find(selected.ArtworkId);if(artwork.Source!=selected.Source)throw new InvalidDataException("desktop-applied-source-mismatch");
     if(artwork.IsContent&&(string)contentService.Store.Revision(selected.ArtworkId,selected.RevisionId)["build_id"]!=selected.BuildId)throw new InvalidDataException("desktop-applied-build-mismatch");
     var profile=selected.Profile;var request=new WallpaperRequest(selected.ArtworkId,selected.DisplayId,profile.id,profile.revision);
     if(UsesNative(artwork))nativePlaybacks[selected.DisplayId]=await OpenNative(selected);
     else{if(appliedStore.Snapshot.Targets.Count!=1)throw new InvalidOperationException("legacy-multi-target-not-supported");policy.Manual=selected.Paused;if(artwork.IsContent)await contentService.ExecuteAsync("SetSoundMix",new JObject{{"work_id",selected.ArtworkId},{"mix",selected.Sound.Json}},CancellationToken.None);await Present(request,profile,selected.RevisionId);}
     if(active==null||selected.DisplayId==appliedStore.Snapshot.SelectedDisplayId){active=request;activeProfile=profile;activeContentRevision=selected.RevisionId;}revision++;
    }catch(Exception failure){nativeFailures[selected.DisplayId]=failure.GetBaseException().Message;error="恢复壁纸失败："+nativeFailures[selected.DisplayId];Debug.LogWarning(error);}
   }
   policy.Manual=appliedStore.Snapshot.AllPaused;PublishNativePoses();
  }
  async Task PresentContent(WallpaperRequest request,CalibrationProfile profile,string fixedRevision){
   runtime.UseDesktopProfile(profile);runtime.SetExhibit(false);runtime.MousePreview=false;
   var camera=runtime.projection.GetComponent<Camera>();camera.targetTexture=null;camera.rect=new Rect(0,0,1,1);camera.enabled=true;
   if(!DisplayViewport.TryMeasure(new Rect(0,0,Screen.width,Screen.height),profile.manual.display_width,out var viewport,out var display)||display!=request.DisplayId)throw new InvalidOperationException("content-display-measurement-mismatch");
   runtime.SetViewport(viewport,display);contentPresentation.SetExternalViewport(new Rect(0,0,Screen.width,Screen.height),viewport,display);
   if(fixedRevision==null){var result=await contentService.ApplyAsync(display,request.ArtworkId,CancellationToken.None);if((bool?)result["succeeded"]!=true)throw new IOException("SDK 应用失败："+(string)result["diagnostic"]);activeContentRevision=(string)result["revision_id"];}
   else{contentService.Store.Revision(request.ArtworkId,fixedRevision);using(var prepared=ContentCoordinator.Prepare(contentService.Store,contentPresentation,contentService.Store.Revision(request.ArtworkId,fixedRevision),fixedRevision))using(var timeout=new CancellationTokenSource(30000)){await prepared.Load(timeout.Token);await contentPresentation.FirstFrameAsync(timeout.Token);}contentService.Store.RecordApplied(display,request.ArtworkId,fixedRevision);activeContentRevision=fixedRevision;}
   contentPaused=false;
  }
  static bool ContentCommand(string type)=>type=="GetContentState"||type=="SetSoundMix"||type=="SetAudioSource"||type=="ImportContent"||type=="UpdateContent"||type=="ExportContent"||type=="RemoveContent";
  async Task<JObject> ExecuteContent(string type,JObject payload){
   if(type=="SetAudioSource"){
    SpatialWallpaper.Sdk.JsonData.Fields(payload,"source");var source=SpatialWallpaper.Sdk.Hosting.TrustedAudioSource.RequireLive(payload["source"]);
    if(policy.Paused||policy.Preview)throw new InvalidOperationException("请先继续桌面播放并退出预览，再切换音源。");
    var previous=contentPresentation.SelectedAudioSource;bool restore=active!=null&&activeContentRevision!=null;
    try{if(restore)await contentPresentation.HoldStaticAsync();await contentPresentation.UnloadAsync();await contentPresentation.ConfigureAudioSourceAsync(source);contentService.SaveAudioSource(source);if(restore)await PresentContent(active,activeProfile,activeContentRevision);return new JObject{{"source",contentPresentation.SelectedAudioSource}};}
    catch(Exception failure){try{await contentPresentation.UnloadAsync();await contentPresentation.RestoreAudioSourceAfterFailureAsync(previous);contentService.SaveAudioSource(previous);if(restore)await PresentContent(active,activeProfile,activeContentRevision);}catch(Exception recovery){throw new AggregateException("音源切换及恢复失败",failure,recovery);}throw;}
    finally{contentPresentation.ReleaseStatic();}
   }
   if(type=="RemoveContent"){InitializeAppliedStore();string removing=(string)payload["work_id"];foreach(var selected in appliedStore.Snapshot.Targets.Values.Where(value=>value.ArtworkId==removing&&value.Source==ArtworkSource.SdkNative).ToArray())await StopAppliedTarget(selected.DisplayId);ClearContentApplied();if(active!=null&&activeContentRevision!=null)contentService.Store.RecordApplied(active.DisplayId,active.ArtworkId,activeContentRevision);}
   SpatialWallpaper.Sdk.Capabilities.SoundMix previousSound=null;if(type=="SetSoundMix")previousSound=contentService.Sound((string)payload["work_id"]);
   JObject result;try{result=await contentService.ExecuteAsync(type,payload,CancellationToken.None);}
   catch(Exception failure){if(previousSound!=null)await RestoreSoundAfterFailure((string)payload["work_id"],previousSound,failure);throw;}
   if(type=="SetSoundMix"&&appliedStore!=null){
    string work=(string)payload["work_id"];var before=appliedStore.Snapshot;var mix=contentService.Sound(work);
    if(before.Targets.Values.Any(s=>s.ArtworkId==work))try{var next=new DesktopAppliedState(before.SelectedDisplayId,before.Targets.Values.Select(s=>s.ArtworkId==work?new DesktopAppliedSelection(s.ArtworkId,s.Source,s.RevisionId,s.BuildId,s.Profile,mix,s.Paused):s));await Task.Run(()=>appliedStore.Save(next));}
    catch(Exception failure){await RestoreSoundAfterFailure(work,previousSound,failure);throw;}
   }
   if(type=="RemoveContent"&&(bool?)result["presentation_stopped"]==true){
    active=null;activeProfile=null;activeContentRevision=null;contentPaused=false;policy.Manual=false;native.Detach();native.Hide();revision++;SaveApplied(null,null,null);
   }
   return result;
  }
  async Task RestoreSoundAfterFailure(string work,SpatialWallpaper.Sdk.Capabilities.SoundMix previous,Exception failure){
   try{await contentService.ExecuteAsync("SetSoundMix",new JObject{{"work_id",work},{"mix",previous.Json}},CancellationToken.None);}
   catch(Exception recovery){error="声音设置与已应用项未恢复一致，请重试声音设置："+recovery.GetBaseException().Message;throw new AggregateException(error,failure,recovery);}
  }
  void ClearContentApplied(){foreach(var item in ((JObject)contentService.Store.Snapshot["applied"]).Properties())contentService.Store.ClearApplied(item.Name);}
  void MeasureContentDesktop(){
   if(active==null||NativeSelected||!ArtworkCatalog.Find(active.ArtworkId).IsContent||policy.Preview)return;
   if(!DisplayViewport.TryMeasure(new Rect(0,0,Screen.width,Screen.height),activeProfile.manual.display_width,out var viewport,out var display)||display!=active.DisplayId)throw new InvalidOperationException("content-desktop-viewport-unavailable");
   runtime.SetViewport(viewport,display);contentPresentation.SetExternalViewport(new Rect(0,0,Screen.width,Screen.height),viewport,display);
  }
  async Task PauseContent(bool paused){
   if(active==null||!ArtworkCatalog.Find(active.ArtworkId).IsContent||policy.Preview)return;
   if(paused&&!contentPaused){await contentPresentation.HoldStaticAsync();await contentPresentation.UnloadAsync();contentPaused=true;}
   else if(!paused&&contentPaused){await PresentContent(active,activeProfile,activeContentRevision);contentPresentation.ReleaseStatic();}
  }
 }
}
