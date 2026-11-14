using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk;
using SpatialTracking;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
namespace SpatialWallpaper.Content {
 [DefaultExecutionOrder(10000)]public sealed class RuntimeContentPresentation:MonoBehaviour,ISandboxContentPresentation {
  public Camera DisplayCamera;readonly ContentLoader loader=new ContentLoader();ContentHandle handle;WindowProjection projection;Vector3 frame;Texture2D still;int renderErrors,loadErrors;
  NativeContentSurface native;bool sandboxLoaded,sandboxStatic,sandboxStopping;JObject sandboxReceipt;string sandboxBuild,sandboxWork,sandboxRevision,sandboxError;
  public bool PreserveWidth;
  public bool ExternalViewport,PreviewMode,FullParentViewport;
  public string ContentDataRoot;
  public Action<Vector2> ObserveDrag;
  public Action ObserveEscape;
  Rect externalRegion;bool externalMeasured;
  public void SetExternalViewport(Rect region,ViewportMetrics viewport,string display){
   if(region.width<1||region.height<1)throw new ArgumentException("content-viewport-empty");
   externalRegion=new Rect(Mathf.Round(region.x),Mathf.Round(region.y),Mathf.Round(region.width),Mathf.Round(region.height));DisplayId=display;externalMeasured=true;
   if(!projection)projection=DisplayCamera.GetComponent<WindowProjection>();projection.viewportManagedExternally=true;
   projection.displayWidth=(float)viewport.Width;projection.displayHeight=(float)viewport.Height;
   TargetPixels(out var targetWidth,out var targetHeight);metrics["target_pixels"]=new JArray(targetWidth,targetHeight);metrics["width"]=viewport.Width;metrics["height"]=viewport.Height;metrics["center_x"]=viewport.CenterX;metrics["center_y"]=viewport.CenterY;
  }
  public async Task RetireViewportAsync(){
   Exception prior=null;try{await UnloadAsync();ReleaseStatic();}catch(Exception failure){prior=failure;if(native==null||handle!=null&&!handle.Unloaded)throw;}
   if(native!=null){try{await native.StopAsync();}catch{if(!native.ResourcesReleased)throw;}if(!native.ResourcesReleased)throw new InvalidOperationException("content-viewport-cleanup-unproven");native.Dispose();native=null;}
   if(prior!=null)Debug.LogWarning("content-viewport-retired-after-failure:"+prior.GetBaseException().Message);
   sandboxLoaded=false;sandboxStatic=false;sandboxReceipt=null;unloadingContent=false;sandboxError=null;presentedX=presentedY=int.MinValue;
  }
  int presentedX=int.MinValue,presentedY=int.MinValue;
  async Task ObserveExternalInteraction(NativeContentSurface selected,long current){
   try{
    if(PreviewMode){var input=await selected.CallAsync("ReadInteraction",new JObject());if(current!=sandboxEpoch)return;ObserveDrag?.Invoke(new Vector2((float)input["dx"],(float)input["dy"]));if((bool)input["escape"])ObserveEscape?.Invoke();}
    if(current!=sandboxEpoch||!sandboxLoaded)return;
    int x=(int)externalRegion.x,y=Screen.height-(int)externalRegion.yMax;
    if(ExternalViewport&&!FullParentViewport&&(x!=presentedX||y!=presentedY)){await selected.CallAsync("SetRegion",new JObject{{"x",x},{"y",y}});if(current!=sandboxEpoch)return;presentedX=x;presentedY=y;}
    var eye=projection.EyeScreen;if((eye-requestedEye).sqrMagnitude>=1e-10f||PreserveWidth!=requestedFit)await ConfigureSandboxEye(selected,eye,PreserveWidth,current);
   }catch(Exception failure){if(current==sandboxEpoch){sandboxError=failure.GetBaseException().Message;Ready=false;}}
  }
  JObject selectedAudio;
  public Func<string,SpatialWallpaper.Sdk.Capabilities.SoundMix> SoundForWork;
  public string SoundError{get;private set;}
  public async Task SetSoundMixAsync(string work,SpatialWallpaper.Sdk.Capabilities.SoundMix mix){if(sandboxLoaded&&sandboxWork==work&&native!=null&&!sandboxStopping){var result=await native.CallAsync("SetSoundMix",new JObject{{"music",mix.MusicGain},{"effects",mix.EffectsGain}});SoundError=(string)result["sound_error"];}}
  JObject frameStatistics;
  public async Task RefreshSoundStateAsync(){if(sandboxLoaded&&Ready&&native!=null&&!sandboxStopping){var result=await native.CallReadyAsync("State",new JObject());SoundError=(string)result["sound_error"];frameStatistics=result["frame_statistics"] as JObject;}}
  public JObject SelectedAudioSource=>selectedAudio==null?null:(JObject)selectedAudio.DeepClone();
  public void RestoreAudioSource(JToken source){if(native!=null)throw new InvalidOperationException("audio-source-restore-after-start");selectedAudio=SpatialWallpaper.Sdk.Hosting.TrustedAudioSource.Validate(source);}
  public Task ConfigureAudioSourceAsync(JToken source)=>SetConfiguredAudioSourceAsync(SpatialWallpaper.Sdk.Hosting.TrustedAudioSource.RequireLive(source));
  public Task RestoreAudioSourceAfterFailureAsync(JToken source)=>SetConfiguredAudioSourceAsync(SpatialWallpaper.Sdk.Hosting.TrustedAudioSource.Validate(source));
  async Task SetConfiguredAudioSourceAsync(JObject next){RequireAvailable();if(HasContent)throw new InvalidOperationException("unload-before-changing-audio");if(native!=null){await native.StopAsync();if(!native.ResourcesReleased)throw new InvalidOperationException("audio-source-old-runtime-still-owned");native.Dispose();native=null;}selectedAudio=next;sandboxError=null;}

  TrackingEyeDriver Tracking=>DisplayCamera?DisplayCamera.GetComponent<TrackingEyeDriver>():null;
  public bool CameraStarted=>Tracking&&Tracking.CameraStarted;
  public JObject TrackingState=>Tracking?Tracking.Snapshot:new JObject{{"state","OFF"},{"source_mode","off"},{"camera_started",false}};
  bool RefreshTrackedViewport(){if(ExternalViewport)return externalMeasured;var tracker=Tracking;if(!tracker||!tracker.OwnsEye)return true;if(!DisplayViewport.TryMeasure(DisplayCamera.pixelRect,.53,out var viewport,out var display)||display!=DisplayId)return false;tracker.SetViewportCenter(new Vector3((float)viewport.CenterX,(float)viewport.CenterY,0));metrics["center_x"]=viewport.CenterX;metrics["center_y"]=viewport.CenterY;return projection.SetEye(tracker.EyeInView);}
  Task sandboxEyeUpdate=Task.CompletedTask,firstFrameTask=Task.CompletedTask,unloadingTask=Task.CompletedTask;CancellationTokenSource firstFrameCancellation;bool unloadingContent;long sandboxEpoch,requestedProjection,projectionAcknowledgedAt;Vector3 requestedEye,presentedEye;bool requestedFit;
  async Task CompleteFirstFrameAsync(Func<Task> operation,TaskCompletionSource<bool> completion,CancellationTokenSource owner){try{await CompleteOwnedOperation(operation,completion);}finally{if(ReferenceEquals(firstFrameCancellation,owner))firstFrameCancellation=null;owner.Dispose();}}
  static async Task CompleteOwnedOperation(Func<Task> operation,TaskCompletionSource<bool> completion){try{await operation();completion.TrySetResult(true);}catch(OperationCanceledException){completion.TrySetCanceled();}catch(Exception failure){completion.TrySetException(failure);}}
  void RequireAvailable(){if(sandboxStopping||unloadingContent)throw new OperationCanceledException("content-player-transition-in-progress");if(!firstFrameTask.IsCompleted)throw new InvalidOperationException("content-first-frame-still-owned");}
  async Task ConfigureSandboxEye(NativeContentSurface selected,Vector3 eye,bool fit,long current){try{var reply=await selected.CallAsync("SetEye",new JObject{{"eye",new JArray(eye.x,eye.y,eye.z)},{"preserve_width",fit}});if(current!=sandboxEpoch)throw new OperationCanceledException("content-native-projection-generation-ended");JsonData.Fields(reply,"projection_revision");JsonData.Require(reply["projection_revision"]?.Type==JTokenType.Integer,"content-native-projection-ack");requestedProjection=(long)reply["projection_revision"];if(requestedProjection<1)throw new InvalidOperationException("content-native-projection-ack");requestedEye=eye;requestedFit=fit;projectionAcknowledgedAt=System.Diagnostics.Stopwatch.GetTimestamp();}catch(Exception failure){if(current==sandboxEpoch){sandboxError=failure.GetBaseException().Message;Ready=false;}throw;}}
  async Task ObserveSandboxEye(NativeContentSurface selected,Vector3 eye,bool fit,long current){try{await ConfigureSandboxEye(selected,eye,fit,current);}catch{}}
  void UpdateSandboxEye(){var shown=native.ObservedReceipt;if(shown!=null&&sandboxReceipt!=null&&(string)shown["attempt"]==(string)sandboxReceipt["attempt"]&&(string)shown["instance"]==(string)sandboxReceipt["instance"]&&(long)shown["generation"]==(long)sandboxReceipt["generation"]&&(string)shown["build"]==sandboxBuild&&(string)shown["work_id"]==sandboxWork&&(string)shown["revision_id"]==sandboxRevision&&(long)shown["sequence"]>=(long)sandboxReceipt["sequence"]&&(long)shown["projection_revision"]>=(long)sandboxReceipt["projection_revision"])sandboxReceipt=shown;if(sandboxReceipt!=null&&(long)sandboxReceipt["projection_revision"]==requestedProjection)presentedEye=requestedEye;if(!sandboxEyeUpdate.IsCompleted)return;if(sandboxReceipt==null||(long)sandboxReceipt["projection_revision"]<requestedProjection){if(projectionAcknowledgedAt!=0&&(System.Diagnostics.Stopwatch.GetTimestamp()-projectionAcknowledgedAt)*1000/System.Diagnostics.Stopwatch.Frequency>5000){sandboxError="content-native-projection-not-presented";Ready=false;}return;}if(ExternalViewport){sandboxEyeUpdate=ObserveExternalInteraction(native,sandboxEpoch);return;}var eye=projection.EyeScreen;if((eye-requestedEye).sqrMagnitude<1e-10f&&PreserveWidth==requestedFit)return;sandboxEyeUpdate=ObserveSandboxEye(native,eye,PreserveWidth,sandboxEpoch);}
  public string DisplayId{get;private set;}public bool HasContent=>sandboxLoaded||handle!=null&&!handle.Unloaded;public bool Ready{get;private set;}public int RenderErrors=>renderErrors;public bool StaticHeld=>still||sandboxStatic;JObject metrics=new JObject();float? visualTime;
  public JObject Metrics{get{
   var result=(JObject)metrics.DeepClone();
   result["frame_statistics"]=sandboxLoaded?frameStatistics?.DeepClone():null;
   if(sandboxLoaded)result["eye"]=new JArray(presentedEye.x,presentedEye.y,presentedEye.z);
   result["sandbox_receipt"]=sandboxReceipt?.DeepClone();result["sandbox_error"]=sandboxError;
   result["llm"]=sandboxLoaded?"deterministic-local-simulation-external-unverified":null;
   if(DisplayCamera){
    var color=DisplayCamera.backgroundColor;result["background"]=new JArray(color.r,color.g,color.b,color.a);
    var p=DisplayCamera.GetComponent<WindowProjection>();if(p)result["content_depth"]=p.contentDepth;
    var pipeline=GraphicsSettings.currentRenderPipeline as UniversalRenderPipelineAsset;
    var data=pipeline?DisplayCamera.GetComponent<UniversalAdditionalCameraData>():null;
    result["camera_settings"]=new JObject{
     ["hdr"]=DisplayCamera.allowHDR,
     ["msaa"]=DisplayCamera.allowMSAA?(pipeline?pipeline.msaaSampleCount:Mathf.Max(1,QualitySettings.antiAliasing)):1,
     ["opaque"]=pipeline&&(data?data.requiresColorTexture:pipeline.supportsCameraOpaqueTexture),
     ["depth"]=pipeline&&(data?data.requiresDepthTexture:pipeline.supportsCameraDepthTexture),
     ["post"]=data&&data.renderPostProcessing,
     ["renderer"]=pipeline?(Shader.IsKeywordEnabled("_FORWARD_PLUS")?"forward-plus":"forward"):"built-in"};
   }
   return result;
  }}
  void Awake(){Application.logMessageReceivedThreaded+=Log;Application.runInBackground=true;string time=ContentArguments.Argument("--content-visual-time");if(time!=null){float value;if(!float.TryParse(time,System.Globalization.NumberStyles.Float,System.Globalization.CultureInfo.InvariantCulture,out value)||float.IsNaN(value)||float.IsInfinity(value)||value<0||value>86400)throw new ArgumentException("invalid-visual-time");visualTime=value;}}
  void Log(string condition,string stack,LogType type){
   // Unity 2022.3 reports strict variant misses as ordinary log messages.
   // Their error-shader pixels must fail the same first-frame gate as errors.
   bool missingVariant=condition.StartsWith("Shader ",StringComparison.Ordinal)&&condition.Contains(": variant ")&&condition.TrimEnd().EndsWith(" not found.",StringComparison.Ordinal);
   if(type==LogType.Error||type==LogType.Assert||type==LogType.Exception||missingVariant)Interlocked.Increment(ref renderErrors);
  }
  NativeContentSurface Native(){RequireAvailable();if(sandboxStopping)throw new OperationCanceledException("content-player-shutting-down");if(native!=null&&!native.ResourcesReleased)return native;if(!sandboxEyeUpdate.IsCompleted)throw new InvalidOperationException("content-native-projection-still-owned");if(!Measure())throw new InvalidOperationException("viewport-unavailable");native?.Dispose();string data=ContentDataRoot??ContentArguments.Argument("--content-data-root")??System.IO.Path.Combine(RuntimePaths.ApplicationData,"SpatialWallpaper","Content");string root=ContentArguments.Argument("--sdk-runtime")??System.IO.Path.GetFullPath(System.IO.Path.Combine(Application.dataPath,"../sdk-runtime"));string attempts=ContentArguments.Argument("--sdk-attempts")??System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"SpatialWallpaper","SDK","attempts");TargetPixels(out var targetWidth,out var targetHeight);native=new NativeContentSurface(root,System.IO.Path.Combine(data,"sdk-runtime"),attempts,targetWidth,targetHeight,(float)metrics["width"],(float)metrics["height"],selectedAudio,ExternalViewport&&!FullParentViewport?(int)externalRegion.x:0,ExternalViewport&&!FullParentViewport?Screen.height-(int)externalRegion.yMax:0,PreviewMode);return native;}
  // An explicit user operation may replace a failed generation only after its
  // original owner proves cleanup. Never retain a stale receipt as a still.
  async Task RetireFailedNativeAsync(){if(native==null||native.ResourcesReleased)return;string failure=native.ObserveFailure()??(sandboxLoaded?sandboxError:null);if(failure==null)return;if(sandboxLoaded)Ready=false;sandboxError=failure;Debug.LogWarning("content-native-retiring:"+failure);try{await native.StopAsync();}catch{if(!native.ResourcesReleased)throw;}sandboxLoaded=false;sandboxStatic=false;sandboxReceipt=null;}
  public async Task<SpatialWallpaper.Sdk.Hosting.RuntimePackageInspection> InspectSandboxAsync(string path){RequireAvailable();await RetireFailedNativeAsync();return await Native().InspectAsync(path);}
  public async Task UninstallInstallationAsync(Guid installation){RequireAvailable();await RetireFailedNativeAsync();var result=await Native().CallAsync("UninstallInstallation",new JObject{{"installation",installation.ToString("N")}});JsonData.Require((bool?)result["storage_deleted"]==true,"content-uninstall-unproven");}
  public bool SandboxResourcesReleased=>(native==null||native.ResourcesReleased)&&sandboxEyeUpdate.IsCompleted&&firstFrameTask.IsCompleted&&unloadingTask.IsCompleted;
  public bool ViewportRecoveryRequired=>!sandboxStopping&&sandboxLoaded&&native!=null&&!NativeGeometryMatches;
  void TargetPixels(out int width,out int height){if(ExternalViewport&&!FullParentViewport){width=(int)externalRegion.width;height=(int)externalRegion.height;}else NativeContentSurface.ParentPixels(out width,out height);}
  bool NativeGeometryMatches{get{if(native==null)return true;TargetPixels(out var width,out var height);return native.MatchesGeometry(width,height,(float)metrics["width"],(float)metrics["height"]);}}
  public async Task StopSandboxRuntimeAsync(){sandboxStopping=true;Ready=false;sandboxEpoch++;firstFrameCancellation?.Cancel();if(native!=null)await native.StopAsync();try{await firstFrameTask;}catch{}try{await sandboxEyeUpdate;}catch{}try{await unloadingTask;}catch{}}
  public async Task HoldStaticAsync(){RequireAvailable();try{await sandboxEyeUpdate;}catch{}await RetireFailedNativeAsync();if(sandboxLoaded){var held=await Native().CallAsync("HoldStatic",new JObject());sandboxStatic=(bool)held["holding_static"];return;}using(var timeout=new CancellationTokenSource(5000))await EndFrame(timeout.Token);if(!HasContent)return;var captured=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);captured.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);captured.Apply();if(still)Destroy(still);still=captured;}
  public Task UnloadAsync(){if(!unloadingTask.IsCompleted)return unloadingTask;unloadingContent=true;Ready=false;sandboxEpoch++;firstFrameCancellation?.Cancel();var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);unloadingTask=completion.Task;_ = CompleteOwnedOperation(UnloadCoreAsync,completion);return unloadingTask;}
  async Task UnloadCoreAsync(){try{await firstFrameTask;}catch{}try{await sandboxEyeUpdate;}catch{}if(!sandboxStopping)await RetireFailedNativeAsync();if(native!=null&&!native.ResourcesReleased&&!sandboxStopping){var state=await native.CallAsync("Unload",new JObject());JsonData.Require((bool)state["runtime_resources_released"],"content-native-unload-unproven");sandboxStatic=(bool)state["holding_static"];}sandboxLoaded=false;sandboxReceipt=null;var old=handle;if(old!=null)await loader.UnloadAsync(old);handle=null;unloadingContent=false;}
  public async Task LoadSandboxAsync(SandboxContentBuild package,JObject revision,CancellationToken cancellation){RequireAvailable();cancellation.ThrowIfCancellationRequested();if(!NativeGeometryMatches)await RetireViewportAsync();Ready=false;if(handle!=null||sandboxLoaded)throw new InvalidOperationException("content-must-unload-before-load");sandboxError=null;sandboxBuild=package.BuildId;sandboxWork=package.Installation.ToString("D");sandboxRevision=package.RevisionId;var mix=SoundForWork?.Invoke(sandboxWork)??new SpatialWallpaper.Sdk.Capabilities.SoundMix();await Native().CallAsync("SetSoundMix",new JObject{{"music",mix.MusicGain},{"effects",mix.EffectsGain}});SoundError=null;await Native().CallAsync("Load",new JObject{{"package",package.Path},{"build",sandboxBuild},{"archive_sha256",package.ArchiveSha256},{"work",sandboxWork},{"revision",sandboxRevision}});cancellation.ThrowIfCancellationRequested();long current=++sandboxEpoch;sandboxEyeUpdate=ConfigureSandboxEye(native,projection.EyeScreen,PreserveWidth,current);await sandboxEyeUpdate;cancellation.ThrowIfCancellationRequested();if(current!=sandboxEpoch||sandboxStopping)throw new OperationCanceledException("content-native-load-generation-ended");sandboxLoaded=true;}
  public async Task LoadAsync(ValidatedPackage package,JObject revision,CancellationToken cancellation){
   RequireAvailable();Ready=false;projection=DisplayCamera.GetComponent<WindowProjection>();if(!projection)projection=DisplayCamera.gameObject.AddComponent<WindowProjection>();projection.viewportManagedExternally=true;
   var a=package.Scene["frame"];frame=new Vector3((float)a[0],(float)a[1],(float)a[2]);
   if(!DisplayViewport.TryMeasure(DisplayCamera.pixelRect,.53,out var viewport,out var display))throw new InvalidOperationException("viewport-unavailable");DisplayId=display;loadErrors=renderErrors;
   handle=await loader.LoadAsync(new SceneLoadRequest{Package=package,Camera=DisplayCamera,ViewportWidth=viewport.Width,ViewportHeight=viewport.Height},cancellation);if(!Measure())throw new InvalidOperationException("projection-context-rejected");
  }
  public Task FirstFrameAsync(CancellationToken cancellation){cancellation.ThrowIfCancellationRequested();RequireAvailable();if(!HasContent)throw new InvalidOperationException("content-first-frame-without-content");var selected=native;var selectedHandle=handle;bool modern=sandboxLoaded;long epoch=sandboxEpoch,revision=requestedProjection;string build=sandboxBuild,work=sandboxWork,contentRevision=sandboxRevision;Vector3 eye=requestedEye;var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);firstFrameTask=completion.Task;var owner=CancellationTokenSource.CreateLinkedTokenSource(cancellation);firstFrameCancellation=owner;_ = CompleteFirstFrameAsync(()=>FirstFrameCoreAsync(selected,selectedHandle,modern,epoch,revision,build,work,contentRevision,eye,owner.Token),completion,owner);return firstFrameTask;}
  void RequireFirstFrameOwner(NativeContentSurface selected,ContentHandle selectedHandle,bool modern,long epoch,CancellationToken cancellation){cancellation.ThrowIfCancellationRequested();if(epoch!=sandboxEpoch||sandboxStopping||unloadingContent||(modern?(!sandboxLoaded||!ReferenceEquals(native,selected)):(!ReferenceEquals(handle,selectedHandle)||selectedHandle==null||selectedHandle.Unloaded)))throw new OperationCanceledException("content-first-frame-generation-ended");}
  async Task FirstFrameCoreAsync(NativeContentSurface selected,ContentHandle selectedHandle,bool modern,long epoch,long revision,string build,string work,string contentRevision,Vector3 eye,CancellationToken cancellation){RequireFirstFrameOwner(selected,selectedHandle,modern,epoch,cancellation);if(modern){var shown=await selected.CallAsync("FirstFrame",new JObject());RequireFirstFrameOwner(selected,selectedHandle,modern,epoch,cancellation);var receipt=shown["receipt"] as JObject;JsonData.Require(receipt!=null&&(string)receipt["build"]==build&&(string)receipt["work_id"]==work&&(string)receipt["revision_id"]==contentRevision&&(long)receipt["projection_revision"]==revision,"content-native-first-frame-identity");sandboxReceipt=receipt;presentedEye=eye;SoundError=(string)shown["sound_error"];Ready=true;return;}await EndFrame(cancellation);await EndFrame(cancellation);RequireFirstFrameOwner(selected,selectedHandle,modern,epoch,cancellation);if(renderErrors!=loadErrors)throw new InvalidOperationException("content-render-error");Ready=true;string capture=ContentArguments.Argument("--content-frame-output");if(capture!=null){System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(capture)));ScreenCapture.CaptureScreenshot(capture);}}
  Task EndFrame(CancellationToken cancellation){var complete=new TaskCompletionSource<bool>();var registration=cancellation.Register(()=>complete.TrySetCanceled());complete.Task.ContinueWith(_=>registration.Dispose(),TaskScheduler.Default);StartCoroutine(Frame(complete,cancellation));return complete.Task;}
  IEnumerator Frame(TaskCompletionSource<bool> complete,CancellationToken cancellation){yield return new WaitForEndOfFrame();if(cancellation.IsCancellationRequested)complete.TrySetCanceled();else complete.TrySetResult(true);}
  public bool Measure(){if(!DisplayCamera)return false;if(ExternalViewport)return externalMeasured;if(!projection){projection=DisplayCamera.GetComponent<WindowProjection>();if(!projection)projection=DisplayCamera.gameObject.AddComponent<WindowProjection>();projection.viewportManagedExternally=true;}if(!DisplayViewport.TryMeasure(DisplayCamera.pixelRect,.53,out var viewport,out var display))return false;DisplayId=display;projection.displayWidth=(float)viewport.Width;projection.displayHeight=(float)viewport.Height;if(handle!=null&&!handle.Unloaded){float scale=(float)Framing.Scale(frame.x,frame.y,viewport.Width,viewport.Height,true);handle.Root.transform.localScale=Vector3.one*scale;projection.contentDepth=handle.ContentDepth*scale;}var tracker=Tracking;if(tracker&&tracker.OwnsEye)tracker.SetViewportCenter(new Vector3((float)viewport.CenterX,(float)viewport.CenterY,0));var tracked=tracker&&tracker.OwnsEye?(Vector3?)tracker.EyeInView:null;var eye=tracked.HasValue?new[]{(double)tracked.Value.x,(double)tracked.Value.y,(double)tracked.Value.z}:viewport.ToView(new[]{0.0,0,-.67});metrics=new JObject{["width"]=viewport.Width,["height"]=viewport.Height,["center_x"]=viewport.CenterX,["center_y"]=viewport.CenterY,["scale"]=handle!=null&&!handle.Unloaded?handle.Root.transform.localScale.x:1,["eye"]=new JArray(eye),["pixels"]=new JArray(Screen.width,Screen.height),["fixed_time"]=visualTime.HasValue?(JToken)visualTime.Value:JValue.CreateNull(),["lightmaps"]=LightmapSettings.lightmaps.Length};return projection.SetEye(new Vector3((float)eye[0],(float)eye[1],(float)eye[2]));}
  void LateUpdate(){if(!sandboxStopping&&!unloadingContent&&sandboxLoaded&&Ready){sandboxError=RefreshTrackedViewport()?native.ObserveFailure():"content-tracking-viewport-unavailable-or-display-changed";if(sandboxError!=null){Ready=false;Debug.LogWarning("content-native-observation:"+sandboxError+":"+native.StatusDiagnostic);}else{UpdateSandboxEye();Ready=sandboxError==null&&sandboxReceipt!=null;}}if(handle!=null&&!handle.Unloaded){Measure();if(visualTime.HasValue)foreach(var signal in handle.Root.GetComponentsInChildren<SignalBindings>())signal.Apply(visualTime.Value);}}
  public void ReleaseStatic(){if(native!=null&&!native.ResourcesReleased&&!sandboxStopping)native.CallReadyAsync("ReleaseStatic",new JObject()).GetAwaiter().GetResult();sandboxStatic=false;if(still)Destroy(still);still=null;}
  void OnGUI(){if(still)GUI.DrawTexture(new Rect(0,0,Screen.width,Screen.height),still,ScaleMode.StretchToFill,false);}
  async Task ReleaseDestroyedRuntime(){try{if(native!=null)await native.StopAsync();try{await firstFrameTask;}catch{}try{await sandboxEyeUpdate;}catch{}try{await unloadingTask;}catch{}}catch(Exception failure){Debug.LogError("destroyed-content-runtime-cleanup-unproven:"+failure.GetBaseException().Message);}}
  void OnDestroy(){Application.logMessageReceivedThreaded-=Log;sandboxStopping=true;Ready=false;sandboxEpoch++;firstFrameCancellation?.Cancel();_ = ReleaseDestroyedRuntime();if(still)Destroy(still);still=null;}
 }
}
