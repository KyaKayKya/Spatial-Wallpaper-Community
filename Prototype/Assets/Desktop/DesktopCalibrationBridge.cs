using System;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Desktop;
using UnityEngine;

public sealed partial class CalibrationRuntime {
 public DesktopHostClient RemoteDesktop{get;set;}
 public RawTrackingSample CurrentRaw=>RemoteDesktop!=null?RemoteDesktop.Tracking.Pipeline.RawLatest:pipeline?.RawLatest;
 public JObject CurrentDescriptor=>RemoteDesktop!=null?RemoteDesktop.Tracking.Pipeline.Descriptor:pipeline?.Descriptor;
 public bool CameraStarted=>host!=null;
 public string DesktopSourceError=>host?.SourceError;
 CalibrationProfile desktopProfile;
 public CalibrationProfile DesktopRenderingProfile=>desktopProfile?.Copy()??Applied;
 public void UseDesktopProfile(CalibrationProfile profile){if(profile==null||!profile.Valid()||!profile.manual.Bound)throw new ArgumentException("invalid-desktop-calibration");desktopProfile=profile.Copy();}
 public void UpdateRemoteCamera(JObject state){
  CameraId=(string)state["camera_id"];CameraName=(string)state["camera_name"]??"未选择";CameraModeId=(string)state["camera_mode_id"];
 }
 async Task ApplyRemoteCamera(string id,string name){
  if(Busy||string.IsNullOrEmpty(id))return;Busy=true;Fresh=false;Message="正在应用相机设置";Changed?.Invoke();
  try{if(id!=CameraId)CameraModeId=null;await RemoteDesktop.ConfigureCamera(id,name,CameraModeId);CameraId=id;CameraName=name;Session.ClearReferenceSamples();Message="等待摄像头与人脸";}catch(Exception e){Message="相机启动失败："+e.Message;}finally{Busy=false;Changed?.Invoke();}
 }
 public void ApplyDesktopSettings(string selected,CalibrationProfile profile,bool pause){
  if(profile!=null){if(!profile.Valid()||!profile.manual.Bound)throw new ArgumentException("invalid-applied-calibration");Applied=profile;}
  foreach(var saved in Store.List())if(saved.id==selected){Session=new CalibrationSession(saved);break;}
  PauseWhenInvisible=pause;PersistSettings();
 }
 public async Task ReleaseTracking(){
  var old=host;host=null;pipeline=null;Fresh=false;
  if(old!=null){await Task.Run(()=>old.Dispose());if(old.Diagnostic=="STOP_TIMEOUT")throw new InvalidOperationException("tracking-release-timeout");}
 }
 public async Task StartDesktopCamera(string id,string name,string mode,bool simulation){
  if(Busy)throw new InvalidOperationException("camera-operation-in-progress");simulate=simulation;CameraModeId=mode;ApplyCamera(id,name);
  float deadline=Time.realtimeSinceStartup+15;while(Busy){if(Time.realtimeSinceStartup>deadline)throw new TimeoutException("camera-configure-timeout");await Task.Delay(20);}
  if(!CameraStarted)throw new InvalidOperationException(Message);
 }
}
