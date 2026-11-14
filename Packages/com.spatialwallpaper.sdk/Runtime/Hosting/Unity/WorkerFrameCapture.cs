using System;using UnityEngine;using UnityEngine.Rendering;using SpatialWallpaper.Sdk.Execution;
namespace SpatialWallpaper.Sdk.Hosting {
 // Platform-owned camera capture. No author binding exposes cameras, GPU readback or IPC handles.
 public sealed class WorkerFrameCapture:MonoBehaviour {
  Camera view;RenderTexture target,previous;WindowsFrameWriter writer;UnityOwnedHost host;OwnedDomain domain;ResourceLease targetMemory,shadowMemory,postMemory;
  int pixelWidth,pixelHeight;byte[] row;FrameCadence cadence;
  bool initialized,stopped,previousEnabled;readonly bool[] reading=new bool[3];readonly AsyncGPUReadbackRequest[] pendingRequests=new AsyncGPUReadbackRequest[3];int requestedFrame=-1;long submittedSequence;RenderPipeline.StandardRequest renderRequest;
  Newtonsoft.Json.Linq.JObject renderProfile;bool renderProfileVerified;
  Action<Newtonsoft.Json.Linq.JObject,Newtonsoft.Json.Linq.JObject> profileReceipt;
  UnityRenderObservation.Binding profileBinding;
  bool timing;long timingAt,submissions,completions,readbackWaits,cadenceSkips,slotWaits,completionFrames,maximumCompletionFrames;
  public string Error{get;private set;}
  public long CompletedSequence{get;private set;}
  public long ProjectionRevision{get;private set;}
  public void SetProjectionRevision(long revision){if(!initialized||stopped||revision<=ProjectionRevision)throw new InvalidOperationException("capture-projection-revision");ProjectionRevision=revision;}
  public int LastCapturedUnityFrame{get;private set;}=-1;
  public void Initialize(Camera camera,WindowsFrameWriter transport,UnityOwnedHost content,OwnedDomain owner,int width,int height,Newtonsoft.Json.Linq.JObject identity,Action<Newtonsoft.Json.Linq.JObject,Newtonsoft.Json.Linq.JObject> receipt,int maximumFramesPerSecond=RuntimeLimits.MaxFps){
   if(initialized||renderProfile!=null||Application.platform!=RuntimePlatform.WindowsPlayer)throw new PlatformNotSupportedException("capture-requires-Windows-Worker");
   SpatialWallpaper.Sdk.Security.RenderProfiles.Require(identity);if(receipt==null)throw new ArgumentNullException(nameof(receipt));
   renderProfile=(Newtonsoft.Json.Linq.JObject)identity.DeepClone();profileReceipt=receipt;
   if(!camera||transport==null||content==null||owner==null||!SystemInfo.supportsAsyncGPUReadback)throw new InvalidOperationException("worker-capture-unavailable");
   if(SystemInfo.graphicsDeviceType!=GraphicsDeviceType.Direct3D11)throw new PlatformNotSupportedException("unverified-Worker-graphics-backend");
   cadence=new FrameCadence(maximumFramesPerSecond);
   timing=Array.IndexOf(Environment.GetCommandLineArgs(),"--sdk-timing-fps")>=0;timingAt=System.Diagnostics.Stopwatch.GetTimestamp();
   RuntimeLimits.RequireRenderSize(width,height);int bytes=WorkerFrameCodec.PayloadBytes(width,height);pixelWidth=width;pixelHeight=height;domain=owner;writer=transport;host=content;view=camera;
   var pipeline=GraphicsSettings.currentRenderPipeline as UnityEngine.Rendering.Universal.UniversalRenderPipelineAsset;
   if(!pipeline)throw new InvalidOperationException("worker-trusted-render-pipeline-missing");
   shadowMemory=domain.Ledger.Reserve(ResourceEstimates.ShadowAtlases(host.UsesMainShadowAtlas&&pipeline.supportsMainLightShadows?pipeline.mainLightShadowmapResolution:0,host.UsesAdditionalShadowAtlas&&pipeline.supportsAdditionalLightShadows?pipeline.additionalLightsShadowmapResolution:0));shadowMemory.Commit();
   postMemory=domain.Ledger.Reserve(ResourceEstimates.PostProcessing(width,height,pipeline.colorGradingLutSize,host.UsesPostProcessing));postMemory.Commit();
   // Color/depth plus at most three asynchronous readback allocations.
   targetMemory=domain.Ledger.Reserve(new ResourceCost(checked(8192L+width*4L),checked(65536L+bytes*5L),checked(65536L+bytes*5L),5));
   try{
    row=new byte[checked(width*4)];previous=view.targetTexture;
    // The legacy depth-bits constructor may choose a different actual depth
    // format. Request the profile's precise allocation and verify it below.
    var descriptor=new RenderTextureDescriptor(width,height,RenderTextureFormat.ARGB32,0){
     graphicsFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.R8G8B8A8_SRGB,
     depthStencilFormat=UnityEngine.Experimental.Rendering.GraphicsFormat.D24_UNorm_S8_UInt,msaaSamples=1};
    target=new RenderTexture(descriptor);
    if(!target.Create())throw new InvalidOperationException("worker-render-target-creation");
    view.targetTexture=target;previousEnabled=view.enabled;view.enabled=false;renderRequest=new RenderPipeline.StandardRequest{destination=target};targetMemory.Commit();initialized=true;
   }catch{if(target)Destroy(target);throw;}
  }
  void LateUpdate(){
   if(!initialized||stopped||Error!=null)return;
   if(timing){long now=System.Diagnostics.Stopwatch.GetTimestamp();if(now-timingAt>=System.Diagnostics.Stopwatch.Frequency){Debug.Log("SDK_CAPTURE_TIMING "+new Newtonsoft.Json.Linq.JObject{{"elapsed_ms",(now-timingAt)*1000.0/System.Diagnostics.Stopwatch.Frequency},{"submissions",submissions},{"completions",completions},{"readback_waits",readbackWaits},{"cadence_skips",cadenceSkips},{"slot_waits",slotWaits},{"completion_unity_frames",completionFrames},{"maximum_completion_unity_frames",maximumCompletionFrames}}.ToString(Newtonsoft.Json.Formatting.None));timingAt=now;submissions=completions=readbackWaits=cadenceSkips=slotWaits=completionFrames=maximumCompletionFrames=0;}}
   int requestSlot=-1;for(int i=0;i<reading.Length;i++){if(reading[i])pendingRequests[i].Update();if(!reading[i]&&requestSlot<0)requestSlot=i;}
   if(requestSlot<0){readbackWaits++;return;}
   if(requestedFrame==Time.frameCount)return;
   if(!cadence.TryBegin(System.Diagnostics.Stopwatch.GetTimestamp())){cadenceSkips++;return;}
   requestedFrame=Time.frameCount;int capturedFrame=requestedFrame,ownedRequest=requestSlot;long capturedProjection=ProjectionRevision;reading[ownedRequest]=true;
   AllocationFrame allocation=null;
   Newtonsoft.Json.Linq.JObject lastObservation=null;
   try{
    allocation=AllocationFrame.Begin(domain.Ledger);allocation.Charge(65536);
    var pendingAllocation=allocation;
    // A private/noninteractive window can be occluded. Submit the full pinned pipeline explicitly.
    // Request readback only after the render request returned, preserving command order.
    var before=UnityRenderObservation.Read(view,target,TextureFormat.RGBA32,out var beforeBinding);
    lastObservation=before;
    SpatialWallpaper.Sdk.Security.RenderProfiles.RequireObservation(renderProfile,before);
    bool initializingRenderer=profileBinding==null;
    if(profileBinding!=null)profileBinding.RequireSame(beforeBinding);
    long captureSequence=checked(++submittedSequence);host.SubmittedRender(captureSequence);
    RenderPipeline.SubmitRenderRequest(view,renderRequest);
    // Validate the renderer selected for the real submitted camera and its
    // allocated target before the first readback can become a published frame.
    // Recheck each submission: a later camera/pipeline mutation cannot inherit
    // the first frame's approval. The JSON allocation is charged per capture.
    var observation=UnityRenderObservation.Read(view,target,TextureFormat.RGBA32,out var afterBinding);
    lastObservation=observation;
    beforeBinding.RequireSame(afterBinding,initializingRenderer);profileBinding=afterBinding;
    SpatialWallpaper.Sdk.Security.RenderProfiles.RequireObservation(renderProfile,observation);
    if(!Newtonsoft.Json.Linq.JToken.DeepEquals(before,observation))throw new InvalidOperationException("render-profile-changed-during-submission");
    // URP creates its pipeline on the first submission and can replace the
    // renderer cache. This is a private warm-up, never a consumable frame.
    // All later submissions require the initialized object binding unchanged.
    if(initializingRenderer){reading[ownedRequest]=false;GC.KeepAlive(allocation);return;}
    if(!renderProfileVerified){
     profileReceipt(observation,UnityRenderObservation.Hardware(target));renderProfileVerified=true;
    }
    submissions++;pendingRequests[ownedRequest]=AsyncGPUReadback.Request(target,0,TextureFormat.RGBA32,request=>{
     reading[ownedRequest]=false;
     completions++;long delay=Time.frameCount-capturedFrame;completionFrames+=delay;maximumCompletionFrames=Math.Max(maximumCompletionFrames,delay);
     FrameSlot slot=null;try{
      if(stopped||Error!=null||capturedProjection!=ProjectionRevision||captureSequence<=CompletedSequence)return;
      if(request.hasError||request.GetData<byte>().Length!=WorkerFrameCodec.PayloadBytes(pixelWidth,pixelHeight))throw new InvalidOperationException("worker-GPU-readback");
      // A trusted component may change state while GPU work is pending. A
      // successful submission does not authorize publishing after that change.
      var completedObservation=UnityRenderObservation.Read(view,target,TextureFormat.RGBA32,out var completedBinding);
      profileBinding.RequireSame(completedBinding);
      SpatialWallpaper.Sdk.Security.RenderProfiles.RequireObservation(renderProfile,completedObservation);
      slot=writer.AcquireCapture();if(slot==null){slotWaits++;return;}request.GetData<byte>().CopyTo(slot.Pixels);
      WorkerFrameCodec.FlipVerticalRows(slot.Pixels,pixelWidth,pixelHeight,row);
      host.CompletedRender(captureSequence);
      writer.Publish(slot,captureSequence,capturedProjection);CompletedSequence=captureSequence;LastCapturedUnityFrame=capturedFrame;
     }catch(Exception error){Error="worker-frame-completion";Debug.LogException(error);if(slot!=null)try{writer.CancelCapture(slot);}catch{}}
     finally{GC.KeepAlive(pendingAllocation);}
    });
    // Complete this camera's readback before accepting the next projection.
    // Otherwise continuous tracking cancels most delayed offscreen frames.
    pendingRequests[ownedRequest].WaitForCompletion();
   }catch(Exception error){reading[ownedRequest]=false;Error="worker-frame-submission";if(lastObservation!=null)Debug.Log("SDK_RENDER_PROFILE_REJECTED "+lastObservation.ToString(Newtonsoft.Json.Formatting.None));Debug.LogException(error);GC.KeepAlive(allocation);}
  }
  public void Stop(){
   if(stopped)return;stopped=true;
   if(view){view.targetTexture=previous;view.enabled=previousEnabled;}if(target)Destroy(target);
   // Renderer/native estimates remain charged for this Worker lifetime; process exit is final reclamation.
  }
  void OnDestroy(){Stop();}
 }
}
