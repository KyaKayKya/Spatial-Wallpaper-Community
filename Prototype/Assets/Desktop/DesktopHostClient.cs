using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Content;
using SpatialWallpaper.Library;

namespace SpatialWallpaper.Desktop {
 public sealed class DesktopHostClient:IWallpaperHost,IDisposable {
  ContentHostClient client;JObject state=new JObject();JObject selection;CameraPreviewFrame preview;
  public readonly DesktopTrackingState Tracking=new DesktopTrackingState();
  public bool PreviewRequested{get;set;}
  public bool TrackingRequested{get;set;}
  public bool Available=>client!=null&&(bool?)state["ready"]==true;
  public string ActiveArtworkId=>(string)state["active_artwork"];
  public string SourceError{get;private set;}
  public string CapabilityWarning=>(string)state["capability_warning"];
  public JArray CameraModes=>state["camera_modes"] as JArray??new JArray();
  public JObject State=>(JObject)state.DeepClone();
  public JObject ContentState {get;private set;}
  public event Action ContentChanged;
  DateTime nextContentPoll=DateTime.MinValue;
  public SpatialWallpaper.Sdk.Capabilities.SoundMix Sound(string work)=>ContentState?["sound_mixes"]?[work]==null?new SpatialWallpaper.Sdk.Capabilities.SoundMix():SpatialWallpaper.Sdk.Capabilities.SoundMix.Parse(ContentState["sound_mixes"][work]);
  public async Task RefreshContent(){
   var next=await client.Library();bool changed=!JToken.DeepEquals(ContentState?["state"]?["works"],next["state"]["works"]);ContentState=next;
   ArtworkCatalog.Content=()=>((JObject)ContentState["state"]["works"]).Properties().Select(p=>{string rev=(string)p.Value["latest_revision"];var alias=(ContentState["bundled_catalog"] as JObject)?.Properties().FirstOrDefault(entry=>(string)entry.Value["work_id"]==p.Name);string cover=alias==null?null:(string)alias.Value["cover_id"]??alias.Name;return Artwork.FromContent(p.Name,(string)p.Value["name"],rev,(string)ContentState["state"]["revisions"][rev]["build_id"],cover,(string)ContentState["state"]["builds"][(string)ContentState["state"]["revisions"][rev]["build_id"]]?["kind"]=="native-v5");}).ToArray();
   if(changed)ContentChanged?.Invoke();
  }
  public async Task<JObject> ContentCall(string type,JObject payload){if(client==null)throw new IOException("桌面运行端未连接");return await client.Call(type,payload);}
  public event Action Changed;
  public async Task Connect(){
   if(client==null)client=await Task.Run(()=>new ContentHostClient(DesktopRuntime.Descriptor));
   await Refresh();
   if(Available)await RefreshContent();
  }
  public async Task<JObject> Call(string type,JObject payload){
   if(client==null)throw new IOException("桌面运行端未连接");
   try{return await client.Call(type,payload);}catch(Exception){Disconnect();throw;}
  }
  void UpdateState(JObject next){bool changed=(bool?)state["ready"]!=(bool?)next["ready"]||(string)state["active_artwork"]!=(string)next["active_artwork"]||(long?)state["revision"]!=(long?)next["revision"]||(string)state["error"]!=(string)next["error"];state=(JObject)next.DeepClone();if(changed)Changed?.Invoke();}
  public async Task Refresh(){UpdateState(await Call("GetDesktopState",new JObject()));}
  public async Task Poll(){
   await FlushSelection();var next=await Call("GetDesktopTracking",new JObject{["preview"]=PreviewRequested,["consume"]=TrackingRequested||PreviewRequested});UpdateState(next);Tracking.Accept(next["tracking"] as JObject,QpcClock.Now);SourceError=(string)next["source_error"];
   if(PreviewRequested){var image=await Call("GetDesktopPreview",new JObject());if(image["frame"] is JObject frame)preview=new CameraPreviewFrame(frame);}
   if(DateTime.UtcNow>=nextContentPoll){nextContentPoll=DateTime.UtcNow.AddSeconds(1);await RefreshContent();}
  }
  public CameraPreviewFrame TakePreview(){var current=preview;preview=null;return current;}
  public async Task<JArray> EnumerateCameras()=>(JArray)(await Call("EnumerateDesktopCameras",new JObject()))["cameras"];
  public async Task ConfigureCamera(string id,string name,string mode){UpdateState(await Call("ConfigureDesktopCamera",new JObject{["camera_id"]=id,["camera_name"]=name,["mode_id"]=mode,["simulate"]=id=="simulation"}));}
  public void UpdateCalibrationSelection(string selected,CalibrationProfile applied,bool pause){selection=new JObject{["selected"]=selected,["applied"]=applied==null?JValue.CreateNull():(JToken)JObject.FromObject(applied),["pause_when_invisible"]=pause};}
  async Task FlushSelection(){var pending=selection;if(pending==null)return;await Call("DesktopCalibrationSelection",pending);if(ReferenceEquals(pending,selection))selection=null;}
  public async Task<WallpaperResult> ApplyAsync(WallpaperRequest request){await FlushSelection();var result=await Call("ApplyDesktop",new JObject{["artwork_id"]=request.ArtworkId,["display_id"]=request.DisplayId,["preset_id"]=request.CalibrationPresetId,["preset_revision"]=request.CalibrationPresetRevision});UpdateState(result);return new WallpaperResult((bool)result["succeeded"],(string)result["message"]);}
  public async Task<WallpaperResult> StopAsync(){var result=await Call("StopDesktop",new JObject());UpdateState(result);return new WallpaperResult((bool)result["succeeded"],(string)result["message"]);}
  public async Task BeginPreview(){UpdateState(await Call("BeginDesktopPreview",new JObject()));}
  public async Task EndPreview(){UpdateState(await Call("EndDesktopPreview",new JObject()));}
  public void Disconnect(){client?.Dispose();client=null;state["ready"]=false;Tracking.Accept(null,QpcClock.Now);Changed?.Invoke();}
  public void Dispose(){Disconnect();}
 }
}
