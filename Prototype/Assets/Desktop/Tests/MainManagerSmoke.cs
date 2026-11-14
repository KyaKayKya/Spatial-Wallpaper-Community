using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Content;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Library;
using UnityEngine;
using UnityEngine.EventSystems;

// Inert on normal launch. Exercises the original manager and its real runtime.
public sealed partial class MainManagerSmoke:MonoBehaviour {
 const BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
 [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
 [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref Point point);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 string output;DesktopManager manager;LibraryPreviewController library;CalibrationRuntime runtime;CalibrationDemoView view;
 JObject report=new JObject{{"status","failed"},{"scope","original Launch.exe, real manager controls and SDK sandbox; synthetic camera; not RC or physical-camera acceptance"},{"cases",new JArray()}};
 T Field<T>(object owner,string name)=>(T)owner.GetType().GetField(name,Hidden).GetValue(owner);
 void Invoke(string name,params object[] values)=>typeof(CalibrationDemoView).GetMethod(name,Hidden).Invoke(view,values);
 void Need(bool condition,string message){if(!condition)throw new InvalidOperationException(message);}
 void Passed(string name){((JArray)report["cases"]).Add(name);File.WriteAllText(Path.Combine(output,"progress.json"),report.ToString());}
 async Task Until(Func<bool> condition,string name,int milliseconds=45000){var until=DateTime.UtcNow.AddMilliseconds(milliseconds);while(!condition()){if(DateTime.UtcNow>until)throw new TimeoutException(name);await Task.Delay(30);}}
 async Task Shot(string name,bool desktop=false){
  File.WriteAllText(Path.Combine(output,"capture-request.json"),new JObject{{"name",name},{"pid",desktop?(int)manager.Client.State["pid"]:System.Diagnostics.Process.GetCurrentProcess().Id},{"desktop",desktop},{"artwork_id",desktop?manager.Client.ActiveArtworkId:null}}.ToString());
  if(!desktop){await Until(()=>File.Exists(Path.Combine(output,name+".captured")),"capture "+name,15000);return;}
  var deadline=DateTime.UtcNow.AddSeconds(15);
  while(!File.Exists(Path.Combine(output,name+".captured"))){
   foreach(string phase in new[]{"initial","capture"}){
    string state=Path.Combine(output,name+".state-"+phase);
    if(File.Exists(state+".request")&&!File.Exists(state+".json")){
     await manager.Client.Refresh();File.WriteAllText(state+".tmp",manager.Client.State.ToString());File.Move(state+".tmp",state+".json");
    }
   }
   if(DateTime.UtcNow>deadline)throw new TimeoutException("capture "+name);await Task.Delay(30);
  }
 }
 async void Start(){
  output=CalibrationRuntime.Argument("--main-manager-smoke");string probe=CalibrationRuntime.Argument("--main-manager-probe");if(DesktopRuntime.IsRuntime||string.IsNullOrEmpty(output)&&string.IsNullOrEmpty(probe))return;
  manager=GetComponent<DesktopManager>();library=GetComponent<LibraryPreviewController>();runtime=GetComponent<CalibrationRuntime>();view=GetComponent<CalibrationDemoView>();
  if(string.IsNullOrEmpty(output)){
   var receipt=new JObject{{"status","failed"},{"manager_pid",System.Diagnostics.Process.GetCurrentProcess().Id}};
   try{
    await Until(()=>manager.Client.Available&&manager.Client.ContentState!=null,"reopened GUI connected",45000);await manager.Client.RefreshContent();
    await Until(()=>FindObjectsOfType<Evo.UI.Button>().Any(b=>b.name=="导入 SDK"&&b.IsInteractable()),"reopened GUI SDK import enabled");
    int installed=((JObject)manager.Client.ContentState["state"]["works"]).Count;int sdk=ArtworkCatalog.All.Count(a=>a.IsContent);
    Need(installed>0&&sdk==installed&&((JObject)manager.Client.ContentState["bundled_catalog"]).Count==5,"reopened GUI SDK catalog differs");
    receipt["status"]="passed-connected-original-manager";receipt["runtime_pid"]=manager.Client.State["pid"];receipt["content_count"]=sdk;receipt["sdk_import_enabled"]=true;
    receipt["active_work"]=manager.Client.ActiveArtworkId;receipt["active_revision"]=manager.Client.State["content_revision"];receipt["mix"]=manager.Client.Sound(manager.Client.ActiveArtworkId).Json;
   }catch(Exception failure){receipt["error"]=failure.ToString();}
   File.WriteAllText(probe,receipt.ToString());return;
  }
  Directory.CreateDirectory(output);
  try{await Run();report["status"]="passed-main-manager-development-combination";}
  catch(Exception failure){report["error"]=failure.ToString();report["desktop_state"]=manager.Client?.State;report["native_failure_state"]=library?.NativePreviewState;report["tracking_message"]=runtime.Message;File.WriteAllText(Path.Combine(output,"result.json"),report.ToString());Debug.LogWarning(failure);try{if(manager.Client.Available)await manager.Client.Call("QuitDesktop",new JObject());}catch(Exception cleanup){report["cleanup_error"]=cleanup.ToString();}}
  finally{File.WriteAllText(Path.Combine(output,"result.json"),report.ToString());runtime.Session.Discard();Application.Quit((string)report["status"]=="passed-main-manager-development-combination"?0:1);}
 }
 async Task Run(){
  await Until(()=>manager.Client.Available&&manager.Client.ContentState!=null,"main manager ready",90000);Need(!runtime.CameraStarted,"manager must not own a Tracker");
  if(CalibrationRuntime.Argument("--main-manager-restore")=="true"){
   var expected=JObject.Parse(File.ReadAllText(Path.Combine(output,"expected-restart.json")));await Until(()=>manager.Client.ActiveArtworkId==(string)expected["work"]&&(bool?)manager.Client.State["content_ready"]==true,"restore actual SDK session");
   Need((string)manager.Client.State["content_revision"]==(string)expected["revision"],"restart applied latest unapplied revision");
   await manager.Client.RefreshContent();var actualMix=manager.Client.Sound((string)expected["work"]);var expectedMix=SpatialWallpaper.Sdk.Capabilities.SoundMix.Parse(expected["mix"]);Need(actualMix.Music==expectedMix.Music&&actualMix.Effects==expectedMix.Effects&&actualMix.MusicMuted==expectedMix.MusicMuted&&actualMix.EffectsMuted==expectedMix.EffectsMuted,"restart changed sound settings: "+actualMix.Json);
   Passed("restart-restores-exact-successful-revision-and-work-sound");await Shot("restored-main-library");
   Invoke("ConfirmLibraryRemoval",(string)expected["work"]);await Task.Delay(100);Click("卸载并删除存档");await Until(()=>!Field<bool>(view,"contentOperation"),"uninstall playing work");await manager.Client.Refresh();await manager.Client.RefreshContent();Need(manager.Client.ActiveArtworkId==null&&manager.Client.ContentState["state"]["works"][(string)expected["work"]]==null,"current uninstall did not stop and remove work");Passed("original-uninstall-confirmation-stops-playing-work-and-clears-library");return;
  }
  Click("设置");await Task.Delay(100);await manager.Client.ConfigureCamera("simulation","合成追踪",null);await Until(()=>runtime.Fresh,"synthetic tracker");
  string target=LibraryDisplayCatalog.List().First().Id;var profile=new CalibrationProfile();profile.manual.Bind(runtime.CurrentRaw.Descriptor,target);profile.reference_ready=true;profile=runtime.Store.Save(profile,"主管理器整合验证",false);
  runtime.ApplyDesktopSettings(profile.id,profile,false);await manager.Client.Call("DesktopCalibrationSelection",new JObject{{"selected",profile.id},{"applied",JObject.FromObject(profile)},{"pause_when_invisible",false}});Click("作品库");await Task.Delay(100);
  if(CalibrationRuntime.Argument("--main-manager-native-preview")=="true"){await RunNativePreview(target,profile.id);return;}
  foreach(string builtin in new[]{"pool","limpool","mita"}){await Apply(ArtworkCatalog.Find(builtin),target,profile.id);await Shot("direct-"+builtin,true);}
  await manager.Client.Call("StopDesktop",new JObject());await manager.Client.Refresh();
  var sdk=ArtworkCatalog.All.Where(a=>a.IsContent).ToArray();Need(sdk.Length==5,"main product must install existing five packages");
  var minishot=sdk.Single(a=>a.CoverId=="minishotgame");
  GetComponent<PackageDropReceiver>().Dropped(new[]{Path.Combine(Application.streamingAssetsPath,"sdk-content","MiniShotGame.spatial")});
  await Until(()=>!Field<bool>(view,"contentOperation")&&library.Selected.Id==minishot.Id,"original library SDK drop");Need(manager.Client.ActiveArtworkId==null,"import implicitly applied wallpaper");Passed("original-library-import-selects-without-applying");
  Select(minishot,target,profile.id);await Task.Delay(100);Click("预览");await Until(()=>!library.Loading,"original page SDK preview");Need(library.Loaded?.Id==minishot.Id&&library.Error==null,"original page SDK preview: "+library.Error);
  Need((bool?)manager.Client.State["preview"]==true&&!runtime.CameraStarted,"preview did not use original handoff/tracker");
  var presentation=Field<RuntimeContentPresentation>(library,"sdkPreview");Need(presentation.PreviewMode&&presentation.Ready,"SDK preview mode not ready");await Shot("sdk-original-preview");
  var before=library.MouseEye;await DragPreview();await Until(()=>(library.MouseEye-before).sqrMagnitude>.00001f,"native mouse observation bridge");
  Click("复位视角");Need(library.MouseEye==Vector2.zero,"reset did not reset original view");Click("摄像头");await Task.Delay(500);Need(library.CameraMode&&!runtime.CameraStarted,"camera mode duplicated manager Tracker");Click("鼠标观察");await Task.Delay(100);Passed("original-preview-mouse-camera-reset-controls");
  Click("保左右");Need(!library.KeepHeight&&Field<string>(library,"framingKey").Contains(minishot.Id)&&PlayerPrefs.GetInt(Field<string>(library,"framingKey"),-1)==1,"SDK framing preference must belong to selected SDK work");await Task.Delay(100);Click("保上下");await Task.Delay(100);
  int fullscreenWidth=Screen.currentResolution.width;Click("全屏预览");await Until(()=>Screen.fullScreen&&Screen.width==fullscreenWidth&&presentation.Ready&&(int?)presentation.Metrics["target_pixels"]?[0]==Screen.width,"fullscreen SDK region");await Shot("sdk-original-fullscreen");
  var hitPoint=new Point{X=(int)(Screen.width*.403f),Y=(int)(64*DisplayViewport.UiScale+(Screen.height-64*DisplayViewport.UiScale)*.449f)};var hitWindow=WindowsDesktopWindow.FindOwnWindow();Need(ClientToScreen(hitWindow,ref hitPoint),"fullscreen target coordinates");SetForegroundWindow(hitWindow);SetCursorPos(hitPoint.X,hitPoint.Y);await Task.Delay(100);mouse_event(2,0,0,0,UIntPtr.Zero);await Task.Delay(100);mouse_event(4,0,0,0,UIntPtr.Zero);await Task.Delay(500);await Shot("sdk-fullscreen-input-hit");
  Click("返回作品预览");await Until(()=>presentation.Ready&&(int?)presentation.Metrics["target_pixels"]?[0]<Screen.width,"windowed SDK region");
  Click("返回作品库");await Until(()=>!library.Loading&&library.Loaded==null,"preview release");await Until(()=>(bool?)manager.Client.State["preview"]==false,"desktop handoff restored");Need(presentation.SandboxResourcesReleased,"preview resources still owned");Passed("window-fullscreen-return-releases-preview-and-sound");
  await Apply(minishot,target,profile.id);await Shot("main-library-sdk-applied");
  DetailsScroll().verticalNormalizedPosition=.35f;await Task.Delay(100);var music=FindObjectsOfType<Evo.UI.Slider>().Single(x=>x.name=="作品配乐音量");var held=music.GetInstanceID();var gesture=music.GetComponent<CalibrationGesture>();gesture.Begin();music.value=.37f;await Task.Delay(1300);Need(music&&music.GetInstanceID()==held,"polling rebuilt a held sound slider");gesture.End();await Shot("original-right-sidebar-sound");
  var controls=FindObjectsOfType<LibraryWorkSoundControls>().Single();controls.MusicMute.onClick.Invoke();await Task.Delay(500);await manager.Client.RefreshContent();Need(Mathf.Abs(manager.Client.Sound(minishot.Id).Music-.37f)<.001f&&manager.Client.Sound(minishot.Id).MusicMuted,"right sidebar sound did not persist");
  var train=sdk.Single(a=>a.CoverId=="train");Select(train,target,profile.id);await Task.Delay(100);controls=FindObjectsOfType<LibraryWorkSoundControls>().Single();controls.EffectsSlider.value=.21f;await Task.Delay(400);await manager.Client.RefreshContent();Need(manager.Client.ActiveArtworkId==minishot.Id&&manager.Client.Sound(minishot.Id).MusicMuted&&Mathf.Abs(manager.Client.Sound(train.Id).Effects-.21f)<.001f,"selected work changed playing work mix");Passed("right-sidebar-drag-survives-poll-and-selection-mixes-are-independent");
  foreach(var item in sdk)await Apply(item,target,profile.id);Passed("five-existing-SDK-packages-switch-through-original-apply-control");
  foreach(string builtin in new[]{"pool","limpool","mita"}){
   await Apply(minishot,target,profile.id);await Apply(ArtworkCatalog.Find(builtin),target,profile.id);
   Need((bool?)manager.Client.State["scene_loaded"]==true&&(string)manager.Client.State["content_revision"]==null,"SDK to builtin source did not switch");await Shot("after-sdk-"+builtin,true);
   await manager.Client.Call("BeginDesktopPreview",new JObject());await manager.Client.Call("EndDesktopPreview",new JObject());await Shot("after-preview-"+builtin,true);
   await manager.Client.Call("PauseDesktop",new JObject{{"paused",true}});await manager.Client.Call("PauseDesktop",new JObject{{"paused",false}});await Shot("after-resume-"+builtin,true);
  }
  Passed("builtin-frame-visible-after-sdk-switch-preview-and-resume");
  Select(ArtworkCatalog.BlueprintTemplate,target,profile.id);Invoke("EnterArtworkPreview",false);await Until(()=>library.Loaded?.Id=="blueprint"&&!library.Loading,"original blueprint preview");Need(FindObjectOfType<SpatialWallpaper.Blueprint.BlueprintEditorPanel>(),"original blueprint editor unavailable");
  var panel=FindObjectOfType<SpatialWallpaper.Blueprint.BlueprintEditorPanel>();Destroy(panel.gameObject);Invoke("NavigateLibrary");await Until(()=>!library.Loading&&library.Loaded==null,"blueprint close");Passed("unmigrated-Pool-and-original-blueprint-entry-remain-available");
  await Apply(minishot,target,profile.id);string oldRevision=(string)manager.Client.State["content_revision"];
  await manager.Client.Call("PauseDesktop",new JObject{{"paused",true}});await Until(()=>(bool?)manager.Client.State["paused"]==true,"tray-policy pause");await Task.Delay(100);Need(Field<TMPro.TMP_Text>(view,"contentPlayingStatus").text.Contains("已暂停"),"sidebar playback status stale after pause");await manager.Client.Call("PauseDesktop",new JObject{{"paused",false}});await Until(()=>(bool?)manager.Client.State["content_ready"]==true,"resume SDK");Passed("original-pause-policy-and-sidebar-real-status");
  var parking=sdk.Single(a=>a.CoverId=="parking");string installed=Path.Combine(DesktopRuntime.ContentRoot,"library","builds",parking.BuildId+".spatial");byte[] original=File.ReadAllBytes(installed);Select(parking,target,profile.id);File.WriteAllText(installed,"fixture broken installed archive");
  try{await Task.Delay(100);Click("应用到桌面");await Until(()=>!library.ApplyWallpaper.Busy,"failed SDK apply recovery");Need(!library.ApplyWallpaper.Succeeded&&manager.Client.ActiveArtworkId==minishot.Id&&(bool?)manager.Client.State["content_ready"]==true,"failed apply did not restore previous actual work");}
  finally{File.WriteAllBytes(installed,original);}Passed("installed-package-failure-restores-previous-real-session");
  Invoke("ConfirmLibraryRemoval",parking.Id);await Task.Delay(100);await Shot("original-uninstall-confirmation");Click("卸载并删除存档");await Until(()=>!Field<bool>(view,"contentOperation"),"uninstall non-current work");await manager.Client.Refresh();Need(manager.Client.ActiveArtworkId==minishot.Id&&(bool?)manager.Client.State["content_ready"]==true,"non-current uninstall interrupted playing work");Passed("original-uninstall-confirmation-preserves-other-playing-work");
  string bad=Path.Combine(output,"bad.spatial");File.WriteAllText(bad,"not a package");GetComponent<PackageDropReceiver>().Dropped(new[]{bad});await Until(()=>!Field<bool>(view,"contentOperation"),"bad package refusal");Need(manager.Client.ActiveArtworkId==minishot.Id&&(bool?)manager.Client.State["content_ready"]==true,"bad import interrupted playing wallpaper");Passed("bad-package-refusal-preserves-playing-work");
  Invoke("ImportLibraryContent",new[]{Path.Combine(Application.streamingAssetsPath,"sdk-content","Train.spatial")},minishot.Id,ArtworkCatalog.Find(minishot.Id).RevisionId);await Until(()=>!Field<bool>(view,"contentOperation"),"same-work update");await manager.Client.RefreshContent();Need(ArtworkCatalog.Find(minishot.Id).RevisionId!=oldRevision&&(string)manager.Client.State["content_revision"]==oldRevision,"update implicitly changed active revision");Passed("update-keeps-installation-and-current-session-until-explicit-apply");
  File.WriteAllText(Path.Combine(output,"expected-restart.json"),new JObject{{"work",minishot.Id},{"revision",oldRevision},{"mix",manager.Client.Sound(minishot.Id).Json}}.ToString());
  report["runtime_pid"]=manager.Client.State["pid"].DeepClone();report["active_work"]=minishot.Id;report["active_revision"]=oldRevision;await Shot("final-main-library");
 }
 void Select(Artwork artwork,string target,string preset){library.Select(artwork);library.SelectTarget(target);library.SelectCalibrationPreset(preset);}
 UnityEngine.UI.ScrollRect DetailsScroll()=>FindObjectsOfType<UnityEngine.UI.ScrollRect>().Single(x=>x.content&&x.content.name=="Selected artwork details");
 async Task Apply(Artwork artwork,string target,string preset){Select(artwork,target,preset);DetailsScroll().verticalNormalizedPosition=1;await Task.Delay(100);Click("应用到桌面");await Until(()=>!library.ApplyWallpaper.Busy,"apply "+artwork.Name);Need(library.ApplyWallpaper.Succeeded,"apply failed: "+library.ApplyWallpaper.Message);await manager.Client.Refresh();Need(manager.Client.ActiveArtworkId==artwork.Id,"active artwork differs from successful apply");if(artwork.IsContent)Need((bool?)manager.Client.State["content_ready"]==true,"SDK first frame missing");Need(!runtime.CameraStarted,"manager owns a duplicate Tracker");}
 async Task DragPreview(){var image=Field<UnityEngine.UI.RawImage>(view,"preview");var corners=new Vector3[4];image.rectTransform.GetWorldCorners(corners);var p=new Point{X=(int)((corners[0].x+corners[2].x)/2),Y=Screen.height-(int)((corners[0].y+corners[2].y)/2)};IntPtr hwnd=WindowsDesktopWindow.FindOwnWindow();Need(ClientToScreen(hwnd,ref p),"preview screen coordinates");SetForegroundWindow(hwnd);SetCursorPos(p.X,p.Y);await Task.Delay(100);mouse_event(2,0,0,0,UIntPtr.Zero);for(int i=1;i<=6;i++){SetCursorPos(p.X+i*8,p.Y+i*3);await Task.Delay(30);}mouse_event(4,0,0,0,UIntPtr.Zero);}
 void Click(string name){var button=FindObjectsOfType<Evo.UI.Button>().First(x=>x.name==name);Need(button.IsInteractable(),"disabled UI "+name);Canvas.ForceUpdateCanvases();var corners=new Vector3[4];button.GetComponent<RectTransform>().GetWorldCorners(corners);var input=new PointerEventData(EventSystem.current){position=(corners[0]+corners[2])/2,button=PointerEventData.InputButton.Left};var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(input,hits);Need(hits.Count>0&&hits[0].gameObject.transform.IsChildOf(button.transform),"blocked UI "+name);var target=hits[0].gameObject;ExecuteEvents.ExecuteHierarchy(target,input,ExecuteEvents.pointerDownHandler);ExecuteEvents.ExecuteHierarchy(target,input,ExecuteEvents.pointerUpHandler);ExecuteEvents.ExecuteHierarchy(target,input,ExecuteEvents.pointerClickHandler);}
}
