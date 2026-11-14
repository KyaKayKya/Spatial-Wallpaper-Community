using System;
using System.IO;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json.Linq;
using UnityEngine;
using UnityEngine.Rendering;
using Debug=UnityEngine.Debug;
using SpatialTracking;

// Standalone adapter for the future Runtime authority. Views never launch a camera themselves.
public sealed partial class CalibrationRuntime:MonoBehaviour,ICalibrationService {
 public CalibrationSession Session{get;private set;}
 public ICalibrationStore Store{get;private set;}
 CalibrationProfile applied;
 public CalibrationProfile Applied{get{return applied?.Copy();}private set{applied=value?.Copy();}}
 public bool Fresh{get;private set;}
 public bool CanEdit{get{return Fresh&&!Busy&&!Capturing&&Session!=null&&Session.Draft.schema_version==2&&Compatible&&ViewportValid;}}
 public bool Compatible{get;private set;}
 public bool MappingSafe{get;private set;}
 public bool CanSave{get{return CanEdit&&MappingSafe;}}
 public bool Capturing{get;private set;}
 public bool ShowingExhibit{get;private set;}
 public Transform ExternalExhibitRoot {get;set;}
 public bool MousePreview {get;set;}
 public bool Busy{get;private set;}
 public string Message{get;private set;}="请在设置中选择摄像头";
 public string CameraName{get;private set;}="未选择";
 public string CameraId{get;private set;}
 public string CameraModeId{get;private set;}
 public bool PauseWhenInvisible{get;private set;}=true;
 // Consumed by the desktop host when it is available; manager focus is not desktop visibility.
 public void SetPauseWhenInvisible(bool enabled){
  bool previous=PauseWhenInvisible;PauseWhenInvisible=enabled;
  try{PersistSettings();}catch(Exception e){PauseWhenInvisible=previous;Debug.LogWarning(e);Report("运行设置保存失败");}
 }
 public double CameraInferenceHz{get;private set;}=20;
 public JArray CameraModes{get{return RemoteDesktop?.CameraModes??host?.CameraModes??new JArray();}}
 public JObject ActualCameraMode{get{return CurrentDescriptor?["actual_mode"] as JObject;}}
 public void ApplyCameraMode(string mode){
  if(Busy||CameraId==null)return;
  var available=CameraModes;var selected=mode==null?null:System.Linq.Enumerable.FirstOrDefault(available,x=>(string)x["mode_id"]==mode);
  if(mode!=null&&selected==null){Report("此模式不在当前设备支持列表中");return;}
  double rate=selected==null?20:Math.Max(1,Math.Min(60,(double)selected["fps"]));
  CameraModeId=mode;CameraInferenceHz=rate;ApplyCamera(CameraId,CameraName);
 }
 public string Diagnostic{get{return "host="+(host?.Diagnostic??"none")+" source="+(host?.SourceState??"none")+" error="+(host?.SourceError??"none")+" pipeline="+(pipeline?.Status??"none")+" accepted="+(pipeline?.Accepted??0)+" rejected="+(pipeline?.Rejected??0);}}
 public event Action Changed;
 public WindowProjection projection;
 public Transform contentRoot,modelRoot;
 public CalibrationRoom room;
 public bool ViewportValid{get;private set;}
 public string DisplayId{get;private set;}
 TrackingPipeline pipeline;RuntimeTrackingHost host;
 bool previewVisible;
 public void SetCameraPreview(bool visible){previewVisible=visible;if(RemoteDesktop!=null)RemoteDesktop.PreviewRequested=visible;else if(host!=null)host.PreviewRequested=visible;}
 public CameraPreviewFrame TakeCameraPreview(){return RemoteDesktop!=null?RemoteDesktop.TakePreview():host?.TakePreview();}
 EyeSmoother smoother;readonly BinocularSmoother binocularSmoother=new BinocularSmoother();Vector3 basePosition;Quaternion baseRotation;
 ViewportMetrics viewport;
 string python,tracker,settingsPath;
 bool simulate,disposed,lastCanEdit,lastCanSave;
 string lastSourceError;
 StreamWriter diagnostics;RawTrackingSample diagnosticRaw;TrackingSnapshot diagnosticMapped;ulong diagnosticNext;
 public static string Argument(string name,string fallback=""){
  var args=Environment.GetCommandLineArgs();int index=Array.IndexOf(args,name);return index>=0&&index+1<args.Length?args[index+1]:fallback;
 }
 void Awake(){
  simulate=Array.IndexOf(Environment.GetCommandLineArgs(),"--calibration-simulate")>=0;
  // Keep the existing calibration store when the library's product/window name changes.
  string defaultRoot=Application.platform==RuntimePlatform.WindowsPlayer?
   Path.Combine(Path.GetDirectoryName(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData)),"LocalLow","Spatial Wallpaper","Spatial Wallpaper Calibration","CalibrationDemo"):
   Path.Combine(Application.persistentDataPath,"CalibrationDemo");
  string root=Argument("--data-root",defaultRoot);
  Store=new CalibrationStore(Path.Combine(root,"presets"));settingsPath=Path.Combine(root,"settings.json");
  python=Argument("--tracking-python");tracker=Argument("--tracking-root");
  Session=new CalibrationSession(new CalibrationProfile());smoother=new EyeSmoother(new[]{0.0,0,-.67},true);
  if(contentRoot){basePosition=contentRoot.localPosition;baseRotation=contentRoot.localRotation;}
  try{
   if(File.Exists(settingsPath)){
    var settings=JObject.Parse(File.ReadAllText(settingsPath));CameraId=(string)settings["camera_id"];CameraName=(string)settings["camera_name"]??"未选择";CameraModeId=(string)settings["camera_mode_id"];PauseWhenInvisible=(bool?)settings["pause_when_invisible"]??true;
    if(settings.ContainsKey("camera_inference_hz")){CameraInferenceHz=TrackingWire.Number(settings["camera_inference_hz"]);if(CameraInferenceHz<1||CameraInferenceHz>60)throw new InvalidDataException("INVALID_INFERENCE_RATE");}
    string selected=(string)(settings["selected_preset_id"]??settings["preset_id"]),active=(string)(settings["applied_preset_id"]??settings["preset_id"]);
    bool hasSnapshot=settings.ContainsKey("applied_profile");
    if(hasSnapshot&&settings["applied_profile"].Type!=JTokenType.Null){
     var snapshot=settings["applied_profile"].ToObject<CalibrationProfile>();
     if(snapshot==null||snapshot.schema_version!=2||!snapshot.Valid()||!snapshot.manual.Bound||snapshot.id!=active)throw new InvalidDataException("INVALID_APPLIED_PROFILE");
     Applied=snapshot;
    }
    foreach(var p in Store.List()){
     if(!hasSnapshot&&p.id==active&&p.schema_version==2)Applied=p;
     if(p.id==selected){Session=new CalibrationSession(p);if(p.schema_version==1)Message="旧预设已保留，请显式创建五参数草稿后校准";}
    }
   }
  }catch(Exception){Message="本地配置读取失败，请检查预设文件";}
  if(simulate){CameraId="simulation";CameraName="合成信号（测试）";}
  else if(!string.IsNullOrEmpty(Argument("--tracking-camera"))){CameraId=Argument("--tracking-camera");CameraName=Argument("--tracking-camera-name","摄像头");}
 }
 void Start(){
  string path=Argument("--calibration-diagnostics");
  if(!string.IsNullOrEmpty(path)){
   try{
    path=Path.GetFullPath(path);Directory.CreateDirectory(Path.GetDirectoryName(path));
    diagnostics=new StreamWriter(new FileStream(path,FileMode.CreateNew,FileAccess.Write,FileShare.Read));
    diagnostics.WriteLine("time_us,raw_present,sample_frame,sample_age_us,fresh,compatible,mapped_valid,accepted,rejected,qx,qy,qz,mapped_x,mapped_y,mapped_z,filtered_x,filtered_y,filtered_z,view_center_x,view_center_y,projection_x,projection_y,projection_z,display_width,display_height,corner_error_px,binocular_u,binocular_v,ipd_px");
    RenderPipelineManager.endFrameRendering+=RecordDiagnostics;
   }catch(Exception e){Debug.LogWarning("Calibration diagnostics unavailable: "+e.GetType().Name);}
  }
  if(simulate||!string.IsNullOrEmpty(Argument("--tracking-camera")))ApplyCamera(CameraId,CameraName);
 }
 public async Task<JArray> EnumerateCameras(){
  if(RemoteDesktop!=null)return await RemoteDesktop.EnumerateCameras();
  if(simulate)return new JArray(new JObject{["camera_id"]="simulation",["display_name"]="合成信号（测试）"});
  return await Task.Run(()=>{
   var info=new ProcessStartInfo(python,"-m spatial_tracker devices"){WorkingDirectory=tracker,UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,StandardOutputEncoding=System.Text.Encoding.UTF8};
   info.EnvironmentVariables["PYTHONPATH"]=Path.Combine(tracker,"src");
   using(var process=Process.Start(info)){
    var output=process.StandardOutput.ReadToEndAsync();var error=process.StandardError.ReadToEndAsync();
    if(!process.WaitForExit(15000)){process.Kill();throw new IOException("CAMERA_ENUMERATION_TIMEOUT");}
    if(process.ExitCode!=0)throw new IOException("CAMERA_ENUMERATION_FAILED");
    return (JArray)JObject.Parse(output.GetAwaiter().GetResult())["devices"];
   }
  });
 }
 public async void ApplyCamera(string id,string name){
  if(RemoteDesktop!=null){await ApplyRemoteCamera(id,name);return;}
  if(Busy||string.IsNullOrEmpty(id))return;Busy=true;Fresh=false;Message="正在应用相机设置";Changed?.Invoke();
  if(id!=CameraId){CameraModeId=null;CameraInferenceHz=20;}
  var old=host;host=null;pipeline=null;
  try{
   if(old!=null){await Task.Run(()=>old.Dispose());if(old.Diagnostic=="STOP_TIMEOUT")throw new IOException("RELEASE_TIMEOUT");}
   if(disposed)return;
   var pipe=new TrackingPipeline();var next=new RuntimeTrackingHost(pipe,python,tracker,simulate?"simulate":"camera",id,Argument("--tracking-recording"),Argument("--tracking-scenario","motion"));
   pipeline=pipe;host=next;next.PreviewRequested=previewVisible;next.CameraModeId=CameraModeId;next.MaxInferenceHz=CameraInferenceHz;CameraId=id;CameraName=name;Capturing=false;Session.ClearReferenceSamples();
   next.Start();PersistSettings();Message="等待摄像头与人脸";
  }catch(Exception e){Message="相机启动失败："+e.GetType().Name;}
  finally{Busy=false;Changed?.Invoke();}
 }
 public void StartReference(){
  BindCurrentSource();
 }
 public void InitializeFirstCalibration(){
  if(!CalibrationGuide.CanInitialize(Session.Draft)||!Fresh||Busy||!ViewportValid)return;
  var raw=CurrentRaw;if(raw?.Binocular==null||!raw.Fresh(QpcClock.Now))return;
  var p=Session.Draft;p.manual.Bind(raw.Descriptor,DisplayId);p.reference_ready=true;
  // Device initialization is the baseline, not an undoable user parameter adjustment.
  Session=new CalibrationSession(p);Message="";Changed?.Invoke();
 }
 public void BindCurrentSource(){
  var raw=CurrentRaw;
  if(raw?.Binocular==null||!raw.Fresh(QpcClock.Now)||Busy||!ViewportValid||Session.Draft.schema_version!=2){Report("需要当前设备的新鲜双眼测量和有效预览区域");return;}
  var p=Session.Draft;p.manual.Bind(raw.Descriptor,DisplayId);p.reference_ready=true;
  Session.BeginEdit();Session.Edit(p);Session.CommitEdit();Message="设备已绑定，请调整水平与垂直位置";Changed?.Invoke();
 }
 public void SelectPreset(CalibrationProfile p){Session=new CalibrationSession(p);Capturing=false;ShowingExhibit=false;Changed?.Invoke();}
 public void NewPreset(){SelectPreset(new CalibrationProfile());Message="请按提示调整五项参数";}
 // The two content roots share only the projection and the calibration service.
 public void SetExhibit(bool show){ShowingExhibit=show;Changed?.Invoke();}
 public bool Save(string name,bool saveAs,bool finish){
  if(!CanSave){Message="需要新鲜双眼追踪、匹配设备、有效预览和安全眼位才能保存";return false;}
  try{
   var saved=Store.Save(Session.Draft,name,saveAs);Session.MarkSaved(saved);if(finish)Applied=saved;
   Message=finish?"预设已保存并应用":"预设已保存，点击保存并完成后应用";
   try{PersistSettings();}catch(Exception){Message="预设已保存，但未能记住当前选择；重启后请手动选择";}
   if(finish)SetExhibit(true);Changed?.Invoke();return true;
  }catch(Exception e){Debug.LogWarning("Calibration preset save failed: "+e);Message=e is InvalidDataException?e.Message:"保存失败，修改仍保留";Changed?.Invoke();return false;}
 }
 void PersistSettings(){
  if(RemoteDesktop!=null){RemoteDesktop.UpdateCalibrationSelection(Session.Draft.id,Applied,PauseWhenInvisible);return;}
  var data=new JObject{["camera_id"]=CameraId,["camera_name"]=CameraName,["camera_mode_id"]=CameraModeId,["camera_inference_hz"]=CameraInferenceHz,["selected_preset_id"]=Session.Draft.id,["applied_preset_id"]=Applied?.id,["applied_profile"]=Applied==null?JValue.CreateNull():JObject.FromObject(Applied)};
  data["pause_when_invisible"]=PauseWhenInvisible;
  string temp=settingsPath+".tmp";File.WriteAllText(temp,data.ToString());if(File.Exists(settingsPath))File.Replace(temp,settingsPath,null);else File.Move(temp,settingsPath);
 }
 public void NotifyEdit(){Changed?.Invoke();}
 public void Report(string message){Message=message;Changed?.Invoke();}
 void LateUpdate(){
  var raw=CurrentRaw;ulong now=QpcClock.Now;
  string error=RemoteDesktop!=null?RemoteDesktop.SourceError:host?.SourceError;
  if(error!=lastSourceError){lastSourceError=error;if(error!=null){Message=error=="CAMERA_BUSY"?"摄像头被其他程序占用，请释放后重新应用":error=="PERMISSION_DENIED"?"摄像头权限被拒绝，请检查 Windows 隐私设置":error=="UNSUPPORTED_CAPABILITY"?"所选模式未能应用，请使用系统当前模式后重新选择":"摄像头启动失败（"+error+"），请检查 Tracker 环境后重新应用";}else Message="等待摄像头与人脸";Changed?.Invoke();}
  bool fresh=raw?.Binocular!=null&&raw.Fresh(now);if(fresh!=Fresh){Fresh=fresh;Changed?.Invoke();}
  var draft=Session.Draft;
  Compatible=raw!=null&&draft.schema_version==2&&draft.manual.Matches(raw.Descriptor,DisplayId);
  MappingSafe=fresh&&Compatible&&TrackingCalibration.SafeEye(draft.manual.Map(raw.Binocular));
  var p=ShowingExhibit?(desktopProfile??applied??new CalibrationProfile()):draft;
  bool renderCompatible=raw!=null&&p.schema_version==2&&p.manual.Matches(raw.Descriptor,DisplayId);
  TrackingSnapshot mapped=null;
  var observation=fresh?binocularSmoother.Update(raw.Stream,raw.Captured,raw.Binocular):null;
  if(fresh&&renderCompatible){var eye=p.manual.Map(observation);if(TrackingCalibration.SafeEye(eye))mapped=new TrackingSnapshot(1,true,raw.Stream,raw.Frame,raw.Captured,eye:eye);}
  smoother.SetReference(new[]{0.0,0,-.67});
  smoother.Advance(mapped,now,Time.unscaledDeltaTime);
  if(projection&&ViewportValid&&!MousePreview){
   var local=viewport.ToView(new[]{smoother.X,smoother.Y,smoother.Z});
   var eye=new Vector3((float)local[0],(float)local[1],(float)local[2]);
   projection.SetEye(eye);projection.SetExternalFreshness(mapped!=null);
  }
  if(contentRoot){contentRoot.localRotation=baseRotation;contentRoot.localPosition=basePosition;}
  if(modelRoot)modelRoot.gameObject.SetActive(ShowingExhibit&&!ExternalExhibitRoot);
  if(room)room.gameObject.SetActive(!ShowingExhibit);
  diagnosticRaw=raw;diagnosticMapped=mapped;
  UpdateVerticalContext();
  if(lastCanEdit!=CanEdit||lastCanSave!=CanSave){lastCanEdit=CanEdit;lastCanSave=CanSave;Changed?.Invoke();}
 }
 public Vector3 ViewCenter{get;set;}
 public void SetViewport(ViewportMetrics metrics,string display){
  ViewportValid=metrics!=null&&metrics.Width>=.01&&metrics.Height>=.01&&metrics.Width<=10&&metrics.Height<=10;
  viewport=ViewportValid?metrics:null;DisplayId=display;
  if(!ViewportValid){projection.SetExternalFreshness(false);return;}
  ViewCenter=new Vector3((float)metrics.CenterX,(float)metrics.CenterY,0);
  projection.displayWidth=(float)metrics.Width;projection.displayHeight=(float)metrics.Height;
  if(room&&!ShowingExhibit)room.SetDimensions((float)metrics.Width,(float)metrics.Height);
  if(modelRoot&&ShowingExhibit&&!ExternalExhibitRoot)modelRoot.localScale=Vector3.one*(float)(metrics.Width/.53);
 }
 void RecordDiagnostics(ScriptableRenderContext context,Camera[] cameras){
  if(!isActiveAndEnabled||diagnostics==null||!projection)return;var camera=projection.GetComponent<Camera>();
  if(Array.IndexOf(cameras,camera)<0)return;ulong now=QpcClock.Now;if(now<diagnosticNext)return;diagnosticNext=now+50000;
  var raw=diagnosticRaw;var mapped=diagnosticMapped;var q=raw?.Eye??new double[3];var eye=projection.EyeScreen;
  float corner=0;
  foreach(float x in new[]{-.5f,.5f})foreach(float y in new[]{-.5f,.5f}){
   var point=camera.WorldToViewportPoint(projection.screenFrame.TransformPoint(new Vector3(x*projection.displayWidth,y*projection.displayHeight,0)));
   corner=Mathf.Max(corner,new Vector2((point.x-x-.5f)*camera.pixelWidth,(point.y-y-.5f)*camera.pixelHeight).magnitude);
  }
  try{
   diagnostics.WriteLine(FormattableString.Invariant($"{now},{raw!=null},{raw?.Frame??0},{(raw!=null&&now>=raw.Captured?now-raw.Captured:0)},{Fresh},{Compatible},{mapped!=null},{pipeline?.Accepted??0},{pipeline?.Rejected??0},{q[0]},{q[1]},{q[2]},{mapped?.X??0},{mapped?.Y??0},{mapped?.Z??0},{smoother.X},{smoother.Y},{smoother.Z},{ViewCenter.x},{ViewCenter.y},{eye.x},{eye.y},{eye.z},{projection.displayWidth},{projection.displayHeight},{corner},{raw?.Binocular?.U},{raw?.Binocular?.V},{raw?.Binocular?.IpdPixels}"));
   diagnostics.Flush();
  }catch(Exception){diagnostics.Dispose();diagnostics=null;Debug.LogWarning("Calibration diagnostics stopped after write failure");}
 }
 void OnDestroy(){disposed=true;RenderPipelineManager.endFrameRendering-=RecordDiagnostics;diagnostics?.Dispose();diagnostics=null;var old=host;host=null;if(old!=null)old.Dispose();}
}
