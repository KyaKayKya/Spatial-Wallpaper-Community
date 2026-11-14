using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public sealed partial class CalibrationDemoView {
 Evo.UI.Button cameraDropdown,resolutionDropdown,rateDropdown,refreshDevices,retryCamera;
 Evo.UI.Button defaultPresetDropdown;string defaultPresetDisplay,lastDefaultPresetKey;
 JArray cameraDevices=new JArray();bool enumerating;int enumerationVersion;string enumerationError;
 TMP_Text settingsMessage;string lastModeKey,settingsError;
 void BuildSettingsSurface(){
  var originalPage=page;float originalHeight=pageHeight;
  var viewport=ui.Panel(page,"Settings viewport",0,60,pageWidth-20,pageHeight-60,Color.clear);viewport.gameObject.AddComponent<RectMask2D>();
  pageHeight=Mathf.Max(pageHeight,pageWidth>=760?860:1160);
  page=ui.Rect(viewport,"Settings content",0,0,pageWidth-20,pageHeight);
  var scroll=viewport.gameObject.AddComponent<ScrollRect>();scroll.content=page;scroll.viewport=viewport;scroll.horizontal=false;scroll.scrollSensitivity=80;scroll.movementType=ScrollRect.MovementType.Clamped;
  var bar=(RectTransform)Instantiate(Resources.Load<GameObject>("ManagerScrollbar"),originalPage,false).transform;
  bar.anchorMin=bar.anchorMax=bar.pivot=new Vector2(0,1);bar.anchoredPosition=new Vector2(pageWidth-12,-60);bar.sizeDelta=new Vector2(12,originalHeight-60);scroll.verticalScrollbar=bar.GetComponent<Evo.UI.Scrollbar>();
  bool columns=pageWidth>=760;float controls=columns?Mathf.Min(440,(pageWidth-32)*.47f):pageWidth;
  SwitchRow("开机自启",72,()=>DesktopStartup.Available&&DesktopStartup.Enabled,value=>{
   try{DesktopStartup.Set(value);settingsError=null;return true;}catch(Exception e){Debug.LogWarning(e);settingsError="开机自启设置失败";RefreshSettingsSurface();return false;}
  },DesktopStartup.Available,"登录自启仅支持正式运行窗口");
  SwitchRow("壁纸不可见时暂停",136,()=>runtime.PauseWhenInvisible,value=>{runtime.SetPauseWhenInvisible(value);return runtime.PauseWhenInvisible==value;},ContentClient?.Available==true,"桌面后台连接后可启用");
  ui.Panel(page,"Settings divider",0,214,pageWidth,1,CalibrationWidgets.Line);
  ui.Text(page,"摄像头",0,238,controls,30,18);
  if(library){
   var displays=LibraryDisplayCatalog.List();
   if(!displays.Any(d=>d.Id==defaultPresetDisplay))defaultPresetDisplay=displays.FirstOrDefault(d=>d.Id==library.TargetDisplayId)?.Id??displays.FirstOrDefault()?.Id;
   ui.Text(page,"默认预设的显示器",0,536,controls,24,14);
   var target=Dropdown(page,"默认预设显示器",0,564,controls,()=>displays.Select(d=>new Choice(d.Name,()=>{defaultPresetDisplay=d.Id;Build();})).ToList(),displays.FirstOrDefault(d=>d.Id==defaultPresetDisplay)?.Name??"无可用显示器");target.SetInteractable(displays.Length>0);
   ui.Text(page,"默认校准预设",0,620,controls,24,14);
   defaultPresetDropdown=Dropdown(page,"默认校准预设",0,648,controls,DefaultPresetChoices);
   lastDefaultPresetKey=null;RefreshDefaultPreset();
  }
  ui.Text(page,"SDK 音频来源应用",0,708,controls,24,14);
  var audio=Dropdown(page,"SDK 音频来源",0,740,controls,LibraryAudioChoices,LibraryAudioName());audio.SetInteractable(ContentClient?.Available==true&&!contentOperation);
  cameraDropdown=Dropdown(page,"摄像头",0,278,controls-52,CameraChoices);
  refreshDevices=IconButton(page,"重新检测","refresh",controls-40,278,()=>{if(!enumerating)EnumerateSettingsCameras();});
  float left=(controls-16)*.56f,right=controls-left-16;
  ui.Text(page,"分辨率",0,338,left,28,16);ui.Text(page,"帧率",left+16,338,right,28,16);
  resolutionDropdown=Dropdown(page,"分辨率",0,374,left,ResolutionChoices);
  rateDropdown=Dropdown(page,"帧率",left+16,374,right,RateChoices);
  settingsMessage=ui.Text(page,"",0,424,controls,52,13,CalibrationWidgets.Muted);settingsMessage.enableWordWrapping=true;
  retryCamera=ui.Button(page,"重试连接",0,480,120,()=>{Service.ApplyCamera(Service.CameraId,Service.CameraName);});
  float px=columns?controls+32:0,py=columns?278:864;
  float available=pageWidth-px;
  ui.Text(page,"实时预览",px,py-40,112,32,18);
  cameraInfo=ui.Text(page,"未连接",px+120,py-40,Mathf.Max(100,available-120),32,13,CalibrationWidgets.Muted);
  cameraMaxWidth=Mathf.Min(available,740);cameraMaxHeight=Mathf.Max(80,pageHeight-py-12);cameraLeft=px;cameraTop=py;
  var box=ui.Panel(page,"Camera preview",px,py,cameraMaxWidth,Mathf.Min(cameraMaxHeight,cameraMaxWidth*.75f),new Color32(28,33,42,255));
  var r=ui.Rect(box,"Recognition image",0,0,box.sizeDelta.x,box.sizeDelta.y);cameraPreview=r.gameObject.AddComponent<RawImage>();cameraPreview.enabled=false;
  FitCameraPreview(4f/3);EnumerateSettingsCameras();RefreshSettingsSurface();
  page=originalPage;pageHeight=originalHeight;
 }
 List<Choice> DefaultPresetChoices(){
  var choices=new List<Choice>{new Choice("自动选择当前生效预设",()=>{library.Bindings.SetDefaultPreset(defaultPresetDisplay,null);lastDefaultPresetKey=null;RefreshDefaultPreset();})};
  choices.AddRange(ReadCompatiblePresets(defaultPresetDisplay).Select(p=>new Choice(p.name,()=>{library.Bindings.SetDefaultPreset(defaultPresetDisplay,p.id);lastDefaultPresetKey=null;RefreshDefaultPreset();})));return choices;
 }
 void RefreshDefaultPreset(){
  if(!library||!defaultPresetDropdown)return;
  string key=defaultPresetDisplay+"|"+Service.CameraId+"|"+runtime.CameraModeId+"|"+runtime.CurrentDescriptor?.ToString()+"|"+Service.Busy;
  if(key==lastDefaultPresetKey)return;lastDefaultPresetKey=key;
  var presets=ReadCompatiblePresets(defaultPresetDisplay);string id=defaultPresetDisplay==null?null:library.Bindings.GetDefaultPreset(defaultPresetDisplay);
  defaultPresetDropdown.GetComponentInChildren<TMP_Text>().text=libraryPresetError??presets.FirstOrDefault(p=>p.id==id)?.name??"自动选择当前生效预设";
  defaultPresetDropdown.SetInteractable(defaultPresetDisplay!=null&&!Service.Busy&&libraryPresetError==null);
 }
 void SwitchRow(string label,float y,Func<bool> read,Func<bool,bool> write,bool enabled,string disabledTip){
  var row=ui.Panel(page,label+" row",0,y,pageWidth,56,Color.white);ui.Text(row,label,20,0,pageWidth-116,56,16);
  var button=ui.Button(row,"",pageWidth-80,12,56,()=>{});button.name=label;
  var background=button.GetComponent<Evo.UI.ProceduralRect>();Destroy(button.GetComponent<CalibrationButtonVisual>());
  button.GetComponent<RectTransform>().sizeDelta=new Vector2(56,32);
  background.cornerRadius=new Vector4(16,16,16,16);var knob=ui.RoundedPanel(button.transform,"Knob",3,3,24,26,Color.white,12);knob.GetComponent<Graphic>().raycastTarget=false;
  var control=button.gameObject.AddComponent<CalibrationSwitch>();control.Track=background;control.Knob=knob;
  try{control.Set(read());}catch(Exception e){Debug.LogWarning(e);control.Set(false);enabled=false;disabledTip="无法读取开机自启状态";}
  button.onClick.AddListener(()=>{bool next=!control.Value;if(write(next))control.Set(next);});button.SetInteractable(enabled);
  if(!enabled)AddTooltip(button.gameObject,disabledTip);
 }
 async void EnumerateSettingsCameras(){
  int request=++enumerationVersion;enumerating=true;enumerationError=null;RefreshSettingsSurface();
  try{var result=await Service.EnumerateCameras();if(!this||request!=enumerationVersion)return;cameraDevices=result;}
  catch(Exception e){if(!this||request!=enumerationVersion)return;Debug.LogWarning(e);enumerationError="无法读取摄像头，请重新检测";cameraDevices=new JArray();}
  finally{if(this&&request==enumerationVersion){enumerating=false;RefreshSettingsSurface();}}
 }
 List<Choice> CameraChoices(){return cameraDevices.Select(d=>{
  string id=(string)d["camera_id"],name=(string)(d["display_name"]??d["name"])??"摄像头";
  return new Choice(name,()=>{if(cameraPreview)cameraPreview.enabled=false;cameraSummary=null;Service.ApplyCamera(id,name);});
 }).ToList();}
 List<Choice> ResolutionChoices(){
  var choices=new List<Choice>{new Choice("系统当前模式",()=>ChangeMode(null))};
  foreach(var group in runtime.CameraModes.GroupBy(m=>(int)m["width_px"]+" × "+(int)m["height_px"]).OrderByDescending(g=>(int)g.First()["width_px"]*(int)g.First()["height_px"])){
   var modes=group.ToArray();double fps=(double?)runtime.ActualCameraMode?["fps"]??30;
   var selected=modes.OrderBy(m=>Math.Abs((double)m["fps"]-fps)).First();string id=(string)selected["mode_id"];
   choices.Add(new Choice(group.Key,()=>ChangeMode(id)));
  }return choices;
 }
 List<Choice> RateChoices(){
  var mode=runtime.CameraModes.FirstOrDefault(m=>(string)m["mode_id"]==runtime.CameraModeId)??runtime.ActualCameraMode;
  if(mode==null)return new List<Choice>();int w=(int)mode["width_px"],h=(int)mode["height_px"];
  return runtime.CameraModes.Where(m=>(int)m["width_px"]==w&&(int)m["height_px"]==h).GroupBy(m=>(double)m["fps"]).OrderBy(g=>g.Key).Select(g=>{string id=(string)g.First()["mode_id"];return new Choice(g.Key.ToString("0.##",CultureInfo.InvariantCulture)+" 帧/秒",()=>ChangeMode(id));}).ToList();
 }
 void ChangeMode(string id){if(cameraPreview)cameraPreview.enabled=false;cameraSummary=null;runtime.ApplyCameraMode(id);RefreshSettingsSurface();}
 void RefreshSettingsSurface(){
  if(currentPage!="设置"||!cameraDropdown)return;
  RefreshDefaultPreset();
  cameraDropdown.GetComponentInChildren<TMP_Text>().text=enumerating?"正在查找…":Service.CameraId==null?(cameraDevices.Count==0?"未发现摄像头":"选择摄像头"):Service.CameraName;
  cameraDropdown.SetInteractable(!enumerating&&!Service.Busy&&cameraDevices.Count>0);refreshDevices.SetInteractable(!enumerating&&!Service.Busy);
  var mode=runtime.CameraModes.FirstOrDefault(m=>(string)m["mode_id"]==runtime.CameraModeId)??runtime.ActualCameraMode;
  resolutionDropdown.GetComponentInChildren<TMP_Text>().text=mode==null?"—":runtime.CameraModeId==null?"系统当前模式":(int)mode["width_px"]+" × "+(int)mode["height_px"];
  rateDropdown.GetComponentInChildren<TMP_Text>().text=mode==null?"—":((double)mode["fps"]).ToString("0.##",CultureInfo.InvariantCulture)+" 帧/秒";
  resolutionDropdown.SetInteractable(!Service.Busy&&runtime.CameraModes.Count>0);rateDropdown.SetInteractable(!Service.Busy&&RateChoices().Count>0);
  string key=Service.CameraId+"|"+Service.Busy+"|"+runtime.CameraModeId+"|"+runtime.CameraModes.ToString();
  if(lastModeKey!=key){lastModeKey=key;CloseChoices();}
  bool receiving=cameraSummary!=null&&Time.realtimeSinceStartup-cameraReceived<=1;
  string message=settingsError??enumerationError??(Service.Busy?"正在切换摄像头…":!receiving&&Service.CameraId!=null?Service.Message:"");
  if(settingsMessage)settingsMessage.text=string.IsNullOrEmpty(message)?runtime.RemoteDesktop?.CapabilityWarning??"":message;
  retryCamera.gameObject.SetActive(!Service.Busy&&Service.CameraId!=null&&!receiving);
  if(cameraInfo&&Service.Busy)cameraInfo.text="连接中…";
 }
}
