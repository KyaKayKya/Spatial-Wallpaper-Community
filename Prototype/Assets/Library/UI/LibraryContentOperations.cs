using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Content;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Library;
using SpatialWallpaper.Sdk.Capabilities;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public sealed partial class CalibrationDemoView {
 bool contentOperation,librarySoundGesture,libraryDetailsPending;
 string detailIdentity;
 TMP_Text contentPlayingStatus;
 DesktopHostClient ContentClient=>GetComponent<DesktopManager>()?.Client;
 void InitializeLibraryContent(){
  if(ContentClient==null)return;
  var drop=GetComponent<PackageDropReceiver>();if(!drop){drop=gameObject.AddComponent<PackageDropReceiver>();drop.Dropped+=paths=>{if(currentPage=="作品库")ImportLibraryContent(paths,null,null);};drop.Error+=message=>{libraryActionError=message;LibraryChanged();};}
  ContentClient.ContentChanged-=LibraryContentChanged;ContentClient.ContentChanged+=LibraryContentChanged;
  library.ContentPreviewEscape=()=>{if(fullscreen)ToggleFullscreen();else NavigateLibrary();};
 }
 void LibraryContentChanged(){
  if(!this||!library)return;var selected=library.Catalog.FirstOrDefault(x=>x.Id==library.Selected.Id);
  if(selected!=null&&(selected.Id!=library.Selected.Id||selected.RevisionId!=library.Selected.RevisionId))library.Select(selected);
  if(currentPage=="作品库"){RebuildGrid();LibraryChanged();}
 }
 async void ChooseLibraryContent(bool update){
  if(contentOperation||ContentClient==null)return;string work=update?library.Selected.Id:null,revision=update?library.Selected.RevisionId:null;
  try{string path=await SpatialWallpaper.Blueprint.BlueprintFilePicker.ChooseAsync("选择 SDK 作品包","Spatial Wallpaper 作品包\0*.spatial\0\0");if(path!=null)ImportLibraryContent(new[]{path},work,revision);}
  catch(Exception failure){libraryActionError="选择作品包失败："+failure.GetBaseException().Message;LibraryChanged();}
 }
 async void ImportLibraryContent(string[] paths,string work,string expected){await ImportLibraryContentAsync(paths,work,expected);}
 async Task ImportLibraryContentAsync(string[] paths,string work,string expected,Action<string> installed=null){
  if(contentOperation||ContentClient==null)return;contentOperation=true;
  try{
   if(paths.Length<1||paths.Length>16||paths.Any(p=>!string.Equals(Path.GetExtension(p),".spatial",StringComparison.OrdinalIgnoreCase)))throw new InvalidDataException("请选择 .spatial 作品包；蓝图请使用导入蓝图。");
   string selected=null;foreach(string path in paths){string id=Guid.NewGuid().ToString("D"),target=Path.Combine(DesktopRuntime.ContentRoot,"staging",id+".spatial");Directory.CreateDirectory(Path.GetDirectoryName(target));
    try{await Task.Run(()=>{using(var source=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){if(source.Length<1||source.Length>SpatialWallpaper.Sdk.Hosting.RuntimePackageInspection.MaxArchiveBytes)throw new InvalidDataException("作品包大小不受支持");using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))source.CopyTo(output);}});
     var payload=new JObject{{"staging_id",id}};if(work==null){string name=Path.GetFileNameWithoutExtension(path);payload["name"]=name.Substring(0,Math.Min(128,name.Length));}else{payload["work_id"]=work;payload["expected_revision"]=expected;}
     var imported=await ContentClient.ContentCall(work==null?"ImportContent":"UpdateContent",payload);selected=(string)imported["work_id"];
    }finally{if(File.Exists(target))File.Delete(target);}
   }
   await ContentClient.RefreshContent();if(selected!=null){library.Select(ArtworkCatalog.Find(selected));installed?.Invoke(selected);}libraryActionError=work==null?"已导入并选中，点击应用到桌面后才会播放。":"已更新，存档和音量保留。当前播放修订不变，点击应用切换。";
  }catch(Exception failure){libraryActionError="作品包操作失败，原作品保留。"+failure.GetBaseException().Message;Debug.LogWarning(failure);}
  finally{contentOperation=false;LibraryChanged();}
 }
 void ConfirmLibraryRemoval(string work){
  if(contentOperation)return;CloseModal();modal=ui.Panel(canvas,"Uninstall confirmation",0,0,layoutWidth,layoutHeight,new Color(0,0,0,.25f));
  float w=Mathf.Min(560,pageWidth-32);var panel=ui.Panel(modal,"Uninstall choices",Mathf.Max(16,(layoutWidth/DisplayViewport.UiScale-w)/2),180,w,240,CalibrationWidgets.Surface);
  ui.Text(panel,"卸载此作品？",24,16,w-48,40,22);var note=ui.Text(panel,"将停止此作品，删除全部修订和本机存档。\n其他作品不受影响；重新导入将从新存档开始。",24,68,w-48,88,15);note.enableWordWrapping=true;
  ui.Button(panel,"取消",24,176,120,CloseModal);ui.Button(panel,"卸载并删除存档",w-212,176,188,()=>{CloseModal();RemoveLibraryContent(work);});
 }
 string LibraryAudioName(){var source=ContentClient?.ContentState?["audio_source"];if(source==null||source.Type==JTokenType.Null)return "不采集音频";try{SpatialWallpaper.Sdk.Hosting.TrustedAudioSource.RequireLive(source);using(var process=System.Diagnostics.Process.GetProcessById((int)source["process"]))return process.ProcessName;}catch{return "所选应用已退出";}}
 System.Collections.Generic.List<Choice> LibraryAudioChoices(){
  var choices=new System.Collections.Generic.List<Choice>{new Choice("不采集音频",()=>SetLibraryAudio(null))};
  try{foreach(JObject item in SpatialWallpaper.Sdk.Hosting.TrustedAudioSource.Applications()){var source=(JObject)item["source"].DeepClone();choices.Add(new Choice((string)item["name"],()=>SetLibraryAudio(source)));}}catch(Exception failure){settingsError="无法读取音源应用："+failure.GetBaseException().Message;}return choices;
 }
 async void SetLibraryAudio(JObject source){if(contentOperation||ContentClient==null)return;contentOperation=true;try{await ContentClient.ContentCall("SetAudioSource",new JObject{{"source",source}});await ContentClient.RefreshContent();settingsError=source==null?"已停止音频采集。":"仅采集所选应用；应用退出后保持静音。";}catch(Exception failure){settingsError="音源切换未完成："+failure.GetBaseException().Message;}finally{contentOperation=false;if(currentPage=="设置")Build();}}
 async void ExportLibraryContent(string work,string revision){if(contentOperation||ContentClient==null)return;contentOperation=true;try{var exported=await ContentClient.ContentCall("ExportContent",new JObject{{"work_id",work},{"revision_id",revision}});string path=Path.Combine(DesktopRuntime.ContentRoot,"exports",ContentStore.Id((string)exported["export_id"])+".spatial");if(!File.Exists(path))throw new IOException("导出文件不存在");libraryActionError="已导出作品包，不含本机存档和服务配置："+path;using(var process=System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("explorer.exe","/select,\""+path+"\""){UseShellExecute=true})){};}catch(Exception failure){libraryActionError="导出未完成："+failure.GetBaseException().Message;}finally{contentOperation=false;LibraryChanged();}}
 async void RemoveLibraryContent(string work){
  if(contentOperation)return;contentOperation=true;
  try{var removed=await ContentClient.ContentCall("RemoveContent",new JObject{{"work_id",work}});await ContentClient.RefreshContent();await ContentClient.Refresh();
   if((bool?)removed["succeeded"]==true){if(library.Selected.Id==work)library.SelectNoWallpaper();libraryActionError="作品和存档已卸载。";}
   else libraryActionError="卸载未完成："+(string)removed["diagnostic"]+"。请刷新后重试。";
  }catch(Exception failure){libraryActionError="卸载结果未确认，请刷新后重试。"+failure.GetBaseException().Message;}
  finally{contentOperation=false;LibraryChanged();}
 }
 float BuildContentDetails(Artwork artwork,float width,float y){
  var client=ContentClient;if(client?.ContentState==null)return y;
  var state=client.ContentState["state"];var build=state["builds"]?[artwork.BuildId];
  contentPlayingStatus=ui.Text(detail,"",20,y,width-40,32,14,CalibrationWidgets.Muted);RefreshContentPlaying();y+=40;
  if((build?["capabilities"] as JArray)?.Any(t=>(string)t=="sound-playback")==true){
   ui.Text(detail,"声音",20,y,width-40,28,16);y+=36;var controls=ui.Rect(detail,"Work sound owner",0,0,0,0).gameObject.AddComponent<LibraryWorkSoundControls>();controls.Client=client;controls.Work=artwork.Id;controls.Gesture=value=>{librarySoundGesture=value;if(!value&&libraryDetailsPending){libraryDetailsPending=false;LibraryChanged();}};
   var mix=client.Sound(artwork.Id);controls.Music=mix.Music;controls.Effects=mix.Effects;controls.MuteMusic=mix.MusicMuted;controls.MuteEffects=mix.EffectsMuted;
   controls.MusicLabel=ui.Text(detail,"配乐 "+Mathf.RoundToInt(mix.Music*100)+"%",20,y,width-40,24,14);y+=28;
   controls.MusicSlider=ui.Slider(detail,20,y,width-128,0,1,mix.Music,value=>{controls.Music=value;controls.RefreshLabels();controls.Queue();},()=>controls.Gesture(true),()=>controls.Gesture(false));controls.MusicSlider.name="作品配乐音量";
   controls.MusicMute=ui.Button(detail,mix.MusicMuted?"取消静音":"静音",width-96,y,76,()=>{controls.MuteMusic=!controls.MuteMusic;controls.RefreshLabels();controls.Queue();});y+=48;
   controls.EffectsLabel=ui.Text(detail,"音效 "+Mathf.RoundToInt(mix.Effects*100)+"%",20,y,width-40,24,14);y+=28;
   controls.EffectsSlider=ui.Slider(detail,20,y,width-128,0,1,mix.Effects,value=>{controls.Effects=value;controls.RefreshLabels();controls.Queue();},()=>controls.Gesture(true),()=>controls.Gesture(false));controls.EffectsSlider.name="作品音效音量";
   controls.EffectsMute=ui.Button(detail,mix.EffectsMuted?"取消静音":"静音",width-96,y,76,()=>{controls.MuteEffects=!controls.MuteEffects;controls.RefreshLabels();controls.Queue();});y+=44;
   controls.Feedback=ui.Text(detail,"",20,y,width-40,44,12,CalibrationWidgets.Muted);controls.Feedback.enableWordWrapping=true;y+=52;
  }
  ui.Button(detail,"更新作品包",20,y,width-40,()=>ChooseLibraryContent(true));y+=48;
  ui.Button(detail,"卸载作品",20,y,width-40,()=>ConfirmLibraryRemoval(artwork.Id));y+=48;
  if((string)build?["kind"]!="sandbox-v4"){ui.Button(detail,"导出授权作品包",20,y,width-40,()=>ExportLibraryContent(artwork.Id,artwork.RevisionId));y+=48;}
  if(artwork.CoverId!=null){ui.Button(detail,"使用原版回退",20,y,width-40,()=>library.Select(ArtworkCatalog.Find(artwork.CoverId)));y+=48;}
  ui.Text(detail,"参数编辑、历史修订选择和 SDK 再分享授权尚未提供。",20,y,width-40,60,12,CalibrationWidgets.Muted).enableWordWrapping=true;return y+76;
 }
 void RefreshContentPlaying(){
  if(!contentPlayingStatus||ContentClient==null||!library.Selected.IsContent)return;
  var state=ContentClient.State;bool selectedPlaying=ContentClient.ActiveArtworkId==library.Selected.Id;string pending=selectedPlaying&&(string)state["content_revision"]!=library.Selected.RevisionId?" · 更新待应用":"";contentPlayingStatus.text=(!selectedPlaying?"已导入 · 尚未应用":(bool?)state["preview"]==true?"已应用 · 预览时暂时让出":(bool?)state["paused"]==true?"已应用 · 已暂停":(bool?)state["content_ready"]==true?"正在播放":"已应用 · 当前未播放")+pending;
 }
}

public sealed class LibraryWorkSoundControls:MonoBehaviour {
 public DesktopHostClient Client;public string Work;public float Music,Effects;public bool MuteMusic,MuteEffects;public Action<bool> Gesture;
 public TMP_Text MusicLabel,EffectsLabel,Feedback;public Evo.UI.Slider MusicSlider,EffectsSlider;public Evo.UI.Button MusicMute,EffectsMute;
 bool writing;JObject pending;
 void Update(){if(Client?.ActiveArtworkId!=Work||!Feedback)return;string failure=(string)Client.ContentState?["sound_error"];if(failure!=null)Feedback.text="设置已保存，播放声音暂不可用："+failure;else if(Feedback.text.StartsWith("设置已保存，播放声音暂不可用：",StringComparison.Ordinal))Feedback.text="已保存";}
 public void RefreshLabels(){if(!this)return;MusicLabel.text="配乐 "+Mathf.RoundToInt(Music*100)+"%";EffectsLabel.text="音效 "+Mathf.RoundToInt(Effects*100)+"%";MusicMute.GetComponentInChildren<TMP_Text>().text=MuteMusic?"取消静音":"静音";EffectsMute.GetComponentInChildren<TMP_Text>().text=MuteEffects?"取消静音":"静音";}
 public async void Queue(){pending=new SoundMix(Music,Effects,MuteMusic,MuteEffects).Json;if(writing)return;writing=true;
  try{while(pending!=null){await Task.Delay(60);var mix=pending;pending=null;await Client.ContentCall("SetSoundMix",new JObject{{"work_id",Work},{"mix",mix}});if(this)Feedback.text="已保存";}}
  catch(Exception failure){pending=null;try{await Client.RefreshContent();if(this){var actual=Client.Sound(Work);Music=actual.Music;Effects=actual.Effects;MuteMusic=actual.MusicMuted;MuteEffects=actual.EffectsMuted;MusicSlider.SetValueWithoutNotify(Music);EffectsSlider.SetValueWithoutNotify(Effects);RefreshLabels();}}catch(Exception refresh){Debug.LogWarning(refresh);}if(this)Feedback.text="声音设置保存失败，请重试。";Debug.LogWarning(failure);}
  finally{writing=false;}
 }
}
