using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Library;
using UnityEngine;

public sealed partial class LibraryPreviewController {
 NativePreviewTarget nativePreview;
 int[] nativePreviewRegion;
 bool nativePreviewMeasured;
 double nativeDragX,nativeDragY;long nativeEscapes;
 DesktopDisplay nativePreviewDisplay;long nativePreviewDisplayRevision=-1;
 public JObject NativePreviewState=>nativePreview?.State;
 bool UseNativePreview(Artwork artwork){
#if UNITY_STANDALONE_WIN && !UNITY_EDITOR
  return artwork.Source==ArtworkSource.SdkNative||ArtworkCatalog.Builtins.Any(item=>item.Id==artwork.Id);
#else
  return false;
#endif
 }
 public bool NativePreview=>UseNativePreview(Selected)&&(Loading||Loaded!=null&&UseNativePreview(Loaded));
 JObject NativePreviewPose(){
  var projection=Runtime.projection;var eye=projection.EyeScreen;
  return new JObject{{"width",projection.displayWidth},{"height",projection.displayHeight},{"keep_height",KeepHeight},{"eye",new JArray(eye.x,eye.y,eye.z)},{"fresh",CameraMode&&projection.TrackingFresh},{"paused",!nativePreviewMeasured||closeRequested},{"tracking",DesktopTrackingState.Pack(Runtime.CurrentRaw,QpcClock.Now)},{"presentation",new JObject{{"numerator",nativePreviewDisplay?.RefreshNumerator??0},{"denominator",nativePreviewDisplay?.RefreshDenominator??0}}}};
 }
 public void MeasureNativePreview(Rect aperture){
  try{MeasureNativePreviewCore(aperture);}catch(Exception failure){nativePreviewMeasured=false;nativePreview?.Suspend();SetNativePreviewError(failure.GetBaseException().Message);}
 }
 void SetNativePreviewError(string failure){string message="预览播放失败，请返回或重试："+failure;if(Error==message)return;Error=message;Changed?.Invoke();}
 void MeasureNativePreviewCore(Rect aperture){
  if(!NativePreview)return;
  if(!DisplayViewport.TryMeasure(aperture,Runtime.Applied?.manual.display_width??.53,out var viewport,out var display,out var region)||region==null){nativePreviewMeasured=false;Runtime.SetViewport(null,null);nativePreview?.Suspend();return;}
  long revision=nativePreview?.DisplayRevision??-1;if(nativePreviewDisplay==null||nativePreviewDisplay.Id!=display||nativePreviewDisplayRevision!=revision){nativePreviewDisplay=WindowsDesktopWindow.Displays(true).SingleOrDefault(item=>item.Id==display);nativePreviewDisplayRevision=revision;}
  if(nativePreviewDisplay==null){nativePreviewMeasured=false;Runtime.SetViewport(null,null);nativePreview?.Suspend();return;}
  Runtime.SetViewport(viewport,display);nativePreviewRegion=region;nativePreviewMeasured=true;
  if(nativePreview==null)return;
  if(nativePreview.Error!=null){SetNativePreviewError(nativePreview.Error);return;}
  if(nativePreview.Ready.Status==TaskStatus.RanToCompletion){nativePreview.AttachReady(region);nativePreview.Verify();}
  if(Loaded!=null)Resize((float)viewport.Width,(float)viewport.Height);
  nativePreview.PublishPose(NativePreviewPose());
  var state=nativePreview.State;var interaction=state["preview_ui"] as JObject;
  if(interaction!=null){
   double x=(double)interaction["drag_x"],y=(double)interaction["drag_y"];long escapes=(long)interaction["escapes"];
   if(!TrackingCalibration.Finite(x)||!TrackingCalibration.Finite(y)||escapes<nativeEscapes)throw new InvalidDataException("native-preview-ui-state");
   Drag(new Vector2((float)(x-nativeDragX),(float)(y-nativeDragY)));nativeDragX=x;nativeDragY=y;
   if(escapes>nativeEscapes){nativeEscapes=escapes;ContentPreviewEscape?.Invoke();}
  }
  if(nativePreview?.Error!=null)SetNativePreviewError(nativePreview.Error);
 }
 async Task LoadNativePreview(Artwork artwork){
  float until=Time.realtimeSinceStartup+10;while(!nativePreviewMeasured&&!closeRequested){if(Time.realtimeSinceStartup>until)throw new TimeoutException("native-preview-viewport-unavailable");await Task.Yield();}
  if(closeRequested)return;Runtime.ExternalExhibitRoot=transform;Runtime.MousePreview=!CameraMode;Runtime.SetExhibit(true);framingKey=PreferencePrefix+"Framing."+artwork.Id+"."+Runtime.DisplayId;KeepHeight=PlayerPrefs.GetInt(framingKey,0)==0;MouseEye=Vector2.zero;nativeDragX=nativeDragY=0;nativeEscapes=0;
  if(!CameraMode){Runtime.projection.SetEye(new Vector3(0,0,-.67f));Runtime.projection.SetExternalFreshness(false);}
  var config=new JObject{{"artwork",artwork.Id},{"session",Guid.NewGuid().ToString("N")},{"generation",1},{"desktop",false},{"preview_ui",true},{"pose",NativePreviewPose()}};
  string player;using(var self=System.Diagnostics.Process.GetCurrentProcess())player=self.MainModule.FileName;
  if(artwork.Source==ArtworkSource.SdkNative){var store=new SpatialWallpaper.Content.ContentStore(Path.Combine(DesktopRuntime.ContentRoot,"library"),Path.Combine(DesktopRuntime.ContentRoot,"staging"));SpatialWallpaper.Content.NativeContentConfiguration.Configure(config,store,artwork.Id,artwork.RevisionId,DesktopRuntime.DataRoot,true);config["pose"]["sound"]=GetComponent<DesktopManager>().Client.Sound(artwork.Id).Json;}
  await SpatialWallpaper.Content.NativeContentConfiguration.PrepareInputAsync(config,DesktopRuntime.DataRoot,nativePreviewRegion);
  nativePreview=new NativePreviewTarget(player,Path.Combine(DesktopRuntime.DataRoot,"native-preview"),DisplayViewport.RegisteredRenderWindow,config);
  until=Time.realtimeSinceStartup+35;while(!nativePreview.Ready.IsCompleted&&!closeRequested){if(Time.realtimeSinceStartup>until)throw new TimeoutException("native-preview-ready-deadline");await Task.Yield();}
  if(closeRequested)return;await nativePreview.Ready;
  while(!closeRequested){if(nativePreviewMeasured){nativePreview.AttachReady(nativePreviewRegion);var state=nativePreview.State;if(nativePreview.Verify()&&(bool?)state["paused"]==false&&(int?)state["preview_ui"]?["rendered_width"]==nativePreviewRegion[2]&&(int?)state["preview_ui"]?["rendered_height"]==nativePreviewRegion[3]&&(int?)state["preview_ui"]?["rendered_frame"]>0)return;}if(nativePreview.Error!=null)throw new InvalidOperationException(nativePreview.Error);if(Time.realtimeSinceStartup>until)throw new TimeoutException("native-preview-placement-deadline");await Task.Yield();}
 }
 async Task ReleaseNativePreview(){
  nativePreviewMeasured=false;nativePreviewRegion=null;nativePreviewDisplay=null;nativePreviewDisplayRevision=-1;
  if(nativePreview==null)return;try{await nativePreview.CloseAsync();}catch(Exception failure){if(!nativePreview.ResourcesReleased)throw;Debug.LogWarning("Native preview ended: "+failure.GetBaseException().Message);}
  if(!nativePreview.ResourcesReleased)throw new InvalidOperationException("native-preview-cleanup-unproven");nativePreview=null;
 }
}
