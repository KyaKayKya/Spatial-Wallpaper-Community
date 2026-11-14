using System;using System.IO;using System.Globalization;using System.Collections.Generic;using UnityEngine;using UnityEngine.Rendering.Universal;using SpatialWallpaper.Sdk.Execution;using SpatialWallpaper.Sdk.Security;
namespace SpatialWallpaper.Sdk.Hosting {
 // Production author-free bootstrap, shared by preview and client builds. No verification fixture or injected mode.
 public sealed class WorkerRuntime:MonoBehaviour {
  WindowsWorkerStopReader stopReader;WindowsWorkerProjectionReader projectionReader;WindowProjection projection;float authoredWidth,authoredHeight;int pixelWidth,pixelHeight;bool stopping;
  OwnedDomain domain;UnityOwnedHost host;VmRuntime runtime;WindowsFrameWriter writer;WorkerFrameCapture capture;SpatialWallpaper.Sdk.Capabilities.WindowsCapabilityClient capabilities;bool failed;bool admitted;
  SpatialWallpaper.Sdk.Capabilities.CapabilityCalls calls;WorkerFrameTiming timing;
  static readonly HashSet<string> Keys=new HashSet<string>(new[]{"--sdk-package","--sdk-build","--sdk-attempt","--sdk-instance","--sdk-generation","--sdk-width","--sdk-height","--sdk-lpac-canary","--sdk-lpac-control","--sdk-frame-handle","--sdk-capability-request","--sdk-capability-reply","--sdk-stop-handle","--sdk-projection-handle"},StringComparer.Ordinal);
  void Start(){try{
   var args=Environment.GetCommandLineArgs();var values=new Dictionary<string,string>(StringComparer.Ordinal);
   for(int i=1;i<args.Length;i++)if(args[i].StartsWith("--sdk-",StringComparison.Ordinal)){
    string key=args[i];if(!Keys.Contains(key)&&key!="--sdk-timing-fps"||values.ContainsKey(key)||++i>=args.Length)throw new InvalidDataException("Worker-launch-contract");values.Add(key,args[i]);
   }
   if(values.Count!=Keys.Count+(values.ContainsKey("--sdk-timing-fps")?1:0))throw new InvalidDataException("Worker-launch-contract");int targetFps=RuntimeLimits.MaxFps;if(values.TryGetValue("--sdk-timing-fps",out var selectedFps)&&(!int.TryParse(selectedFps,NumberStyles.None,CultureInfo.InvariantCulture,out targetFps)||targetFps!=30&&targetFps!=60))throw new InvalidDataException("Worker-timing-fps");
   var identity=new WorkerIdentity(Guid.ParseExact(values["--sdk-attempt"],"N"),Guid.ParseExact(values["--sdk-instance"],"N"),uint.Parse(values["--sdk-generation"],CultureInfo.InvariantCulture));
   int width=int.Parse(values["--sdk-width"],CultureInfo.InvariantCulture),height=int.Parse(values["--sdk-height"],CultureInfo.InvariantCulture);pixelWidth=width;pixelHeight=height;RuntimeLimits.RequireRenderSize(width,height);WorkerFrameCodec.PayloadBytes(width,height);
   var handle=new IntPtr(long.Parse(values["--sdk-frame-handle"],CultureInfo.InvariantCulture));WindowsWorkerBoundary.Require(handle,values["--sdk-lpac-canary"],values["--sdk-lpac-control"]);
   stopReader=new WindowsWorkerStopReader(new IntPtr(long.Parse(values["--sdk-stop-handle"],CultureInfo.InvariantCulture)),identity);if(StopRequested())return;
   projectionReader=new WindowsWorkerProjectionReader(new IntPtr(long.Parse(values["--sdk-projection-handle"],CultureInfo.InvariantCulture)),identity);
   var file=new FileInfo(values["--sdk-package"]);if(file.Length<1||file.Length>64L*1024*1024)throw new InvalidDataException("Worker-package-size");
   var build=Admission.Admit(File.ReadAllBytes(file.FullName),"windows-x86_64");if(!string.Equals(build.BuildId,values["--sdk-build"],StringComparison.Ordinal))throw new InvalidDataException("Worker-build-identity");
   ExecutionBindings.Require(build,member=>RenderingBindings.Supports(member)||SpatialWallpaper.Sdk.Capabilities.CapabilityBindings.Supports(member));admitted=true;domain=new OwnedDomain(identity.Instance,identity.Generation);
   capabilities=new SpatialWallpaper.Sdk.Capabilities.WindowsCapabilityClient(domain.Ledger,identity,new IntPtr(long.Parse(values["--sdk-capability-request"],CultureInfo.InvariantCulture)),new IntPtr(long.Parse(values["--sdk-capability-reply"],CultureInfo.InvariantCulture)));
   calls=new SpatialWallpaper.Sdk.Capabilities.CapabilityCalls(domain.Ledger,capabilities);var services=new SpatialWallpaper.Sdk.Capabilities.CapabilityBindings(calls);host=new UnityOwnedHost(build,domain,services);runtime=new VmRuntime(build,domain,host,new ExecutionQuota(RuntimeLimits.Instructions,RuntimeLimits.Depth,RuntimeLimits.ExecutionMs));host.Bind(runtime);
   writer=new WindowsFrameWriter(domain.Ledger,identity,width,height,handle);
   var content=build.Content;var frame=content["frame"];var camera=new GameObject("Platform view").AddComponent<Camera>();camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=Color.black;camera.gameObject.AddComponent<UniversalAdditionalCameraData>();
   host.ConfigurePresentation(camera);
   projection=camera.gameObject.AddComponent<WindowProjection>();authoredWidth=(float)frame[0];authoredHeight=(float)frame[1];projection.displayWidth=authoredWidth;projection.displayHeight=authoredHeight;projection.contentDepth=(float)frame[2];projection.viewportManagedExternally=true;if(!projection.SetEye(new Vector3(0,0,-.6f)))throw new InvalidDataException("Worker-default-eye");
   host.ConfigureView(projection);
   capture=camera.gameObject.AddComponent<WorkerFrameCapture>();
   var renderIdentity=(Newtonsoft.Json.Linq.JObject)build.Manifest["render_profile"];
   capture.Initialize(camera,writer,host,domain,width,height,renderIdentity,(observation,hardware)=>Debug.Log("SDK_RENDER_PROFILE "+new Newtonsoft.Json.Linq.JObject{
    {"instance",identity.Instance.ToString("N")},{"attempt",identity.Attempt.ToString("N")},{"generation",identity.Generation},{"build_id",build.BuildId},
    {"profile",renderIdentity},{"observed",observation},{"hardware",hardware}}.ToString(Newtonsoft.Json.Formatting.None)),targetFps);
   if(StopRequested())return;QualitySettings.vSyncCount=0;Application.runInBackground=true;Application.targetFrameRate=targetFps;if(values.ContainsKey("--sdk-timing-fps"))timing=new WorkerFrameTiming(identity,build.BuildId,width,height,targetFps);calls.AdvanceFrame();runtime.Start();Debug.Log("SDK_WORKER_RUNNING");
  }catch(Exception error){failed=true;Debug.LogException(error);Application.Quit(Exit(error,!admitted));}}
  bool StopRequested(){if(stopReader!=null&&stopReader.Requested){stopping=true;capture?.Stop();Application.Quit(0);return true;}if(stopReader!=null&&stopReader.Error!=null)throw new IOException("Worker-stop-transport-fault",stopReader.Error);return stopping;}
  void ApplyProjection(){
   if(projectionReader.Error!=null)throw new IOException("Worker-projection-transport-fault",projectionReader.Error);var value=projectionReader.Take();if(value==null)return;
   if(value.Width!=pixelWidth||value.Height!=pixelHeight)throw new InvalidDataException("Worker-projection-frame-size");
   float scale=value.PreserveWidth?value.DisplayWidth/authoredWidth:value.DisplayHeight/authoredHeight;
   projection.displayWidth=value.DisplayWidth/scale;projection.displayHeight=value.DisplayHeight/scale;
   if(!projection.SetScaledEye(new Vector3(value.EyeX/scale,value.EyeY/scale,value.EyeZ/scale),scale))throw new InvalidDataException("Worker-projection-eye");
   capture.SetProjectionRevision(value.Revision);Debug.Log("SDK_PROJECTION_APPLIED "+value.Revision.ToString(CultureInfo.InvariantCulture));
  }
  void Update(){if(failed||runtime==null||capture==null)return;try{if(StopRequested())return;ApplyProjection();if(writer.Error!=0||capture.Error!=null)throw new InvalidOperationException("Worker-render-transport-fault");if(capabilities.Error!=0)throw new InvalidOperationException("Worker-capability-transport-fault");calls.AdvanceFrame();runtime.Update();timing?.Poll();}catch(Exception error){failed=true;Debug.LogException(error);Application.Quit(Exit(error,false));}}
  static int Exit(Exception error,bool admission){if(error is ExecutionBudgetException)return WorkerExitCodes.ExecutionBudget;if(error is ResourceBudgetException)return WorkerExitCodes.ResourceBudget;return admission?WorkerExitCodes.Admission:error.Message=="Worker-render-transport-fault"?WorkerExitCodes.Render:WorkerExitCodes.Script;}
  void OnApplicationQuit(){Exception failure=null;Action<Action> clean=action=>{try{action();}catch(Exception e){if(failure==null)failure=e;Debug.LogException(e);}};clean(()=>timing?.Flush());clean(()=>stopReader?.Dispose());clean(()=>projectionReader?.Dispose());clean(()=>capabilities?.Dispose());clean(()=>capture?.Stop());clean(()=>writer?.Dispose());clean(()=>host?.Dispose());clean(()=>runtime?.Stop());clean(()=>domain?.Dispose());if(failure!=null)Environment.Exit(WorkerExitCodes.Script);}
 }
}
