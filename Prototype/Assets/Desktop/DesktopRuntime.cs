using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Content;
using SpatialWallpaper.Library;
using UnityEngine;
using UnityEngine.Rendering;
using Debug=UnityEngine.Debug;

namespace SpatialWallpaper.Desktop {
 [DefaultExecutionOrder(-1000)]public sealed partial class DesktopRuntime:MonoBehaviour {
  public static bool IsRuntime=>CalibrationRuntime.Argument("--desktop-runtime")=="true";
  public static string DataRoot=>CalibrationRuntime.Argument("--desktop-data-root",Path.Combine(RuntimePaths.ApplicationData,"SpatialWallpaper","Desktop"));
  public static string Descriptor=>Path.Combine(DataRoot,"runtime","connection.json");
  IDesktopSurface native;readonly DesktopPolicy policy=new DesktopPolicy();readonly ConcurrentQueue<string> events=new ConcurrentQueue<string>();
  RuntimeTrackingHost control;IDisposable shell;CalibrationRuntime runtime;LibraryPreviewController library;SynchronizationContext main;
  WallpaperRequest active;CalibrationProfile activeProfile;bool busy,quitting,ready;long revision;string error;float managerSeen,nextCheck;bool captureAuthorized,managerConsumer;string cameraId,cameraName,cameraMode;bool useSimulation,trackingResetRequested;
  int renderErrors;
  int nativeRenderedFrame=-1,nativeRenderedCamera;string nativeRenderedArtwork;bool nativeDiagnostics;
  void Awake(){
   if(!IsRuntime){enabled=false;return;}runtime=GetComponent<CalibrationRuntime>();library=GetComponent<LibraryPreviewController>();
   GetComponent<CalibrationDemoView>().enabled=false;var smoke=GetComponent<LibrarySmoke>();if(smoke)smoke.enabled=false;
   Application.logMessageReceived+=RenderLog;
   nativeDiagnostics=CalibrationRuntime.Argument("--native-presentation-diagnostics")=="true";
   if(nativeDiagnostics){Camera.onPostRender+=ObserveNativeFrame;RenderPipelineManager.endCameraRendering+=ObserveNativeSrpFrame;}
  }
  IEnumerator Start(){
   main=SynchronizationContext.Current;
   try{control=new RuntimeTrackingHost();control.StartManagerControl(Path.Combine(DataRoot,"runtime"),Dispatch);native=DesktopPlatform.CreateSurface();shell=DesktopPlatform.CreateShell(value=>events.Enqueue(value));}
   catch(Exception e){Debug.LogError(e);Application.Quit(1);yield break;}
   yield return DesktopManager.CompleteStartup(false);
   float deadline=Time.realtimeSinceStartup+10;
   bool initialized=false;
   while(!initialized&&Time.realtimeSinceStartup<deadline){initialized=native.Initialize();if(!initialized)yield return null;}
   if(!initialized){Debug.LogError("desktop-own-window-unavailable");Application.Quit(1);yield break;}
   runtime.SetExhibit(false);try{InitializeContent();}catch(Exception failure){Debug.LogError(failure);Application.Quit(1);yield break;}var bundled=contentService.InstallBundledAsync(Path.Combine(Application.streamingAssetsPath,"sdk-content"));while(!bundled.IsCompleted)yield return null;if(bundled.IsFaulted){error="内置 SDK 作品安装失败："+bundled.Exception.GetBaseException().Message;Debug.LogWarning(bundled.Exception);}var nativeBundled=contentService.InstallBundledAsync(Path.Combine(Application.streamingAssetsPath,"native-content"));while(!nativeBundled.IsCompleted)yield return null;if(nativeBundled.IsFaulted){error="原生 SDK 作品安装失败："+nativeBundled.Exception.GetBaseException().Message;Debug.LogWarning(nativeBundled.Exception);}
   var recovery=RestoreApplied();while(!recovery.IsCompleted)yield return null;if(recovery.IsFaulted){Debug.LogError(recovery.Exception);Application.Quit(1);yield break;}ready=true;
  }
  Task<JObject> Dispatch(JObject command){var complete=new TaskCompletionSource<JObject>();main.Post(async _=>{try{complete.TrySetResult(await Execute((string)command["type"],(JObject)command["payload"]));}catch(Exception e){Debug.LogWarning("Desktop command "+(string)command["type"]+": "+e);complete.TrySetException(e);}},null);return complete.Task;}
  JObject BasicSnapshot(){
   float corner=0;if(library.Loaded!=null&&runtime.projection){var p=runtime.projection;var camera=p.GetComponent<Camera>();foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){var point=camera.WorldToViewportPoint(p.screenFrame.TransformPoint(new Vector3(x*p.displayWidth,y*p.displayHeight,0)));corner=Mathf.Max(corner,new Vector2((point.x-x-.5f)*camera.pixelWidth,(point.y-y-.5f)*camera.pixelHeight).magnitude);}}
   return new JObject{["ready"]=ready,["active_artwork"]=active?.ArtworkId,["display_id"]=active?.DisplayId,["revision"]=revision,["attached"]=native?.Verify()??false,["scene_loaded"]=library.Loaded!=null,["paused"]=policy.Paused,["pause_reason"]=policy.Reason,["preview"]=policy.Preview,["error"]=error,["camera_started"]=runtime.CameraStarted,["camera_id"]=runtime.CameraId,["camera_name"]=runtime.CameraName,["camera_mode_id"]=runtime.CameraModeId,["camera_modes"]=runtime.CameraModes,["pid"]=Process.GetCurrentProcess().Id,["corner_error_px"]=corner,["displays"]=new JArray(DesktopPlatform.Displays().Select(d=>new JObject{["id"]=d.Id,["x"]=d.X,["y"]=d.Y,["width"]=d.Width,["height"]=d.Height}))};
  }
  public JObject Snapshot(){var state=BasicSnapshot();if(native!=null)foreach(var field in native.Diagnostics.Properties())if(!state.ContainsKey(field.Name))state[field.Name]=field.Value.DeepClone();state["quitting"]=quitting;state["render_width"]=Screen.width;state["render_height"]=Screen.height;state["render_preset_id"]=runtime.DesktopRenderingProfile?.id;state["audio_paused"]=AudioListener.pause;state["render_errors"]=renderErrors;state["content_revision"]=activeContentRevision;state["content_ready"]=contentPresentation&&contentPresentation.Ready;state["content_metrics"]=contentPresentation?contentPresentation.Metrics:null;if(nativeDiagnostics)state["native_presentation"]=NativePresentationObservation();AddNativeSnapshot(state);return state;}
  void ObserveNativeSrpFrame(ScriptableRenderContext context,Camera camera)=>ObserveNativeFrame(camera);
  void ObserveNativeFrame(Camera camera){if(library&&library.Loaded!=null&&runtime.projection&&camera==runtime.projection.GetComponent<Camera>()){nativeRenderedFrame=Time.frameCount;nativeRenderedCamera=camera.GetInstanceID();nativeRenderedArtwork=library.Loaded.Id;}}
  JObject NativePresentationObservation(){
   var camera=runtime.projection?runtime.projection.GetComponent<Camera>():null;
   return new JObject{
    ["eye"]=runtime.projection?new JArray(runtime.projection.EyeScreen.x,runtime.projection.EyeScreen.y,runtime.projection.EyeScreen.z):null,
    ["unity_frame"]=Time.frameCount,["rendered_frame"]=nativeRenderedFrame,["rendered_camera"]=nativeRenderedCamera,["rendered_artwork"]=nativeRenderedArtwork,
    ["pipeline"]=GraphicsSettings.currentRenderPipeline?GraphicsSettings.currentRenderPipeline.name:"built-in",
    ["default_pipeline"]=GraphicsSettings.defaultRenderPipeline?GraphicsSettings.defaultRenderPipeline.name:"built-in",
    ["quality_pipeline"]=QualitySettings.renderPipeline?QualitySettings.renderPipeline.name:"default",
    ["sdk_loaded"]=contentPresentation&&contentPresentation.HasContent,["sdk_static_held"]=contentPresentation&&contentPresentation.StaticHeld,
    ["cameras"]=new JArray(FindObjectsOfType<Camera>(true).Select(view=>new JObject{
     ["id"]=view.GetInstanceID(),["name"]=view.name,["selected"]=view==camera,["scene"]=view.gameObject.scene.path,
     ["enabled"]=view.enabled,["active"]=view.gameObject.activeInHierarchy,["target_texture"]=view.targetTexture?view.targetTexture.name:null,
     ["rect"]=new JArray(view.rect.x,view.rect.y,view.rect.width,view.rect.height),["pixels"]=new JArray(view.pixelWidth,view.pixelHeight),["depth"]=view.depth
    }))
   };
  }
  void RenderLog(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert||message.StartsWith("Shader ",StringComparison.Ordinal)&&message.Contains(": variant ")&&message.TrimEnd().EndsWith(" not found.",StringComparison.Ordinal))renderErrors++;}
  async Task<JObject> Execute(string type,JObject payload){
   if(type=="GetDesktopState")return Snapshot();if(!ready)throw new InvalidOperationException("desktop-initializing");
   if(type=="QuitDesktop"){
    if(quitting)return Result(true,null);
    DesktopLifetime.RequestShutdown(DataRoot);quitting=true;
    while(busy)await Frames(1);busy=true;
     try{await runtime.ReleaseTracking();await CloseNativeTargets();await CloseScene();await NativeBrokerLease.CloseAsync();if(contentPresentation){await contentPresentation.StopSandboxRuntimeAsync();if(!contentPresentation.SandboxResourcesReleased)throw new InvalidOperationException("desktop-content-cleanup-unproven");}active=null;activeProfile=null;native.Detach();StartCoroutine(QuitAfterResponse());return Result(true,null);}catch(Exception failure){quitting=false;error="退出清理失败，请重试："+failure.GetBaseException().Message;revision++;throw;}finally{busy=false;}
   }
   if(quitting){if(type=="GetDesktopTracking")return Snapshot();throw new InvalidOperationException("desktop-exiting");}
   if(type=="GetDesktopTracking"){
    managerSeen=Time.realtimeSinceStartup;managerConsumer=(bool?)payload["consume"]==true;runtime.SetCameraPreview((bool?)payload["preview"]==true);var state=Snapshot();state["tracking"]=DesktopTrackingState.Pack(runtime.CurrentRaw,QpcClock.Now);state["source_error"]=runtime.DesktopSourceError;
    return state;
   }
   if(type=="GetDesktopPreview"){var frame=runtime.TakeCameraPreview();return new JObject{["frame"]=frame==null?JValue.CreateNull():(JToken)new JObject{["stream_id"]=frame.Stream,["frame_id"]=frame.Frame.ToString(),["captured_at_us"]=frame.Captured.ToString(),["width"]=frame.Width,["height"]=frame.Height,["face_found"]=frame.FaceFound,["mirrored"]=true,["inference_ms"]=frame.InferenceMs,["ipd_px"]=frame.IpdPixels,["jpeg"]=Convert.ToBase64String(frame.Jpeg)}};}
   if(type=="EnumerateDesktopCameras")return new JObject{["cameras"]=await runtime.EnumerateCameras()};
   if(type=="DesktopCalibrationSelection"){runtime.ApplyDesktopSettings((string)payload["selected"],payload["applied"]?.Type==JTokenType.Object?payload["applied"].ToObject<CalibrationProfile>():null,(bool)payload["pause_when_invisible"]);return Snapshot();}
   // System reconciliation may be releasing the Tracker or restoring a scene.
   // Wait for that owner, rather than fail the user's first preview request.
   while(busy){await Frames(1);if(quitting)throw new InvalidOperationException("desktop-exiting");}busy=true;
   try{
    if(ContentCommand(type))return await ExecuteContent(type,payload);
    switch(type){
     case "ApplyDesktop":return await Apply(new WallpaperRequest((string)payload["artwork_id"],(string)payload["display_id"],(string)payload["preset_id"],(long)payload["preset_revision"]));
     case "StopDesktop":if(payload["display_id"]?.Type==JTokenType.String)return await StopAppliedTarget((string)payload["display_id"]);await CloseNativeTargets();await CloseScene();SaveApplied(null,null,null);foreach(var entry in ((JObject)contentService.Store.Snapshot["applied"]).Properties())contentService.Store.ClearApplied(entry.Name);active=null;activeProfile=null;activeContentRevision=null;policy.Manual=false;native.Detach();native.Hide();await SetPaused();revision++;error=null;return Result(true,null);
     case "PauseDesktop":await PauseAppliedTargets(payload);revision++;return Snapshot();
     case "BeginDesktopPreview":managerSeen=Time.realtimeSinceStartup;policy.Preview=true;PublishNativePoses();await CloseScene();await contentPresentation.RetireViewportAsync();native.Hide();revision++;return Snapshot();
     case "EndDesktopPreview":policy.Preview=false;if(active!=null&&!NativeSelected)await Present(active,activeProfile,activeContentRevision);PublishNativePoses();revision++;return Snapshot();
     case "ConfigureDesktopCamera":
      if(policy.Locked||policy.Suspended)throw new InvalidOperationException("会话锁定或休眠时无法启用摄像头");
      cameraId=(string)payload["camera_id"];cameraName=(string)payload["camera_name"];cameraMode=(string)payload["mode_id"];useSimulation=(bool?)payload["simulate"]==true;
      if(useSimulation&&cameraId!="simulation")throw new ArgumentException("invalid-simulation-camera");captureAuthorized=true;managerSeen=Time.realtimeSinceStartup;managerConsumer=true;
      await runtime.ReleaseTracking();await runtime.StartDesktopCamera(cameraId,cameraName,cameraMode,useSimulation);return Snapshot();
     default:throw new InvalidDataException("unsupported-desktop-command");
    }
   }finally{busy=false;}
  }
  IEnumerator QuitAfterResponse(){yield return new WaitForSecondsRealtime(.25f);Application.Quit();}
  JObject Result(bool success,string message){var value=Snapshot();value["succeeded"]=success;value["message"]=message;return value;}
  async Task<JObject> Apply(WallpaperRequest request){
   var previous=active;var profile=activeProfile;var previousContentRevision=activeContentRevision;bool previousNative=NativeSelected;DesktopAppliedSelection previousSelection=null;if(previous!=null)appliedStore?.Snapshot.Targets.TryGetValue(previous.DisplayId,out previousSelection);string phase="validate";Debug.Log("Desktop apply received: artwork="+request.ArtworkId);
   try{
    ArtworkCatalog.Find(request.ArtworkId);if(request.ArtworkId=="blueprint")throw new InvalidOperationException("请先保存蓝图作品再应用到桌面");if(!DesktopPlatform.Displays().Any(d=>d.Id==request.DisplayId))throw new InvalidOperationException("目标显示器未连接");
    var chosen=runtime.Store.List().SingleOrDefault(p=>p.id==request.CalibrationPresetId);if(chosen==null||chosen.schema_version!=2||chosen.manual==null||chosen.revision!=request.CalibrationPresetRevision||!chosen.Valid()||!chosen.manual.Bound||chosen.manual.display_id!=request.DisplayId)throw new InvalidOperationException("校准预设已失效、修订变化或与显示器不匹配");
    var source=runtime.CurrentDescriptor;if(source!=null&&!chosen.manual.Matches(source,request.DisplayId))throw new InvalidOperationException("校准预设与当前摄像头或采集模式不匹配");
    if(source==null&&runtime.CameraId!=null&&(string)chosen.manual.source["camera_id"]!=runtime.CameraId)throw new InvalidOperationException("校准预设与所选摄像头不匹配");
    if(UsesNative(ArtworkCatalog.Find(request.ArtworkId)))return await ApplyNative(request,chosen);
    InitializeAppliedStore();appliedStore.Snapshot.RequireRouting(request.DisplayId,false,NativeApplied);
    phase="present";policy.Preview=false;await CloseNative(request.DisplayId);await Present(request,chosen);if(!ArtworkCatalog.Find(request.ArtworkId).IsContent)ClearContentApplied();SaveApplied(request,chosen,activeContentRevision);active=request;activeProfile=chosen.Copy();error=null;revision++;Debug.Log("Desktop apply completed: artwork="+request.ArtworkId);return Result(true,null);
   }catch(Exception e){
    Debug.LogWarning("Desktop apply failed: artwork="+request.ArtworkId+" phase="+phase+" "+e);
    error=e.Message;if(phase=="validate"){revision++;return Result(false,error);}try{if(previousNative&&previousSelection!=null){await CloseScene();native.Detach();native.Hide();ClearContentApplied();nativePlaybacks[previous.DisplayId]=await OpenNative(previousSelection);activeContentRevision=previousContentRevision;}else if(previous!=null){await Present(previous,profile,previousContentRevision);if(!ArtworkCatalog.Find(previous.ArtworkId).IsContent)ClearContentApplied();}else{await CloseScene();ClearContentApplied();native.Detach();native.Hide();}}catch(Exception recovery){Debug.LogWarning("Desktop apply recovery failed: "+recovery);active=null;activeProfile=null;await CloseScene();native.Detach();native.Hide();error+="；恢复失败："+recovery.Message;}
    revision++;return Result(false,error);
   }
  }
  async Task Present(WallpaperRequest request,CalibrationProfile profile,string fixedContentRevision=null){
   await CloseScene();Time.timeScale=1;AudioListener.pause=false;int initialErrors=renderErrors;
   var display=DesktopPlatform.Displays().SingleOrDefault(d=>d.Id==request.DisplayId);if(display==null)throw new InvalidOperationException("desktop-display-offline");
   native.Attach(request.DisplayId);
   // Embedded Windows Players resize through their owned HWND (WM_SIZE), not
   // Screen.SetResolution, which attempts an unsupported DX mode switch here.
   await Frames(3);
   if(ArtworkCatalog.Find(request.ArtworkId).IsContent){await PresentContent(request,profile,fixedContentRevision);await SetPaused();return;}activeContentRevision=null;
   runtime.UseDesktopProfile(profile);library.Select(ArtworkCatalog.Find(request.ArtworkId));library.SelectTarget(request.DisplayId);library.OpenPreview(true);
   float deadline=Time.realtimeSinceStartup+20;while(library.Loading){if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("desktop-scene-load-timeout");await Frames(1);}
   if(library.Error!=null||library.Loaded==null)throw new InvalidOperationException(library.Error??"desktop-scene-unavailable");
   var camera=runtime.projection.GetComponent<Camera>();camera.targetTexture=null;camera.rect=new Rect(0,0,1,1);camera.enabled=true;
   await Frames(3);if(!native.Verify())native.Attach(request.DisplayId);Measure();await Frames(2);
   if(!native.Verify()||!runtime.ViewportValid||runtime.DisplayId!=request.DisplayId||renderErrors!=initialErrors)throw new InvalidOperationException("desktop-presentation-verification-failed");
   await SetPaused();
  }
  void Measure(){if(library.Loaded==null)return;var camera=runtime.projection.GetComponent<Camera>();if(DisplayViewport.TryMeasure(new Rect(0,0,Screen.width,Screen.height),runtime.DesktopRenderingProfile?.manual.display_width??.53,out var viewport,out var id)){runtime.SetViewport(viewport,id);library.Resize((float)viewport.Width,(float)viewport.Height);}else runtime.SetViewport(null,null);}
  async Task CloseScene(){
   if(contentPresentation){await contentPresentation.UnloadAsync();contentPresentation.ReleaseStatic();contentPaused=false;}
   if(runtime.projection)runtime.projection.GetComponent<Camera>().enabled=true;
   library.ClosePreview();float deadline=Time.realtimeSinceStartup+20;while(library.Loading||library.Loaded!=null){if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("desktop-scene-unload-timeout");await Frames(1);}
  }
  Task Frames(int count){var complete=new TaskCompletionSource<bool>();StartCoroutine(WaitFrames(count,complete));return complete.Task;}
  IEnumerator WaitFrames(int count,TaskCompletionSource<bool> complete){for(int i=0;i<count;i++)yield return new WaitForEndOfFrame();complete.TrySetResult(true);}
  async Task SetPaused(){
   bool paused=policy.Paused;
   await PauseContent(paused);
   if(library.Loaded!=null)runtime.projection.GetComponent<Camera>().enabled=!paused;Time.timeScale=paused?0:1;AudioListener.pause=paused;
   PublishNativePoses();bool capture=policy.CaptureNeeded(captureAuthorized,managerConsumer&&Time.realtimeSinceStartup-managerSeen<4,HasPlayback);
   if(!capture&&runtime.CameraStarted)await runtime.ReleaseTracking();else if(capture&&!runtime.CameraStarted)await runtime.StartDesktopCamera(cameraId,cameraName,cameraMode,useSimulation);
  }
  async void Update(){
   if(!ready||quitting)return;
   bool openRequested=false;
   while(events.TryDequeue(out var message)){
    if(message=="open"){openRequested=true;continue;}
    if(message=="pause"){if(HasPlayback)try{await Execute("PauseDesktop",new JObject{{"paused",!policy.Manual}});}catch(Exception failure){error=failure.GetBaseException().Message;revision++;}}else if(message=="locked")policy.Locked=true;else if(message=="unlocked")policy.Locked=false;else if(message=="suspended")policy.Suspended=true;else if(message=="resumed")policy.Suspended=false;else if(message.StartsWith("shell-error:")){error=message;revision++;}
    else if(message=="quit"||message=="stop"){try{await Execute(message=="quit"?"QuitDesktop":"StopDesktop",new JObject());}catch(Exception e){error=e.Message;Debug.LogError(e);}if(quitting)return;continue;}
    if(message=="locked"||message=="suspended"||message=="resumed")trackingResetRequested=true;
    else if(message=="display-changed"||message=="explorer-changed")nextCheck=0;
   }
   if(openRequested&&!quitting)DesktopManager.LaunchManager();
   if(busy||Time.realtimeSinceStartup<nextCheck)return;nextCheck=Time.realtimeSinceStartup+1;busy=true;
   try{
    if(trackingResetRequested){trackingResetRequested=false;await runtime.ReleaseTracking();}
    if(policy.Preview&&Time.realtimeSinceStartup-managerSeen>4){policy.Preview=false;if(active!=null&&!NativeSelected)await Present(active,activeProfile,activeContentRevision);}
    await ReconcileNativeTargets();
    if(active!=null&&!NativeSelected&&!policy.Preview){
     if(!DesktopPlatform.Displays().Any(d=>d.Id==active.DisplayId)){error="目标显示器离线，已停止空间壁纸";await CloseScene();ClearContentApplied();SaveApplied(null,null,null);active=null;activeProfile=null;activeContentRevision=null;native.Detach();native.Hide();revision++;}
     else{
      MeasureContentDesktop();if(contentPresentation.ViewportRecoveryRequired){await contentPresentation.RetireViewportAsync();await Present(active,activeProfile,activeContentRevision);revision++;}
      if(!native.Verify()){try{native.Attach(active.DisplayId);await Frames(3);Measure();if(runtime.DisplayId!=active.DisplayId)throw new InvalidOperationException("desktop-display-measurement-mismatch");revision++;}catch(Exception e){error=e.Message;await CloseScene();native.Detach();native.Hide();active=null;activeProfile=null;revision++;}}
     }
    }
    policy.PauseWhenCovered=runtime.PauseWhenInvisible;policy.Covered=nativePlaybacks.Count>0?nativePlaybacks.Values.All(p=>p.Target.Covered()):active!=null&&!NativeSelected&&native.Covered();await SetPaused();
   }catch(Exception e){error=e.Message;Debug.LogWarning(e);}finally{busy=false;}
  }
  void LateUpdate(){(shell as IDesktopShellState)?.SetWallpaperState(HasPlayback,policy.Manual);if(ready){PublishNativePoses();if(!busy&&library.Loaded!=null)Measure();}}
  void OnDestroy(){quitting=true;Application.logMessageReceived-=RenderLog;Camera.onPostRender-=ObserveNativeFrame;RenderPipelineManager.endCameraRendering-=ObserveNativeSrpFrame;try{native?.Dispose();shell?.Dispose();control?.Dispose();contentLease?.Dispose();}catch(Exception e){Debug.LogError(e);}Time.timeScale=1;AudioListener.pause=false;}
 }
}
