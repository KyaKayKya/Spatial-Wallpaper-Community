using System;
using System.Collections;
using System.Globalization;
using System.IO;
using UnityEngine;
using UnityEngine.Rendering;
using SpatialTracking;

[DefaultExecutionOrder(-100)]
public sealed class TrackingEyeDriver:MonoBehaviour {
 WindowProjection projection;AdaptiveRenderCadence cadence;PreviewEyeDriver preview;
 RuntimeTrackingHost host;TrackingPipeline pipeline;EyeSmoother filter;
 TrackingSnapshot applied;long seen;ulong appliedAt;float started;
 bool startedOnce,calibrationMeasured;Vector3 viewCenter,referenceDisplayEye;string sourceMode;
 StreamWriter evidence;string output;bool captureFrames;float seconds;int frame;
 public string State{get{return pipeline==null?"OFF":filter.Fresh?"TRACKING":pipeline.Status;}}
 public bool OwnsEye=>isActiveAndEnabled&&pipeline!=null&&filter!=null&&host!=null;
 public Vector3 EyeInView=>OwnsEye?new Vector3((float)filter.X,(float)filter.Y,(float)filter.Z)-viewCenter:projection.referenceEye;
 public bool CameraStarted=>OwnsEye&&sourceMode=="camera"&&host.SourceConnected&&host.SourceState.StartsWith("streaming / ",StringComparison.Ordinal);
 public Newtonsoft.Json.Linq.JObject Snapshot=>new Newtonsoft.Json.Linq.JObject{{"state",State},{"source_mode",sourceMode??"off"},{"source_state",host?.SourceState},{"camera_started",CameraStarted},{"calibration_measured",calibrationMeasured},{"accepted",pipeline?.Accepted??0},{"rejected",pipeline?.Rejected??0},{"connections",host?.Connections??0},{"restarts",host?.Restarts??0},{"diagnostic",host?.Diagnostic}};
 // The content owner measures the actual viewport; the existing filter remains
 // in display metres and its result is converted to view coordinates once.
 public void SetViewportCenter(Vector3 center){if(!OwnsEye)return;viewCenter=center;projection.referenceEye=referenceDisplayEye-center;}
 static string Arg(string name,string fallback=""){
  var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);
  return index>=0&&index+1<args.Length?args[index+1]:fallback;
 }
 [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
 static void Install(){
  UnityEngine.SceneManagement.SceneManager.sceneLoaded-=SceneLoaded;
  UnityEngine.SceneManagement.SceneManager.sceneLoaded+=SceneLoaded;
  Attach();
 }
 static void SceneLoaded(UnityEngine.SceneManagement.Scene scene,UnityEngine.SceneManagement.LoadSceneMode mode){Attach();}
 static void Attach(){
  if(Array.IndexOf(Environment.GetCommandLineArgs(),"--tracking")<0)return;
  var projection=FindObjectOfType<WindowProjection>();
  if(projection&&!projection.GetComponent<TrackingEyeDriver>())projection.gameObject.AddComponent<TrackingEyeDriver>();
 }
 void OnEnable(){if(startedOnce)Start();}
 void Start(){
  startedOnce=true;
  try{
   if(host!=null)throw new InvalidOperationException("previous-tracking-owner-not-released");
   projection=GetComponent<WindowProjection>();cadence=GetComponent<AdaptiveRenderCadence>();preview=GetComponent<PreviewEyeDriver>();
   string mode=Arg("--tracking-mode","simulate");sourceMode=mode;
   string profile=Arg("--tracking-calibration",mode=="simulate"?Path.Combine(Application.streamingAssetsPath,"tracking-calibration-synthetic.json"):"");
   if(string.IsNullOrEmpty(profile))throw new InvalidDataException("Camera mode requires explicit local calibration");
   var calibration=TrackingCalibration.Load(profile);
   pipeline=new TrackingPipeline(calibration);filter=new EyeSmoother(calibration.e0);calibrationMeasured=calibration.measured;referenceDisplayEye=new Vector3((float)calibration.e0[0],(float)calibration.e0[1],(float)calibration.e0[2]);
   viewCenter=new Vector3((float)calibration.view_center_m[0],(float)calibration.view_center_m[1],0);
   projection.referenceEye=referenceDisplayEye-viewCenter;
   projection.ResetTrackingClock();projection.SetExternalFreshness(false);
   if(preview)preview.externalTracking=true;
   host=new RuntimeTrackingHost(pipeline,Arg("--tracking-python"),Arg("--tracking-root"),mode,
    mode=="simulate"?"simulation":Arg("--tracking-camera"),Arg("--tracking-recording"),Arg("--tracking-scenario","motion"));
   output=Arg("--tracking-output");
   if(!string.IsNullOrEmpty(output)){
    output=Path.GetFullPath(output);Directory.CreateDirectory(output);
    evidence=new StreamWriter(Path.Combine(output,"frames.csv"));
    evidence.WriteLine("frame,stream,sampleFrame,captured_us,processed_us,sent_us,received_us,applied_us,render_us,fresh,targetX,targetY,targetZ,eyeX,eyeY,eyeZ,cornerErrorPx,parallaxErrorPx,accepted,rejected,connections,restarts");
    File.WriteAllText(Path.Combine(output,"conditions.txt"),$"Unity {Application.unityVersion}; {SystemInfo.operatingSystem}; {SystemInfo.processorType}; {SystemInfo.graphicsDeviceName}; {Screen.width}x{Screen.height}; {QualitySettings.activeColorSpace}; {SystemInfo.graphicsDeviceType}\nmode={mode}; calibrationMeasured={calibration.measured}; timestamp=frame_arrival; render_us=endFrameRendering before physical presentation\n");
   }
   captureFrames=Array.IndexOf(Environment.GetCommandLineArgs(),"--tracking-frames")>=0;
   float.TryParse(Arg("--tracking-seconds","0"),NumberStyles.Float,CultureInfo.InvariantCulture,out seconds);
   if(captureFrames&&!string.IsNullOrEmpty(output))Directory.CreateDirectory(Path.Combine(output,"frames"));
   RenderPipelineManager.endFrameRendering+=Rendered;started=Time.unscaledTime;host.Start();
   if(captureFrames)StartCoroutine(Capture());
  }catch(Exception e){Debug.LogError("Tracking startup failed: "+e.GetType().Name+"; check launch paths and calibration.");enabled=false;if(preview)preview.externalTracking=false;}
 }
 void Update(){
  if(pipeline==null)return;
  ulong now=QpcClock.Now;var snapshot=pipeline.Latest;
  filter.Advance(snapshot,now,Time.unscaledDeltaTime);
  var eye=new Vector3((float)filter.X,(float)filter.Y,(float)filter.Z)-viewCenter;
  if(snapshot.Version!=seen){
   seen=snapshot.Version;
   if(filter.Fresh){
    if(applied!=null&&snapshot.Stream!=applied.Stream)projection.ResetTrackingClock();
    applied=snapshot;appliedAt=now;
    projection.ApplyScreenEyeSample(eye,snapshot.Captured/1000000.0);
    if(cadence&&(eye-projection.EyeScreen).sqrMagnitude>.00000001f)cadence.Wake();
   }
  }
  projection.SetEye(eye);projection.SetExternalFreshness(filter.Fresh);
  if(seconds>0&&Time.unscaledTime-started>=seconds){WriteSummary();Application.Quit();}
 }
 void Rendered(ScriptableRenderContext context,Camera[] cameras){
  if(evidence==null||projection==null||Array.IndexOf(cameras,GetComponent<Camera>())<0)return;
  var camera=GetComponent<Camera>();float corner=0,parallax=0;
  foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
   var q=camera.WorldToViewportPoint(projection.screenFrame.TransformPoint(new Vector3(x*projection.displayWidth,y*projection.displayHeight,0)));
   corner=Mathf.Max(corner,new Vector2((q.x-x-.5f)*camera.pixelWidth,(q.y-y-.5f)*camera.pixelHeight).magnitude);
  }
  var eye=projection.EyeScreen;float d=-eye.z;
  foreach(float z in new[]{.035f,.10f,.18f}){
   var p=new Vector3(.031f,-.017f,z);var q=camera.WorldToViewportPoint(projection.screenFrame.TransformPoint(p));
   float px=(d*p.x+z*eye.x)/(d+z),py=(d*p.y+z*eye.y)/(d+z);
   parallax=Mathf.Max(parallax,new Vector2((q.x-.5f-px/projection.displayWidth)*camera.pixelWidth,(q.y-.5f-py/projection.displayHeight)*camera.pixelHeight).magnitude);
  }
  var s=applied;
  evidence.WriteLine(FormattableString.Invariant($"{frame++},{s?.Stream},{s?.Frame??0},{s?.Captured??0},{s?.Processed??0},{s?.Sent??0},{s?.Received??0},{appliedAt},{QpcClock.Now},{filter.Fresh},{(s?.X??0)-viewCenter.x},{(s?.Y??0)-viewCenter.y},{s?.Z??0},{eye.x},{eye.y},{eye.z},{corner},{parallax},{pipeline.Accepted},{pipeline.Rejected},{host.Connections},{host.Restarts}"));
 }
 IEnumerator Capture(){
  int index=0;
  while(enabled){
   yield return new WaitForEndOfFrame();
   if(index++%2!=0||string.IsNullOrEmpty(output))continue;
   var active=RenderTexture.active;RenderTexture.active=null;
   var image=new Texture2D(Screen.width,Screen.height,TextureFormat.RGB24,false);
   image.ReadPixels(new Rect(0,0,Screen.width,Screen.height),0,0);image.Apply();RenderTexture.active=active;
   File.WriteAllBytes(Path.Combine(output,"frames",frame.ToString("D5")+".png"),image.EncodeToPNG());Destroy(image);
  }
 }
 void WriteSummary(){
  if(evidence!=null){evidence.Flush();File.WriteAllText(Path.Combine(output,"complete.txt"),$"accepted={pipeline.Accepted}; rejected={pipeline.Rejected}; connections={host.Connections}; restarts={host.Restarts}; state={State}; diagnostic={host.Diagnostic}\n");}
 }
 void OnDisable(){Shutdown();}
 void OnDestroy(){Shutdown();}
 public void StopTracking(){Shutdown();}
 void Shutdown(){
  StopAllCoroutines();
  RenderPipelineManager.endFrameRendering-=Rendered;
  if(host!=null)host.Dispose();WriteSummary();host=null;pipeline=null;filter=null;seen=0;applied=null;frame=0;
  if(evidence!=null){evidence.Dispose();evidence=null;}
  if(projection){projection.ClearExternalFreshness();projection.ResetTrackingClock();}if(preview)preview.externalTracking=false;
 }
}
