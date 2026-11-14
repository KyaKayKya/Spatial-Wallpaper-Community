using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using SpatialWallpaper.Content;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Library;
using UnityEngine;

public sealed partial class LibraryPreviewController {
 RuntimeContentPresentation sdkPreview;
 bool sdkMeasured;
 Task sdkResize=Task.CompletedTask;
 bool previewQuitAllowed,previewQuitPending;
 bool ContentWantsToQuit(){
  if(previewQuitAllowed||(nativePreview==null||nativePreview.ResourcesReleased)&&(!sdkPreview||sdkPreview.SandboxResourcesReleased&&sdkResize.IsCompleted))return true;
  if(!previewQuitPending){previewQuitPending=true;_ = FinishContentQuit();}return false;
 }
 async Task FinishContentQuit(){
  try{ClosePreview();float until=Time.realtimeSinceStartup+45;while(Loading||Loaded!=null){if(Time.realtimeSinceStartup>until)throw new TimeoutException("preview-exit-release-timeout");await Task.Yield();}await sdkResize;if(sdkPreview){await sdkPreview.RetireViewportAsync();if(!sdkPreview.SandboxResourcesReleased)throw new InvalidOperationException("preview-exit-cleanup-unproven");}if(nativePreview!=null)throw new InvalidOperationException("native-preview-exit-cleanup-unproven");await NativeBrokerLease.CloseAsync();previewQuitAllowed=true;Application.Quit();}
  catch(Exception failure){previewQuitPending=false;Error="关闭预览失败，请重试退出："+failure.GetBaseException().Message;Debug.LogWarning(failure);Changed?.Invoke();}
 }
 public Action ContentPreviewEscape;
 public bool ContentPreview=>Selected.IsContent&&!UseNativePreview(Selected)&&(Loading||Loaded?.IsContent==true);
 public void MeasureContentPreview(Rect aperture){
  EnsureContentPreview();var profile=Runtime.Applied;
  int left=Mathf.RoundToInt(aperture.xMin),bottom=Mathf.RoundToInt(aperture.yMin),right=Mathf.RoundToInt(aperture.xMax),top=Mathf.RoundToInt(aperture.yMax);aperture=new Rect(left,bottom,right-left,top-bottom);
  if(!DisplayViewport.TryMeasure(aperture,profile?.manual.display_width??.53,out var viewport,out var display)){sdkMeasured=false;return;}
  Runtime.SetViewport(viewport,display);sdkPreview.SetExternalViewport(aperture,viewport,display);sdkMeasured=true;
  if(!Loading&&Loaded?.IsContent==true&&sdkResize.IsCompleted&&sdkPreview.ViewportRecoveryRequired)sdkResize=ResizeContentPreview(Loaded);
  if(!Loading&&Loaded?.IsContent==true&&sdkResize.IsCompleted&&!sdkPreview.Ready){string failure=(string)sdkPreview.Metrics["sandbox_error"];if(failure!=null&&Error!="预览播放失败，请返回或重试："+failure){Error="预览播放失败，请返回或重试："+failure;Changed?.Invoke();}}
 }
 void EnsureContentPreview(){
  if(sdkPreview)return;sdkPreview=gameObject.AddComponent<RuntimeContentPresentation>();sdkPreview.DisplayCamera=Runtime.projection.GetComponent<Camera>();sdkPreview.ExternalViewport=true;sdkPreview.PreviewMode=true;sdkPreview.ContentDataRoot=DesktopRuntime.ContentRoot;
  sdkPreview.SoundForWork=work=>GetComponent<DesktopManager>().Client.Sound(work);sdkPreview.ObserveDrag=Drag;sdkPreview.ObserveEscape=()=>ContentPreviewEscape?.Invoke();
 }
 async Task LoadContentPreview(Artwork artwork){
  EnsureContentPreview();sdkPreview.RestoreAudioSource(GetComponent<DesktopManager>().Client.ContentState?["audio_source"]);float until=Time.realtimeSinceStartup+10;while(!sdkMeasured&&!closeRequested){if(Time.realtimeSinceStartup>until)throw new TimeoutException("content-preview-viewport-unavailable");await Task.Yield();}
  if(closeRequested)return;Runtime.ExternalExhibitRoot=transform;Runtime.MousePreview=!CameraMode;Runtime.SetExhibit(true);framingKey=PreferencePrefix+"Framing."+artwork.Id+"."+Runtime.DisplayId;KeepHeight=PlayerPrefs.GetInt(framingKey,0)==0;sdkPreview.PreserveWidth=!KeepHeight;MouseEye=Vector2.zero;
  var store=new ContentStore(Path.Combine(DesktopRuntime.ContentRoot,"library"),Path.Combine(DesktopRuntime.ContentRoot,"staging"));
  using(var prepared=ContentCoordinator.Prepare(store,sdkPreview,store.Revision(artwork.Id,artwork.RevisionId),artwork.RevisionId))using(var timeout=new CancellationTokenSource(30000)){await prepared.Load(timeout.Token);await sdkPreview.FirstFrameAsync(timeout.Token);}
 }
 async Task ResizeContentPreview(Artwork artwork){
  try{await sdkPreview.RetireViewportAsync();if(closeRequested||Loaded!=artwork)return;var store=new ContentStore(Path.Combine(DesktopRuntime.ContentRoot,"library"),Path.Combine(DesktopRuntime.ContentRoot,"staging"));using(var prepared=ContentCoordinator.Prepare(store,sdkPreview,store.Revision(artwork.Id,artwork.RevisionId),artwork.RevisionId))using(var timeout=new CancellationTokenSource(30000)){await prepared.Load(timeout.Token);await sdkPreview.FirstFrameAsync(timeout.Token);}}
  catch(Exception failure){Error="预览尺寸恢复失败："+failure.GetBaseException().Message;Debug.LogWarning(failure);Changed?.Invoke();}
 }
}
