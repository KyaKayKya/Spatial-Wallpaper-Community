using System;
using System.Linq;
using SpatialTracking;
using SpatialWallpaper.Library;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class CalibrationDemoView {
 LibraryDisplayTarget[] libraryTargets=Array.Empty<LibraryDisplayTarget>();
 string pendingLibraryArtwork,pendingLibraryDisplay,libraryActionError,libraryPresetError;
 void AddCardState(RectTransform card,Func<bool> selected,Func<bool> active){
  var check=ui.Icon(card,"Circle",card.sizeDelta.x-34,10,24,Color.white);Glyph(check.transform,LibrarySymbol.Check,0,0,24,CalibrationWidgets.Blue);
  var monitor=ui.RoundedPanel(card,"Desktop badge",10,10,28,28,Color.white);monitor.GetComponent<Graphic>().raycastTarget=false;Glyph(monitor,LibrarySymbol.Monitor,4,4,20,CalibrationWidgets.Ink);
  var visual=card.gameObject.AddComponent<LibraryCardVisual>();visual.Selected=selected;visual.Active=active;visual.Border=card.GetComponent<Evo.UI.ProceduralRect>();visual.Check=check.gameObject;visual.Monitor=monitor.gameObject;check.gameObject.SetActive(selected());monitor.gameObject.SetActive(active());
 }
 CalibrationProfile[] ReadLibraryPresets(){
  return ReadCompatiblePresets(library.TargetDisplayId);
 }
 CalibrationProfile[] ReadCompatiblePresets(string display){
  try{libraryPresetError=null;return Service.Store.List().Where(p=>p.CanUse(display,Service.CameraId,runtime.CameraModeId,runtime.CurrentDescriptor)).ToArray();}
  catch(Exception error){Debug.LogWarning(error);libraryPresetError="预设读取失败："+error.Message;return Array.Empty<CalibrationProfile>();}
 }
 string ResolvedLibraryPreset(CalibrationProfile[] presets)=>library.Bindings.ResolvePreset(library.Selected.Id,library.TargetDisplayId,presets.Select(p=>p.id),Service.Applied?.id);
 void BuildArtworkDetails(float width){
  var entry=library.Selected;float h=Mathf.Min((width-32)*9/16,Mathf.Max(48,detail.sizeDelta.y-372));Thumbnail(detail,entry,16,16,width-32,h);
  float y=h+24;ui.Text(detail,entry.Name,20,y,width-40,32,20);y+=40;
  if(library.NoWallpaperSelected){
   var action=library.StopWallpaper;var restore=ui.Button(detail,action.Busy?"正在恢复…":action.CanRestore?"恢复原壁纸":"当前使用 系统壁纸",20,y,width-40,ApplyLibrarySelection,true);restore.SetInteractable(action.CanRestore&&!library.ApplyWallpaper.Busy);
   if(!string.IsNullOrEmpty(action.Message)){var note=ui.Text(detail,action.Message,20,y+48,width-40,80,14,CalibrationWidgets.Muted);note.enableWordWrapping=true;}return;
  }
  if(entry.Id=="blueprint"){ui.Text(detail,"导入并保存蓝图后，即可选择校准预设并应用。",20,y,width-40,80,14,CalibrationWidgets.Muted);ui.Button(detail,"继续创作",20,y+96,width-40,()=>EnterArtworkPreview(false),true);return;}
  ui.Text(detail,"显示器",20,y,width-40,20,12,CalibrationWidgets.Muted);y+=24;
  var targets=libraryTargets;int target=Array.FindIndex(targets,t=>t.Id==library.TargetDisplayId);string[] displays=targets.Select(t=>t.Name).ToArray();
  if(target<0){displays=(new[]{string.IsNullOrEmpty(library.TargetDisplayId)?"无可用显示器":"显示器离线"}).Concat(displays).ToArray();}
  var display=Dropdown(detail,"Target display",20,y,width-40,()=>targets.Select(t=>new Choice(t.Name,()=>library.SelectTarget(t.Id))).ToList(),target<0?displays[0]:targets[target].Name);display.SetInteractable(targets.Length>0);y+=48;
  ui.Text(detail,"校准预设",20,y,width-40,20,12,CalibrationWidgets.Muted);y+=24;
  libraryPresetError=null;var presets=ReadLibraryPresets();string chosen=ResolvedLibraryPreset(presets);
  int selected=Array.FindIndex(presets,p=>p.id==chosen);bool missing=selected<0;
  var preset=Dropdown(detail,"Calibration preset",20,y,width-40,()=>presets.Select(p=>new Choice(p.name,()=>library.SelectCalibrationPreset(p.id))).ToList(),missing?(string.IsNullOrEmpty(chosen)?"选择预设":"预设已失效"):presets[selected].name);preset.SetInteractable(presets.Length>0);y+=56;
  bool busy=library.ApplyWallpaper.Busy||library.StopWallpaper.Busy;
  var apply=ui.Button(detail,library.ApplyWallpaper.Busy?"正在应用…":"应用到桌面",20,y,width-40,ApplyLibrarySelection,true);apply.SetInteractable(library.WallpaperHost.Available&&!busy&&target>=0);apply.name="应用到桌面";
  if(!library.WallpaperHost.Available||target<0)AddTooltip(apply.gameObject,!library.WallpaperHost.Available?"桌面后台未就绪，连接恢复后即可应用":"目标显示器未连接");y+=48;
  ui.Button(detail,"预览",20,y,width-40,()=>EnterArtworkPreview(false));y+=48;
  var request=library.ApplyWallpaper.Request;string message=libraryActionError??libraryPresetError??(request!=null&&request.ArtworkId==entry.Id&&request.DisplayId==library.TargetDisplayId?library.ApplyWallpaper.Message:null);
  if(!string.IsNullOrEmpty(message)){var note=ui.Text(detail,message,20,y,width-40,64,14,CalibrationWidgets.Muted);note.enableWordWrapping=true;y+=72;}
  if(entry.IsContent)y=BuildContentDetails(entry,width,y);detail.sizeDelta=new Vector2(width,Mathf.Max(((RectTransform)detail.parent).rect.height,y+24));
 }
 async void ApplyLibrarySelection(){
  Debug.Log("Desktop apply control: artwork="+library.Selected.Id+" available="+library.WallpaperHost.Available+" loading="+library.Loading+" busy="+(library.ApplyWallpaper.Busy||library.StopWallpaper.Busy));
  if(library.ApplyWallpaper.Busy||library.StopWallpaper.Busy||library.Loading)return;
  if(library.Selected.Id=="blueprint"){EnterArtworkPreview(false);return;}
  if(library.NoWallpaperSelected){await library.StopWallpaper.RestoreAsync();return;}
  if(!library.WallpaperHost.Available)return;
  libraryActionError=null;
  if(!LibraryDisplayCatalog.List().Any(t=>t.Id==library.TargetDisplayId)){libraryActionError="目标显示器未连接";LibraryChanged();return;}
  var presets=ReadLibraryPresets();var profile=presets.FirstOrDefault(p=>p.id==ResolvedLibraryPreset(presets));
  if(profile==null){pendingLibraryArtwork=library.Selected.Id;pendingLibraryDisplay=library.TargetDisplayId;Leave(()=>{currentPage="校准";runtime.SetExhibit(false);Build();});return;}
  var request=new WallpaperRequest(library.Selected.Id,library.TargetDisplayId,profile.id,profile.revision);await library.ApplyWallpaper.ApplyAsync(request);
  Debug.Log("Desktop apply control result: succeeded="+library.ApplyWallpaper.Succeeded+" message="+library.ApplyWallpaper.Message);
 }
 bool ContinueLibraryApplication(){
  if(pendingLibraryArtwork==null)return false;
  bool same=library.Selected.Id==pendingLibraryArtwork&&library.TargetDisplayId==pendingLibraryDisplay;pendingLibraryArtwork=null;pendingLibraryDisplay=null;
  if(!same)return false;
  library.SelectCalibrationPreset(Service.Session.Draft.id);NavigateLibrary();ApplyLibrarySelection();return true;
 }
}
