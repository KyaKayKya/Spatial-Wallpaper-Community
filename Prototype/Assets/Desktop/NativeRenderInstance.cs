using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;
using SpatialWallpaper.Library;
using SpatialWallpaper.Content;
using SpatialWallpaper.Sdk;
using System.Security.Cryptography;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Sdk.Native;
using SpatialWallpaper.Sdk.Native.Unity;
using SpatialWallpaper.Sdk.Native.Windows;
using SpatialWallpaper.Sdk.Editor.Native;
using SpatialWallpaper.Sdk.Capabilities;

namespace SpatialWallpaper.Desktop {
 // A render-only child. DesktopRuntime remains the only desktop/Tracker/tray
 // authority. All pose values arrive from that owner, never from author code.
 public sealed class NativeRenderInstance:MonoBehaviour {
  readonly object gate=new object();JObject configuration,pose,state=new JObject{{"ready",false}};
  WindowProjection projection;Transform content;Vector3 authoredScale;float authoredDepth,authorFactor=1;Scene scene;
  NativeScriptSession script;UnitySceneCommandTarget target;INativeInputSession nativeInput;JObject inputSpecification;Thread controller;volatile bool closing;bool quitting,exitAllowed;string failure;int renderErrors;
  readonly DesktopTrackingState tracking=new DesktopTrackingState();
  [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
  IntPtr renderWindow;uint renderThread;long processCreation,inputPauseRevision;
  bool nativeDiagnostics;int renderedFrame=-1,renderedCamera,renderedWidth,renderedHeight;
  int lastGc0,lastGc1,lastGc2,initialQueuedFrames;long lastGcTime;
  uint presentationNumerator,presentationDenominator;int presentationSync;bool presentationConfigured;
  WindowsPreviewInput previewCollector;NativePreviewInteractionTarget previewInteraction;int[] previewRegion;bool previewEnabled;
  SceneObject[] scriptStatusNodes;string scriptStatusJson;
  NativeSoundBus sound;JToken appliedSound;
  SpatialWallpaper.Sdk.Execution.OwnedDomain resourceOwner;NativeDynamicResources dynamicResources;long resourceAuthority=1;bool resourcePaused,resourceFault;ulong resourceInputEpoch;
  NativePrefabPool prefabs;IEnumerator resourcePreparation;Action beginScript;
  ExternalNativeScene externalScene;Task externalLoading;CancellationTokenSource externalCancellation;bool externalConstructionFailed;Vector3 externalFrame;
  public SpatialTracking.RawTrackingSample CurrentRaw=>tracking.Pipeline.RawLatest;
  public bool DesktopTarget=>(bool?)configuration?["desktop"]==true;
  public string DisplayId{get{lock(gate)return (string)pose?["display_id"];}}
  public void Initialize(string path){
   try{
    var file=new FileInfo(path);if(file.Length<1||file.Length>16384||(file.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("native-render-config");
    configuration=NativeScriptControl.Parse(File.ReadAllText(path));if((int?)configuration["version"]!=1)throw new InvalidDataException("native-render-version");
    pose=(JObject)configuration["pose"].DeepClone();Application.runInBackground=true;AudioListener.pause=true;
    // Match the original entry's AdaptiveRenderCadence base policy even for
    // scenes such as Mita without their own cadence component. NativeEntry
    // must not silently inherit the QualitySettings vSync=1 fallback.
    QualitySettings.vSyncCount=0;Application.targetFrameRate=60;UnityEngine.Rendering.OnDemandRendering.renderFrameInterval=1;
    initialQueuedFrames=QualitySettings.maxQueuedFrames;
    renderThread=GetCurrentThreadId();using(var owner=PinnedProcessOwner.PinCurrent())processCreation=owner.CreationFileTime;
    Application.wantsToQuit+=WantsToQuit;
    Application.logMessageReceived+=RenderLog;
    nativeDiagnostics=(bool?)configuration["diagnostics"]==true;
    if(nativeDiagnostics||(bool?)configuration["preview_ui"]==true){Camera.onPostRender+=ObserveFrame;UnityEngine.Rendering.RenderPipelineManager.endCameraRendering+=ObserveSrpFrame;}
    state["nonce"]=configuration["nonce"];controller=new Thread(Control){IsBackground=true,Name="SpatialWallpaper native instance control"};controller.Start();
   }catch(Exception error){failure=error.Message;closing=true;}
  }
  IEnumerator Start(){
   if(configuration==null){Application.Quit(1);yield break;}
   if(configuration["external_package"]!=null){yield return StartExternal();yield break;}
   var artwork=ArtworkCatalog.Builtins.SingleOrDefault(a=>a.Id==(string)configuration["artwork"]);
   if(artwork==null){failure="native-builtin-not-found";closing=true;yield break;}
   var loading=SceneManager.LoadSceneAsync(artwork.ScenePath,LoadSceneMode.Single);yield return loading;scene=SceneManager.GetSceneByPath(artwork.ScenePath);
   var specification=configuration["script"] as JObject;NativeSceneLibrary library=null;
   if(specification?["library_digest"]!=null){var preparing=UnityEngine.Resources.LoadAsync<NativeSceneLibrary>(NativeSceneLibrary.ResourcePath(artwork.Id));yield return preparing;library=preparing.asset as NativeSceneLibrary;if(!library){failure="native-scene-library-missing";closing=true;yield break;}}
   try{
    var objects=scene.GetRootGameObjects();projection=objects.SelectMany(r=>r.GetComponentsInChildren<WindowProjection>(true)).Single();
    content=objects.SelectMany(r=>r.GetComponentsInChildren<Transform>(true)).Single(t=>t.name=="SceneModel"||t.name=="PoolContent"||t.name=="LimpoolContent");
    authoredScale=content.localScale;authoredDepth=projection.contentDepth;projection.viewportManagedExternally=true;ApplyPose(pose);
    if(nativeDiagnostics&&configuration["diagnostic_vsync"]!=null){int sync=(int)configuration["diagnostic_vsync"];if(sync<0||sync>1)throw new InvalidDataException("native-diagnostic-vsync");QualitySettings.vSyncCount=sync;}
    if(nativeDiagnostics&&configuration["diagnostic_queued_frames"]!=null){int queued=(int)configuration["diagnostic_queued_frames"];if(queued<1||queued>2)throw new InvalidDataException("native-diagnostic-queued-frames");QualitySettings.maxQueuedFrames=queued;}
    if(specification!=null){
     var paths=(JArray)specification["node_paths"];if(paths.Count<1||paths.Count>SceneCommandCatalog.MaxObjects)throw new InvalidDataException("native-script-node-paths");
     var nodes=paths.Select(v=>ResolveScriptNode(scene,content,(string)v)).ToArray();
     var identity=new SessionIdentity(Guid.ParseExact((string)configuration["session"],"N"),(uint)configuration["generation"],SceneCommandCatalog.Digest);
     if(library)library.Require((string)specification["library_digest"]);
     var assets=new NativeAssetCatalog((specification["resources"] as JArray??new JArray()).Select(value=>value["asset_key"]==null?ResolveScriptAsset(scene,content,(JObject)value):library==null?throw new InvalidDataException("native-script-library-required"):library.Resolve(artwork.Id,(string)value["asset_key"],(string)value["kind"],(string)value["source_sha256"],(string)value["name"])));BindScript(identity,nodes,assets,specification);

    }
    inputSpecification=configuration["input"] as JObject;if(inputSpecification!=null&&beginScript==null)throw new InvalidDataException("native-input-requires-script");
   }catch(Exception error){failure=error.GetType().Name+":"+error.Message;Debug.LogError("native-render-start:"+failure);closing=true;}
  }
  void BindScript(SessionIdentity identity,Transform[] nodes,NativeAssetCatalog assets,JObject specification){
   assets.Bind(nodes.Length+1);sound=new NativeSoundBus(transform);ApplySound(pose);prefabs=new NativePrefabPool(assets,transform);
   if(resourceOwner==null)resourceOwner=new SpatialWallpaper.Sdk.Execution.OwnedDomain(identity.Instance,identity.Generation);if(NativeDynamicResources.Supported)dynamicResources=new NativeDynamicResources(resourceOwner,authorityEpoch:1);
     target=new UnitySceneCommandTarget(identity,nodes,assets,sound,prefabs,dynamicResources:dynamicResources);var launch=(JObject)specification["launch"].DeepClone();launch["session"]=identity.Instance.ToString("N");launch["generation"]=identity.Generation;
     resourcePreparation=PreparePrefabs(assets,specification["prewarm"] as JObject);
     beginScript=()=>script=new NativeScriptSession(identity,target,()=>new ScriptProcessClient((string)specification["helper"],(string)specification["helper_sha256"],(string)specification["bootstrap_cache"],launch),Enumerable.Range(1,nodes.Length).Select(i=>(long)i).ToArray(),assets.Bindings,(string)launch["build"],configuration["input"]==null?(Func<SpatialWallpaper.Sdk.Capabilities.InputAuthority>)null:()=>nativeInput==null?default(SpatialWallpaper.Sdk.Capabilities.InputAuthority):nativeInput.ReadAuthority(),new NativeViewSnapshot(projection.EyeScreen.x/authorFactor,projection.EyeScreen.y/authorFactor,projection.EyeScreen.z/authorFactor,projection.displayWidth/authorFactor,projection.displayHeight/authorFactor));
  }
  IEnumerator StartExternal(){
   var specification=configuration["script"] as JObject;WindowProjection externalProjection=null;
   try{
    var source=(JObject)configuration["external_package"];string path=(string)source["path"];
    if((string)configuration["artwork"]!=NativeInstalledPackage.ExternalSceneId||specification==null||!Path.IsPathRooted(path)||Path.GetFullPath(path)!=path)throw new InvalidDataException("external-native-render-config");
    NativeExternalPackage package;
    using(var input=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
     string digest;using(var sha=SHA256.Create())digest=BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();
     if(digest!=(string)source["sha256"])throw new InvalidDataException("external-native-archive-changed");
     package=NativeExternalPackage.Validate(path);
    }
    if(package.BuildId!=(string)source["build_id"]||package.WorkId!=(string)source["work_id"]||package.Revision!=(string)source["revision"]||PackageValidator.Digest(package.ScriptDefinition)!=(string)specification["launch"]["program_sha256"]||NativeScriptDefinition.Admit(package.ScriptDefinition).BuildId!=(string)specification["launch"]["build"]||!(specification["resources"] is JArray resources)||resources.Count!=0)throw new InvalidDataException("external-native-installed-identity");
    var frame=(JArray)package.Data.Scene["frame"];externalFrame=new Vector3((float)frame[0],(float)frame[1],(float)frame[2]);
    var identity=new SessionIdentity(Guid.ParseExact((string)configuration["session"],"N"),(uint)configuration["generation"],SceneCommandCatalog.Digest);
    resourceOwner=new SpatialWallpaper.Sdk.Execution.OwnedDomain(identity.Instance,identity.Generation);
    // Once native construction begins, a failure is process-fatal. Its ledger
    // remains retained until process exit rather than claiming unknown GPU state.
    externalConstructionFailed=true;externalScene=new ExternalNativeScene(package,resourceOwner);
    var cameraObject=new GameObject("ExternalNativeCamera",typeof(Camera),typeof(AudioListener),typeof(WindowProjection));
    var camera=cameraObject.GetComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;
    externalProjection=cameraObject.GetComponent<WindowProjection>();externalProjection.viewportManagedExternally=true;
    externalCancellation=new CancellationTokenSource();externalLoading=externalScene.LoadAsync(camera,(double)pose["width"],(double)pose["height"],(bool)pose["keep_height"],externalCancellation.Token);
   }catch(Exception error){failure=error.GetType().Name+":"+error.Message;Debug.LogError("external-native-render-start:"+failure);closing=true;}
   if(externalLoading==null)yield break;
   while(!externalLoading.IsCompleted)yield return null;
   try{
    externalLoading.GetAwaiter().GetResult();externalConstructionFailed=false;if(closing)yield break;
    content=externalScene.Content;projection=externalProjection;
    authoredScale=Vector3.one;authoredDepth=externalFrame.z;projection.viewportManagedExternally=true;ApplyPose(pose);
    var identity=new SessionIdentity(Guid.ParseExact((string)configuration["session"],"N"),(uint)configuration["generation"],SceneCommandCatalog.Digest);
    var assets=new NativeAssetCatalog(new NativeAssetBinding[0]);externalConstructionFailed=true;BindScript(identity,externalScene.ScriptNodes,assets,specification);externalConstructionFailed=false;
    inputSpecification=configuration["input"] as JObject;
   }catch(Exception error){failure=error.GetType().Name+":"+error.Message;Debug.LogError("external-native-render-start:"+failure);closing=true;}
  }
  void ApplyPose(JObject value){
   if(!projection)return;
   float width=(float)value["width"],height=(float)value["height"];bool keep=(bool)value["keep_height"];
   var eye=value["eye"] as JArray;if(!Finite(width)||!Finite(height)||width<.01f||height<.01f||width>10||height>10||eye==null||eye.Count!=3)throw new InvalidDataException("native-render-pose");
   float factor=externalScene==null?(float)ArtworkFraming.Scale(width,height,keep):(float)Framing.Scale(externalFrame.x,externalFrame.y,width,height,keep);authorFactor=factor;content.localScale=authoredScale*factor;projection.contentDepth=authoredDepth*factor;
   projection.displayWidth=width;projection.displayHeight=height;
   var position=new Vector3((float)eye[0],(float)eye[1],(float)eye[2]);if(!projection.SetEye(position))throw new InvalidDataException("native-render-eye");projection.SetExternalFreshness((bool?)value["fresh"]==true);
   script?.PublishView(position.x/factor,position.y/factor,position.z/factor,width/factor,height/factor);
   tracking.Accept(value["tracking"] as JObject,SpatialTracking.QpcClock.Now);
  }
  static bool Finite(float value)=>!float.IsNaN(value)&&!float.IsInfinity(value);
  void ApplySound(JObject value){var next=value["sound"];if(sound==null||JToken.DeepEquals(next,appliedSound))return;sound.ApplyUserMix(next==null?new SoundMix():SoundMix.Parse(next));appliedSound=next?.DeepClone();}
  IEnumerator PreparePrefabs(NativeAssetCatalog assets,JObject counts){
   foreach(var resource in assets.Definitions.Where(value=>value.Kind=="sound")){
    var clip=assets.Require<AudioClip>(resource.Id,"sound");if(clip.loadState==AudioDataLoadState.Unloaded&&!clip.LoadAudioData())throw new InvalidOperationException("native-sound-preparation-start-failed");while(clip.loadState==AudioDataLoadState.Loading)yield return null;if(clip.loadState!=AudioDataLoadState.Loaded)throw new InvalidOperationException("native-sound-preparation-failed");
   }
   if(counts==null)yield break;
   foreach(var property in counts.Properties()){
    var id=assets.Bindings[property.Name];if(id==null||property.Value.Type!=JTokenType.Integer)throw new InvalidDataException("native-prefab-prewarm-binding");var preparation=prefabs.Prewarm((long)id,(int)property.Value);
    try{while(preparation.MoveNext())yield return null;}finally{(preparation as IDisposable)?.Dispose();}
   }
  }
  public static NativeAssetBinding ResolveScriptAsset(Scene ownedScene,Transform ownedContent,JObject binding){
   var node=ResolveScriptNode(ownedScene,ownedContent,(string)binding["node_path"]);string kind=(string)binding["kind"];UnityEngine.Object asset;
   if(kind=="prefab")asset=node.gameObject;
   else if(kind=="sound")asset=node.GetComponent<AudioSource>()?.clip;
   else if(kind=="mesh")asset=node.GetComponent<MeshFilter>()?.sharedMesh??node.GetComponent<SkinnedMeshRenderer>()?.sharedMesh;
   else if(kind=="material"||kind=="texture"||kind=="cubemap"){
    var renderer=node.GetComponent<Renderer>();int slot=(int?)binding["slot"]??0;var materials=renderer?renderer.sharedMaterials:null;if(materials==null||slot<0||slot>=materials.Length||!materials[slot])throw new InvalidDataException("native-script-material-binding");var material=materials[slot];
    if(kind=="material")asset=material;else{string property=(string)binding["property"];if(string.IsNullOrEmpty(property)||!material.HasProperty(property))throw new InvalidDataException("native-script-texture-binding");asset=material.GetTexture(property);}
   }else throw new InvalidDataException("native-script-resource-binding-kind");
   return new NativeAssetBinding((string)binding["name"],kind,asset,(string)binding["source_sha256"]);
  }
  bool PreviewRegionProof(bool focus,out int left,out int top,out int width,out int height){left=top=width=height=0;if(!previewEnabled||previewRegion==null)return false;left=previewRegion[0];top=previewRegion[1];width=previewRegion[2];height=previewRegion[3];return true;}
  void UpdatePreviewInteraction(JObject latest,bool paused){
   if(DesktopTarget||(bool?)configuration["preview_ui"]!=true||renderWindow==IntPtr.Zero)return;
   if(previewInteraction==null){previewInteraction=new NativePreviewInteractionTarget(()=>(nativeInput as NativePreviewInputSession)?.CaptureTarget);previewCollector=new WindowsPreviewInput(renderWindow,previewInteraction,PreviewRegionProof,null,previewInteraction.Escape,(bool?)configuration["input_trace"]==true);}
   int[] next=(latest["input_region"] as JArray)?.ToObject<int[]>();if(next!=null&&(next.Length!=4||next[2]<=0||next[3]<=0))throw new InvalidDataException("native-preview-ui-region");
   bool changed=previewRegion==null||next==null||!previewRegion.SequenceEqual(next);if(changed||previewEnabled&&paused)previewInteraction.Revoke();previewRegion=next;previewEnabled=!paused&&next!=null;
  }
  void ApplyPresentation(JObject latest){
   if(!(latest["presentation"] is JObject refresh)||nativeDiagnostics&&configuration["diagnostic_vsync"]!=null)return;
   uint numerator=(uint?)refresh["numerator"]??0,denominator=(uint?)refresh["denominator"]??0;
   if(!presentationConfigured||numerator!=presentationNumerator||denominator!=presentationDenominator){presentationNumerator=numerator;presentationDenominator=denominator;presentationSync=new DesktopDisplay{RefreshNumerator=numerator,RefreshDenominator=denominator}.NativeVSyncCount;presentationConfigured=true;}
   if(QualitySettings.vSyncCount!=presentationSync)QualitySettings.vSyncCount=presentationSync;
   int queued=nativeDiagnostics&&configuration["diagnostic_queued_frames"]!=null?(int)configuration["diagnostic_queued_frames"]:presentationSync>0?1:initialQueuedFrames;if(QualitySettings.maxQueuedFrames!=queued)QualitySettings.maxQueuedFrames=queued;
  }
  // Trusted catalog bindings may include a root outside the content container
  // (for example an original scene light). Never search another loaded scene.
  public static Transform ResolveScriptNode(Scene ownedScene,Transform ownedContent,string path){
   if(!ownedScene.IsValid()||!ownedScene.isLoaded||!ownedContent||ownedContent.gameObject.scene!=ownedScene||path==null||path.Split('/').Any(p=>p==".."||p=="."||path.Length!=0&&p.Length==0))throw new InvalidDataException("native-script-node-path");
   Transform node;
   if(path.StartsWith("@scene/",StringComparison.Ordinal)){
    var relative=path.Substring(7);int slash=relative.IndexOf('/');string rootName=slash<0?relative:relative.Substring(0,slash);var roots=ownedScene.GetRootGameObjects().Where(r=>r.name==rootName).ToArray();
    if(roots.Length!=1)throw new InvalidDataException("native-script-root-missing-or-ambiguous:"+rootName);node=slash<0?roots[0].transform:UniqueDescendant(roots[0].transform,relative.Substring(slash+1));
   }else node=path.Length==0?ownedContent:UniqueDescendant(ownedContent,path);
   if(!node||node.gameObject.scene!=ownedScene||node.GetComponentInChildren<Camera>(true))throw new InvalidDataException("native-script-node-missing-or-forbidden:"+path);return node;
  }
  static Transform UniqueDescendant(Transform parent,string path){
   foreach(string segment in path.Split('/')){
    Transform next=null;int count=0;for(int i=0;i<parent.childCount;i++){var child=parent.GetChild(i);if(child.name==segment){next=child;count++;}}
    if(count!=1)throw new InvalidDataException("native-script-child-missing-or-ambiguous:"+segment);parent=next;
   }
   return parent;
  }
  void Update(){
   if(closing){if(!quitting){quitting=true;StartCoroutine(Close());}return;}
   if(!projection)return;
   try{
    JObject latest;lock(gate)latest=pose;ApplyPose(latest);ApplyPresentation(latest);ApplySound(latest);
    bool paused=(bool?)latest["paused"]==true;script?.SetPaused(paused);Time.timeScale=paused?0:1;AudioListener.pause=paused;
    if(resourcePreparation!=null){if(resourcePreparation.MoveNext())return;(resourcePreparation as IDisposable)?.Dispose();resourcePreparation=null;beginScript();beginScript=null;}
    prefabs?.Step();
    if(renderWindow==IntPtr.Zero)renderWindow=WindowsDesktopWindow.FindOwnWindow(true);
    if(nativeInput==null&&inputSpecification!=null&&renderWindow!=IntPtr.Zero){
     using(var self=PinnedProcessOwner.PinCurrent()){
      var instance=Guid.ParseExact((string)configuration["session"],"N");uint generation=(uint)configuration["generation"];var region=((JArray)inputSpecification["region"]).ToObject<int[]>();
      if(DesktopTarget)nativeInput=new NativeInputProcess((string)inputSpecification["helper"],(string)inputSpecification["cache"],(string)inputSpecification["descriptor"],instance,generation,self,renderWindow,renderThread,region);
      else nativeInput=new NativePreviewInputSession((string)inputSpecification["descriptor"],instance,generation,self,renderWindow,region,(bool?)configuration["preview_ui"]==true);
     }
    }
    UpdatePreviewInteraction(latest,paused);
    var observedInput=nativeInput?.Latest;
    ulong inputEpoch=observedInput==null?0:BitConverter.ToUInt64(observedInput.Snapshot,8);bool fault=script?.Error!=null;bool revoke=paused&&!resourcePaused||fault&&!resourceFault||resourceInputEpoch!=0&&inputEpoch!=resourceInputEpoch;if(revoke)target?.RevokeResourceAuthority(checked(++resourceAuthority));resourcePaused=paused;resourceFault=fault;resourceInputEpoch=inputEpoch;target?.StepDynamicResources();
    if(nativeInput!=null){
     long pause=(long?)latest["input_pause_revision"]??0;if(pause<inputPauseRevision)throw new InvalidDataException("native-input-pause-revision-regressed");
     if(pause>inputPauseRevision){nativeInput.SetEnabled(false);inputPauseRevision=pause;}
     nativeInput.SetEnabled((bool?)latest["paused"]!=true&&script.Error==null&&!script.Completion.IsCompleted&&previewInteraction?.AuthorError==null);
     if(nativeInput is NativePreviewInputSession preview&&latest["input_region"] is JArray region)preview.SetRegion(region.ToObject<int[]>());
     nativeInput.Pump();
     if(!nativeInput.Ready)script.RevokeInput();var inputs=nativeInput.Ready?observedInput:null;
     if(inputs!=null){script.PumpOrdered(Time.unscaledDeltaTime,inputs);if(script.Error==null)nativeInput.Accept(inputs.Cursor);}else script.Pump(Time.unscaledDeltaTime);
    }else script?.Pump(Time.unscaledDeltaTime);
    // Publish the surface only after its optional input child has a verified
    // control peer, so DesktopRuntime can retain that original child pin too.
    var snapshot=new JObject{{"ready",renderWindow!=IntPtr.Zero&&(configuration["script"]==null||script!=null&&script.AcknowledgedBatches>0&&script.Error==null)&&(inputSpecification==null||nativeInput!=null&&nativeInput.Connected)},{"pid",System.Diagnostics.Process.GetCurrentProcess().Id},{"creation_filetime",processCreation},{"window",renderWindow.ToInt64()},{"render_thread",renderThread},{"frame",Time.renderedFrameCount},{"artwork",configuration["artwork"]},{"pipeline",UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline?UnityEngine.Rendering.GraphicsSettings.currentRenderPipeline.name:"Built-in"},{"lightmaps",LightmapSettings.lightmaps.Length},
     {"screen_width",Screen.width},{"screen_height",Screen.height},{"eye",new JArray(projection.EyeScreen.x,projection.EyeScreen.y,projection.EyeScreen.z)},{"render_errors",renderErrors},{"script_running",script?.Running??false},{"script_batches",script?.AcknowledgedBatches??0},{"script_error",script?.Error},{"script_pid",script?.ScriptProcessId??0},{"script_creation",script?.ScriptCreationFileTime??0},{"input_pid",nativeInput?.ProcessId??0},{"input_creation",nativeInput?.CreationFileTime??0},{"input_ready",nativeInput?.Ready??false},{"input_helpers",new JArray(nativeInput?.HelperProcessIds??new int[0])},{"input_epoch",observedInput==null?0:BitConverter.ToUInt64(observedInput.Snapshot,8)},{"input_error",nativeInput?.Error},{"error",failure??(script!=null&&script.AcknowledgedBatches==0?script.Error:null)}};
    // Bounded trusted diagnostics from actual native snapshot metadata (never
    // pixels). The script cannot write these values or supply an object handle.
    snapshot["paused"]=paused;snapshot["time_scale"]=Time.timeScale;snapshot["audio_paused"]=AudioListener.pause;
    snapshot["script_requires_reapply"]=script?.Error!=null||nativeInput?.Error!=null||previewInteraction?.AuthorError!=null;
    snapshot["input_completed"]=nativeInput?.Completion.IsCompleted??false;snapshot["input_resources_released"]=nativeInput?.ResourcesReleased??false;
    if(nativeInput is NativePreviewInputSession previewInput){snapshot["input_source_revoke_sequence"]=previewInput.SourceRevocationSequence;snapshot["input_source_revoke_time"]=previewInput.SourceRevocationTime;snapshot["input_source_revoke_reason"]=previewInput.SourceRevocationReason;}
    if(previewInteraction!=null){snapshot["preview_ui"]=new JObject{{"drag_x",previewInteraction.DragX},{"drag_y",previewInteraction.DragY},{"dragging",previewInteraction.Dragging},{"escapes",previewInteraction.Escapes},{"author_error",previewInteraction.AuthorError},{"rendered_frame",renderedFrame},{"rendered_width",renderedWidth},{"rendered_height",renderedHeight}};snapshot["input_error"]=nativeInput?.Error??previewInteraction.AuthorError;snapshot["input_source_revoke_sequence"]=previewCollector.SourceRevocationSequence;snapshot["input_source_revoke_time"]=previewCollector.SourceRevocationTime;snapshot["input_source_revoke_reason"]=previewCollector.SourceRevocationReason;}
    if(previewCollector!=null){snapshot["input_source_revoke_detail"]=previewCollector.SourceRevocationDetail;if((bool?)configuration["input_trace"]==true)snapshot["input_message_trace"]=new JArray(previewCollector.MessageTrace);}
    snapshot["tracking_fresh"]=(bool?)latest["fresh"]==true;snapshot["tracking_stream"]=CurrentRaw?.Stream;
    snapshot["v_sync_count"]=QualitySettings.vSyncCount;snapshot["target_frame_rate"]=Application.targetFrameRate;snapshot["render_frame_interval"]=UnityEngine.Rendering.OnDemandRendering.renderFrameInterval;
    if(nativeDiagnostics){
     if(nativeInput is NativePreviewInputSession observedPreview)snapshot["input_transport"]=new JObject{{"pending",observedPreview.TransportPending},{"queued",observedPreview.PendingCount},{"peak_queued",observedPreview.PeakPendingCount}};
     if(script!=null){snapshot["input_script_queue"]=new JObject{{"pending",script.PendingInputEvents},{"rejected",script.RejectedInputEvents}};snapshot["input_cursor"]=observedInput?.Cursor??0;snapshot["script_last_input_event"]=script.LastAcknowledgedInputEvent;}
     snapshot["max_queued_frames"]=QualitySettings.maxQueuedFrames;snapshot["graphics_api"]=SystemInfo.graphicsDeviceType.ToString();
     int gc0=GC.CollectionCount(0),gc1=GC.CollectionCount(1),gc2=GC.CollectionCount(2);if(gc0!=lastGc0||gc1!=lastGc1||gc2!=lastGc2){lastGc0=gc0;lastGc1=gc1;lastGc2=gc2;lastGcTime=SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds();}
     snapshot["gc0"]=gc0;snapshot["gc1"]=gc1;snapshot["gc2"]=gc2;snapshot["last_gc_time"]=lastGcTime;
     var camera=projection.GetComponent<Camera>();
     snapshot["unity_frame"]=Time.frameCount;snapshot["rendered_frame"]=renderedFrame;snapshot["rendered_camera"]=renderedCamera;snapshot["rendered_artwork"]=renderedFrame<0?null:configuration["artwork"];
     snapshot["sdk_loaded"]=false;snapshot["sdk_static_held"]=false;
     snapshot["cameras"]=new JArray(new JObject{{"id",camera.GetInstanceID()},{"name",camera.name},{"selected",true},{"scene",camera.gameObject.scene.path},{"enabled",camera.enabled},{"active",camera.gameObject.activeInHierarchy},{"target_texture",camera.targetTexture?camera.targetTexture.name:null},{"pixels",new JArray(camera.pixelWidth,camera.pixelHeight)}});
    }
    var scriptScene=script?.LatestSnapshot;if(scriptScene!=null){snapshot["script_scene_revision"]=scriptScene.SceneRevision;snapshot["script_node_values"]=ScriptNodeDiagnostics(scriptScene);}
    snapshot["nonce"]=configuration["nonce"];lock(gate)state=snapshot;
   }catch(Exception error){failure=error.GetType().Name+":"+error.Message;closing=true;}
  }
  JObject CaptureState(){lock(gate)return state;}
  JToken ScriptNodeDiagnostics(SceneSnapshot value){
   int count=Math.Min(8,value.Objects.Count);bool changed=scriptStatusNodes==null||scriptStatusNodes.Length!=count;
   if(!changed)for(int index=0;index<count;index++)if(!ReferenceEquals(scriptStatusNodes[index],value.Objects[index])){changed=true;break;}
   if(changed){var nodes=new SceneObject[count];var values=new JArray();for(int index=0;index<count;index++){var node=value.Objects[index];nodes[index]=node;values.Add(new JObject{{"id",node.Id},{"kind",node.Kind},{"state",node.State}});}string json=values.ToString(Newtonsoft.Json.Formatting.None);scriptStatusNodes=nodes;scriptStatusJson=json;}
   return new JRaw(scriptStatusJson);
  }
  void Control(){
   try{
    var owner=configuration["owner"];using(var pinned=PinnedProcessOwner.Open((int)owner["pid"],(long)owner["creation_filetime"]))using(var pipe=new NamedPipeClientStream(".",(string)configuration["pipe"],PipeDirection.InOut,PipeOptions.Asynchronous)){
     pipe.Connect(5000);if(NativeScriptControl.Peer(pipe,false)!=pinned.Id)throw new InvalidDataException("native-render-controller");
     while(!closing){
      var request=NativeScriptControl.Deadline(()=>NativeScriptControl.Read(pipe),5000);if((string)request["nonce"]!=(string)configuration["nonce"]||pinned.HasExited)throw new InvalidDataException("native-render-owner");
      string operation=(string)request["operation"];if(operation=="pose"){var next=request["pose"] as JObject;if(next==null)throw new InvalidDataException("native-render-pose");lock(gate)pose=(JObject)next.DeepClone();}else if(operation=="stop")closing=true;else if(operation!="status")throw new InvalidDataException("native-render-operation");
      var reply=CaptureState();NativeScriptControl.Deadline(()=>{NativeScriptControl.Write(pipe,reply);return true;},1000);
     }
    }
   }catch(Exception error){failure=error.GetType().Name+":"+error.Message;closing=true;}
  }
  bool WantsToQuit(){if(exitAllowed)return true;closing=true;return false;}
  void ObserveSrpFrame(UnityEngine.Rendering.ScriptableRenderContext context,Camera camera)=>ObserveFrame(camera);
  void ObserveFrame(Camera camera){if(projection&&camera==projection.GetComponent<Camera>()){renderedFrame=Time.frameCount;renderedCamera=camera.GetInstanceID();renderedWidth=camera.pixelWidth;renderedHeight=camera.pixelHeight;}}
  void RenderLog(string message,string trace,LogType type){if(type==LogType.Error||type==LogType.Exception||type==LogType.Assert||message.StartsWith("Shader ",StringComparison.Ordinal)&&message.Contains(": variant ")&&message.TrimEnd().EndsWith(" not found.",StringComparison.Ordinal))renderErrors++;}
  IEnumerator Close(){
   Debug.Log("Native renderer shutdown: begin");
   (resourcePreparation as IDisposable)?.Dispose();resourcePreparation=null;beginScript=null;
   previewEnabled=false;previewCollector?.Dispose();previewCollector=null;nativeInput?.Stop();script?.Dispose();float deadline=Time.realtimeSinceStartup+9;while(script!=null&&!script.Completion.IsCompleted&&Time.realtimeSinceStartup<deadline)yield return null;
   Debug.Log("Native renderer shutdown: script completed="+(script==null||script.Completion.IsCompleted)+" released="+(script==null||script.ResourcesReleased)+" error="+script?.CleanupError);
   if(script!=null&&!script.Completion.IsCompleted)failure="native-script-cleanup-unproven";
   else if(script?.CleanupError!=null)failure="native-script-cleanup-failed:"+script.CleanupError;
   // Original global input hooks may outlive an IO deadline. Their background
   // owner keeps retrying; the renderer must not pretend they were released.
   while(nativeInput!=null&&!nativeInput.Completion.IsCompleted)yield return null;
   Debug.Log("Native renderer shutdown: input completed="+(nativeInput==null||nativeInput.Completion.IsCompleted)+" released="+(nativeInput==null||nativeInput.ResourcesReleased)+" error="+nativeInput?.CleanupError);
   if(nativeInput!=null&&!nativeInput.ResourcesReleased)failure="native-input-cleanup-unproven:"+nativeInput.CleanupError;
   target?.Dispose();while(target!=null&&!target.CanDisposeResourceOwner){target.StepDynamicResources();yield return null;}
   externalCancellation?.Cancel();while(externalLoading!=null&&!externalLoading.IsCompleted)yield return null;
   if(externalScene!=null&&!externalConstructionFailed&&externalLoading!=null&&externalLoading.Status==TaskStatus.RanToCompletion){
    Task retirement=null;try{retirement=externalScene.CloseAsync();}catch(Exception error){failure="external-native-retirement:"+error.Message;externalConstructionFailed=true;}
    if(retirement!=null){while(!retirement.IsCompleted)yield return null;if(retirement.IsFaulted||retirement.IsCanceled){failure="external-native-retirement-failed";externalConstructionFailed=true;}else while(!externalScene.CanDisposeOwner){externalScene.PumpRetirement();yield return null;}}
   }
   if(externalConstructionFailed||externalLoading!=null&&externalLoading.Status!=TaskStatus.RanToCompletion){failure=failure??"external-native-construction-unproven";exitAllowed=true;Debug.LogError("Native renderer shutdown: retained unknown native state until process exit: "+failure);Application.Quit(1);yield break;}
   externalCancellation?.Dispose();externalCancellation=null;resourceOwner?.Dispose();resourceOwner=null;dynamicResources=null;prefabs?.Dispose();prefabs=null;sound?.Dispose();sound=null;exitAllowed=true;Debug.Log("Native renderer shutdown: quit error="+failure);Application.Quit(failure==null?0:1);
  }
  void OnDestroy(){Application.wantsToQuit-=WantsToQuit;Application.logMessageReceived-=RenderLog;Camera.onPostRender-=ObserveFrame;UnityEngine.Rendering.RenderPipelineManager.endCameraRendering-=ObserveSrpFrame;previewEnabled=false;previewCollector?.Dispose();previewCollector=null;nativeInput?.Stop();script?.Stop();(resourcePreparation as IDisposable)?.Dispose();resourcePreparation=null;beginScript=null;prefabs?.Dispose();prefabs=null;sound?.Dispose();sound=null;}
 }
}
