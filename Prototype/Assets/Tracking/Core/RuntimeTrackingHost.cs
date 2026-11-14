using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Text;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SpatialTracking {
 // Minimal Runtime host for the standalone/Editor showcase. Manager coordination is separate.
 public sealed partial class RuntimeTrackingHost:IDisposable {
  readonly TrackingPipeline pipeline;readonly string python,root,mode,camera,recording,scenario;
  readonly string runtime=Guid.NewGuid().ToString();
  readonly object ownership=new object();
  string lastMessage="none";string workerFailure="none";
  volatile bool stopping,sourceConnected;Thread thread;TcpListener listener;TcpClient client;Process child;bool childStarted;
#if SDK_TRACKING_TESTS
  public static Func<Process,int,bool> ChildWait;
  public static Action<Process> ChildKill;
#endif
  static bool WaitChild(Process value,int milliseconds){
#if SDK_TRACKING_TESTS
   if(ChildWait!=null)return ChildWait(value,milliseconds);
#endif
   return value.WaitForExit(milliseconds);
  }
  static void KillChild(Process value){
#if SDK_TRACKING_TESTS
   if(ChildKill!=null){ChildKill(value);return;}
#endif
   value.Kill();
  }
  public bool SourceConnected=>sourceConnected;
  void InvalidateSource(){lock(ownership){sourceConnected=false;SourceState="disconnected";}}
  readonly object previewGate=new object();CameraPreviewFrame preview;volatile bool previewRequested;
  JArray cameraModes=new JArray();
  public string CameraModeId{get;set;}
  public double MaxInferenceHz{get;set;}=20;
  public JArray CameraModes{get{lock(previewGate)return (JArray)cameraModes.DeepClone();}}
  public bool PreviewRequested{get{return previewRequested;}set{previewRequested=value;if(!value)lock(previewGate)preview=null;}}
  public CameraPreviewFrame TakePreview(){lock(previewGate){var next=preview;preview=null;return next;}}
  public string Diagnostic{get;private set;}="STARTING";
  public string SourceState{get;private set;}="starting";
  public string SourceError{get;private set;}
  public int Connections{get;private set;}
  public int Restarts{get;private set;}
  public RuntimeTrackingHost(TrackingPipeline pipe,string executable,string trackerRoot,string source,string cameraId,string recordingPath="",string faultScenario="motion"){
   pipeline=pipe;python=Path.GetFullPath(executable);root=Path.GetFullPath(trackerRoot);mode=source;camera=cameraId;recording=recordingPath;scenario=faultScenario;
   if(!File.Exists(python)||!Directory.Exists(Path.Combine(root,"src"))||(mode!="simulate"&&mode!="camera")||string.IsNullOrEmpty(camera))throw new InvalidDataException("INVALID_TRACKING_LAUNCH");
  }
  public void Start(){if(thread!=null)throw new InvalidOperationException();thread=new Thread(Run){IsBackground=true,Name="Spatial Tracking TCP"};thread.Start();}
  void Run(){
   try{
    lock(ownership){if(stopping)return;listener=new TcpListener(IPAddress.Loopback,0);listener.Start(1);}
    for(int attempt=0;!stopping&&attempt<6;attempt++){
     try{Session();}
     catch(Exception e){if(!stopping)Diagnostic=(e is IOException?"TRANSPORT_CLOSED":e.GetType().Name)+" after "+lastMessage+" worker="+workerFailure+" reason="+(e is InvalidDataException?e.Message:"IO");}
     finally{InvalidateSource();pipeline.Disconnect();lock(previewGate){preview=null;cameraModes.Clear();}ReleaseChild();}
     if(stopping)break;Restarts++;for(int i=0;i<Math.Min(50,5*(1<<attempt))&&!stopping;i++)Thread.Sleep(100);
    }
    if(!stopping)Diagnostic="RETRY_LIMIT_REACHED: "+Diagnostic;
   }catch(Exception e){if(!stopping)Diagnostic=e.GetType().Name;}
   finally{lock(ownership){if(listener!=null)listener.Stop();}pipeline.Disconnect();}
  }
  static string Quote(string arg){if(arg.IndexOf('"')>=0||arg.EndsWith("\\"))throw new InvalidDataException("INVALID_LAUNCH_PATH");return "\""+arg+"\"";}
  void Session(){
   lock(ownership){if(stopping)return;sourceConnected=false;SourceState="starting";SourceError=null;}
   string token=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N"),connection=Guid.NewGuid().ToString();
   var info=new ProcessStartInfo(python,"-m spatial_tracker "+(mode=="camera"?"run":"simulate --scenario "+scenario+(string.IsNullOrEmpty(recording)?"":" --recording "+Quote(Path.GetFullPath(recording))))){
    WorkingDirectory=root,UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true};
   info.EnvironmentVariables["PYTHONPATH"]=Path.Combine(root,"src");
   var process=new Process{StartInfo=info};
   // Drain both pipes, without copying potentially sensitive native diagnostics to Unity logs.
   process.OutputDataReceived+=(s,e)=>{};process.ErrorDataReceived+=(s,e)=>{if(e.Data!=null&&e.Data.StartsWith("Tracker command failed ("))workerFailure=e.Data.Split('(')[1].Split(')')[0];};
   lock(ownership){if(stopping){process.Dispose();return;}child=process;childStarted=false;if(!process.Start())throw new IOException("TRACKER_PROCESS_NOT_CREATED");childStarted=true;}
   process.BeginOutputReadLine();process.BeginErrorReadLine();
   var bootstrap=new JObject{["host"]="127.0.0.1",["port"]=((IPEndPoint)listener.LocalEndpoint).Port,["token"]=token,["runtime_instance_id"]=runtime}.ToString(Newtonsoft.Json.Formatting.None)+"\n";
   byte[] bootstrapBytes=new UTF8Encoding(false).GetBytes(bootstrap);
   process.StandardInput.BaseStream.Write(bootstrapBytes,0,bootstrapBytes.Length);process.StandardInput.BaseStream.Close();
   ulong deadline=QpcClock.Now+15000000;
   while(!listener.Pending()){if(stopping)return;if(process.HasExited||QpcClock.Now>deadline)throw new IOException("WORKER_START_TIMEOUT");Thread.Sleep(20);}
   var accepted=listener.AcceptTcpClient();
   lock(ownership){if(stopping){accepted.Dispose();return;}client=accepted;}
   accepted.NoDelay=true;accepted.ReceiveTimeout=6000;accepted.SendTimeout=2000;
   using(var stream=accepted.GetStream()){
    ulong rx=0,tx=0;
    var hello=TrackingWire.Read(stream);ValidateEnvelope(hello,null,null,ref rx);
    if((string)hello["type"]!="Hello"||(string)hello["kind"]!="handshake"||hello["connection_id"].Type!=JTokenType.Null||
       (string)hello["payload"]["role"]!="tracker"||(string)hello["payload"]["token"]!=token||
       !(hello["payload"]["protocol_versions"] is JArray)||!((JArray)hello["payload"]["protocol_versions"]).Any(v=>v.Type==JTokenType.Integer&&(int)v==1))throw new InvalidDataException("INVALID_HELLO");
    string instance=TrackingWire.Id(hello["sender_instance_id"]);
    Action<string,JObject,string,string> send=(name,payload,kind,request)=>{
     lock(ownership){if(stopping)return;
     var envelope=new JObject{["protocol_version"]=1,["kind"]=kind,["type"]=name,
      ["runtime_instance_id"]=runtime,["sender_instance_id"]=runtime,["connection_id"]=connection,
      ["seq"]=(++tx).ToString(),["request_id"]=request,["sent_at_us"]=QpcClock.Now.ToString(),
      ["state_revision"]=null,["payload"]=payload};TrackingWire.Write(stream,envelope);}
    };
    var optional=hello["payload"]["capabilities"]?["optional_features"] as JArray;
    var selected=new JArray();if(pipeline.WantsBinocular&&optional!=null&&optional.Any(x=>(string)x==BinocularObservation.Feature))selected.Add(BinocularObservation.Feature);
    pipeline.SetNegotiatedFeatures(selected);
    bool previewSupported=(bool?)hello["payload"]["capabilities"]?["camera_preview"]==true;
    bool modesSupported=(bool?)hello["payload"]["capabilities"]?["capture_modes"]==true;
    var capabilities=new JArray();if(previewSupported)capabilities.Add("camera_preview_v1");if(modesSupported)capabilities.Add("camera_modes_v1");
    if(CameraModeId!=null&&!modesSupported)throw new InvalidDataException("UNSUPPORTED_CAPTURE_MODE");
    send("Welcome",new JObject{["protocol_version"]=1,["connection_id"]=connection,["clock_domain"]=QpcClock.Domain,["heartbeat_interval_ms"]=2000,["capabilities"]=capabilities,["optional_features"]=selected,["state_snapshot"]=new JObject()},"handshake",null);
    send("GetState",new JObject{["scopes"]=new JArray("cameras","tracking")},"command",Guid.NewGuid().ToString());
    send("SetTrackerIntent",new JObject{["intent_revision"]="1",["camera_id"]=camera,["capture_requested"]=true,
     ["auto_resume_allowed"]=true,["capture_preference"]=new JObject{["mode_id"]=CameraModeId,["max_inference_hz"]=mode=="camera"?MaxInferenceHz:60},
     ["required_features"]=new JArray("eye_pose"),["retry_generation"]="0"},"command",Guid.NewGuid().ToString());
    lock(ownership){if(stopping)return;sourceConnected=true;}Connections++;Diagnostic="CONNECTED";ulong nextSnapshot=0;bool previewSent=false;
    while(true){
     // After the owner half-closes its send side, drain to the Worker's EOF.
     // Closing while unread samples remain can turn a clean stop into a reset.
     if(stopping){TrackingWire.Read(stream);continue;}
     if(previewSupported&&previewSent!=previewRequested){previewSent=previewRequested;send("SetCameraPreview",new JObject{["enabled"]=previewSent},"command",Guid.NewGuid().ToString());}
     JObject packet=TrackingWire.Read(stream);ulong received=QpcClock.Now;
     if(stopping)continue;
     ValidateEnvelope(packet,instance,connection,ref rx);
     lastMessage=(string)packet["type"];
     string name=(string)packet["type"],kind=(string)packet["kind"];var payload=(JObject)packet["payload"];
     if(kind=="heartbeat"&&(name=="Ping"||name=="Pong")){
      string nonce=TrackingWire.Text(payload["nonce"]);if(nonce.Length>128)throw new InvalidDataException("INVALID_NONCE");
      if(name=="Ping")send("Pong",new JObject{["nonce"]=nonce},"heartbeat",null);
     }else if(kind=="event"&&name=="TrackerStreamChanged"){
      pipeline.SetStream(payload["stream_descriptor"] as JObject,instance);
     }else if(kind=="event"&&name=="TrackerStateReport"){
      ApplyState(payload);
     }else if(kind=="event"&&name=="CameraModesChanged"&&modesSupported){
      if((string)payload["camera_id"]!=camera||!(payload["modes"] is JArray available)||available.Count>512)throw new InvalidDataException("INVALID_CAMERA_MODES");
      foreach(var item in available){
       if(!(item is JObject m)||TrackingWire.Text(m["mode_id"]).Length>128||m["width_px"]?.Type!=JTokenType.Integer||m["height_px"]?.Type!=JTokenType.Integer||(int)m["width_px"]<1||(int)m["width_px"]>16384||(int)m["height_px"]<1||(int)m["height_px"]>16384||TrackingWire.Number(m["fps"])<=0)throw new InvalidDataException("INVALID_CAMERA_MODES");
      }
      lock(previewGate)cameraModes=(JArray)available.DeepClone();
     }else if(kind=="preview"&&name=="CameraPreview"&&previewSupported){
      if(previewRequested){
       var frame=new CameraPreviewFrame(payload);var current=pipeline.Descriptor;
       if(current!=null&&frame.Stream==(string)current["stream_id"]&&received>=frame.Captured&&received-frame.Captured<=1000000)lock(previewGate)preview=frame;
      }
     }else if(kind=="sample"&&name=="TrackingSample"){
      pipeline.Offer(payload,TrackingWire.Decimal(packet["sent_at_us"]),received);
      if(pipeline.Status=="UNKNOWN_STREAM"&&received>=nextSnapshot){
       nextSnapshot=received+1000000;send("GetState",new JObject{["scopes"]=new JArray("tracking")},"command",Guid.NewGuid().ToString());
      }
     }else if(kind=="response"&&name=="OperationResult"){
      string status=TrackingWire.Text(payload["status"]);
      if(status=="failed"){Diagnostic="OPERATION_FAILED";SourceError=(string)(payload["error"] as JObject)?["code"];}
      if(status=="completed"&&payload["result"] is JObject result){
       if(result.ContainsKey("stream_descriptor"))pipeline.SetStream(result["stream_descriptor"] as JObject,instance);
       if(result["tracking"] is JObject tracking)ApplyState(tracking);
      }
     }else if(kind=="event"&&name=="CameraCatalogChanged"){
      // Catalog remains a fact; never auto-select an arbitrary device.
     }else if(kind=="command"){
      send("OperationResult",new JObject{["status"]="failed",["result"]=null,
       ["error"]=new JObject{["code"]="UNSUPPORTED_MESSAGE",["message"]="Unsupported Runtime command",["retryable"]=false,["retry_after_ms"]=null,["details"]=new JObject()}},"response",(string)packet["request_id"]);
     }else if(name=="Error")Diagnostic="TRACKER_ERROR";
     else throw new InvalidDataException("UNEXPECTED_MESSAGE");
    }
   }
  }
  void ApplyState(JObject state){
   lock(ownership){if(stopping||!sourceConnected)return;
   SourceState=(string)state["camera_state"]+" / "+(string)state["tracking_state"];
   SourceError=(string)(state["last_error"] as JObject)?["code"];
   if((string)state["camera_state"]!="streaming"||((string)state["tracking_state"]!="tracking"&&(string)state["tracking_state"]!="degraded"))pipeline.Invalidate("SOURCE_UNAVAILABLE");}
  }
  void ValidateEnvelope(JObject value,string instance,string connection,ref ulong sequence){
   foreach(string field in new[]{"protocol_version","kind","type","runtime_instance_id","sender_instance_id","connection_id","seq","request_id","sent_at_us","state_revision","payload"})if(!value.ContainsKey(field))throw new InvalidDataException("INVALID_ENVELOPE");
   if(value["protocol_version"].Type!=JTokenType.Integer||(int)value["protocol_version"]!=1||(string)value["runtime_instance_id"]!=runtime||!(value["payload"] is JObject)||value["state_revision"].Type!=JTokenType.Null)throw new InvalidDataException("INVALID_ENVELOPE");
   TrackingWire.Id(value["sender_instance_id"]);TrackingWire.Text(value["kind"]);TrackingWire.Text(value["type"]);
   ulong seq=TrackingWire.Decimal(value["seq"]);TrackingWire.Decimal(value["sent_at_us"]);
   if(seq!=sequence+1||(instance!=null&&(string)value["sender_instance_id"]!=instance)||(connection!=null&&(string)value["connection_id"]!=connection))throw new InvalidDataException("SOURCE_SEQUENCE_MISMATCH");
   if((string)value["kind"]=="response"||(string)value["kind"]=="command")TrackingWire.Id(value["request_id"]);else if(value["request_id"].Type!=JTokenType.Null)throw new InvalidDataException("INVALID_REQUEST_ID");
   sequence=seq;
  }
  void ReleaseChild(){
   lock(ownership){
    if(client!=null){client.Dispose();client=null;}
    if(child!=null){if(childStarted&&!WaitChild(child,3500)){KillChild(child);if(!WaitChild(child,1500))throw new TimeoutException("TRACKER_CHILD_EXIT_UNPROVEN");}child.Dispose();child=null;childStarted=false;}
   }
  }
  public void Dispose(){
   stopping=true;InvalidateSource();StopManagerControl();
   lock(ownership){if(client!=null)try{client.Client.Shutdown(SocketShutdown.Send);}catch(SocketException){client.Close();}catch(ObjectDisposedException){}if(listener!=null)listener.Stop();}
   if(thread!=null&&!thread.Join(7000)){Diagnostic="STOP_TIMEOUT";throw new TimeoutException("TRACKER_THREAD_EXIT_UNPROVEN");}
   ReleaseChild();
  }
 }
}
