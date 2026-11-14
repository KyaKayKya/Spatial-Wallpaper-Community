using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Library;
using UnityEngine;
using UnityEngine.EventSystems;
using System.Collections.Generic;

// Player-only acceptance driver. It uses real UI controls, the production store,
// authenticated IPC, and a real synthetic Tracker. Ordinary launch does nothing.
public sealed class DesktopSmoke:MonoBehaviour {
 string output;bool failed,expectWriteFailure;int expectedWriteFailures;DesktopManager manager;LibraryPreviewController library;CalibrationRuntime runtime;
 const BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
 void Require(bool value,string message){if(!value)throw new Exception(message);}
 void Log(string message,string trace,LogType type){if(expectWriteFailure&&type==LogType.Error&&message.StartsWith("Vertical calibration measurement write failed:")){expectedWriteFailures++;return;}if(output!=null&&(type==LogType.Error||type==LogType.Exception)){failed=true;File.AppendAllText(Path.Combine(output,"errors.txt"),message+"\n"+trace+"\n");}}
 IEnumerator Start(){
  output=CalibrationRuntime.Argument("--desktop-smoke");if(string.IsNullOrEmpty(output)){output=null;yield break;}Directory.CreateDirectory(output);Application.logMessageReceived+=Log;
  manager=GetComponent<DesktopManager>();library=GetComponent<LibraryPreviewController>();runtime=GetComponent<CalibrationRuntime>();
  if(CalibrationRuntime.Argument("--desktop-pointer-probe")=="true"){yield return PointerApplyProbe();yield break;}
  if(CalibrationRuntime.Argument("--desktop-cold-preview")=="true"){
   yield return null;
   var coldView=GetComponent<CalibrationDemoView>();library.Select(ArtworkCatalog.All[5]);
   typeof(CalibrationDemoView).GetMethod("EnterArtworkPreview",Hidden).Invoke(coldView,new object[]{false});
   yield return Until(()=>!library.Loading,"cold first preview");
   Require(library.Loaded!=null&&library.Error==null,"Cold first preview failed: "+library.Error);
   Require(!runtime.CameraStarted,"Cold mouse preview opened local Tracker");
   yield return Shot("cold-first-preview.png");
   library.ClosePreview();yield return Until(()=>!library.Loading,"cold close");
   File.WriteAllText(Path.Combine(output,"result.txt"),"PASS cold first preview without retry; released scene; no camera enabled");
   runtime.Session.Discard();Application.Quit(0);yield break;
  }
  yield return Until(()=>manager.Client.Available,"desktop manager discovery");
  manager.Client.TrackingRequested=true;
  Require(!runtime.CameraStarted,"Manager started a local Tracker");
  var setup=manager.Client.ConfigureCamera("simulation","合成追踪",null);yield return Task(setup);
  yield return Until(()=>runtime.Fresh,"remote raw tracking fresh");
  var target=LibraryDisplayCatalog.List().First().Id;var profile=new CalibrationProfile();profile.manual.Bind(runtime.CurrentRaw.Descriptor,target);profile.reference_ready=true;profile=runtime.Store.Save(profile,"桌面自动验证",false);
  var settings=manager.Client.Call("DesktopCalibrationSelection",new JObject{["selected"]=profile.id,["applied"]=JObject.FromObject(profile),["pause_when_invisible"]=false});yield return Task(settings);
  var settingsView=GetComponent<CalibrationDemoView>();
  typeof(CalibrationDemoView).GetField("currentPage",Hidden).SetValue(settingsView,"设置");typeof(CalibrationDemoView).GetMethod("Build",Hidden).Invoke(settingsView,null);yield return null;
  Require(FindObjectsOfType<Evo.UI.Button>().Any(x=>x.name=="默认预设显示器"),"Settings missing explicit default display selector");
  var defaultChoices=typeof(CalibrationDemoView).GetMethod("DefaultPresetChoices",Hidden).Invoke(settingsView,null) as System.Collections.ICollection;Require(defaultChoices.Count>=2,"Settings did not include compatible default preset");
  var settingsScroll=FindObjectsOfType<UnityEngine.UI.ScrollRect>().Single();settingsScroll.verticalNormalizedPosition=0;yield return Shot("manager-settings-default.png");
  typeof(CalibrationDemoView).GetField("currentPage",Hidden).SetValue(settingsView,"作品库");typeof(CalibrationDemoView).GetMethod("Build",Hidden).Invoke(settingsView,null);yield return null;
  foreach(var artwork in ArtworkCatalog.All){
   library.Select(artwork);library.SelectTarget(target);library.SelectCalibrationPreset(profile.id);yield return null;Click("应用到桌面");
   yield return Until(()=>!library.ApplyWallpaper.Busy,"UI apply "+artwork.Id);
   Require(library.ApplyWallpaper.Succeeded,"UI apply failed: "+library.ApplyWallpaper.Message);
   Require(manager.Client.ActiveArtworkId==artwork.Id&&(bool)manager.Client.State["attached"],"UI claims incorrect active state");
   Require(!runtime.CameraStarted,"Manager duplicated the Runtime Tracker");
  }
  yield return Shot("manager-applied.png");
  Click("预览");yield return Until(()=>!library.Loading&&library.Loaded!=null,"manager preview");
  Require((bool)manager.Client.State["preview"]&&!(bool)manager.Client.State["scene_loaded"],"Desktop was not released before preview");
  yield return Shot("manager-preview.png");Click("返回作品库");yield return Until(()=>!library.Loading&&library.Loaded==null,"preview unload");
  yield return Until(()=>!(bool)manager.Client.State["preview"]&&(bool)manager.Client.State["attached"],"desktop restored after preview");
  var view=GetComponent<CalibrationDemoView>();typeof(CalibrationDemoView).GetField("currentPage",Hidden).SetValue(view,"校准");typeof(CalibrationDemoView).GetMethod("Build",Hidden).Invoke(view,null);
  yield return Until(()=>(bool)manager.Client.State["preview"]&&runtime.ViewportValid&&runtime.Fresh,"calibration handoff");
  yield return VerticalMeasurements(view);
  yield return Shot("manager-calibration.png");
  typeof(CalibrationDemoView).GetMethod("NavigateLibrary",Hidden).Invoke(view,null);yield return Until(()=>!(bool)manager.Client.State["preview"]&&(bool)manager.Client.State["attached"],"calibration restores desktop");
  library.SelectNoWallpaper();yield return null;Click("恢复原壁纸");yield return Until(()=>!library.StopWallpaper.Busy,"UI stop");Require(library.StopWallpaper.Succeeded&&manager.Client.ActiveArtworkId==null,"No wallpaper did not stop Runtime");
  library.Select(ArtworkCatalog.All[0]);library.SelectTarget(target);library.SelectCalibrationPreset(profile.id);yield return null;Click("应用到桌面");yield return Until(()=>!library.ApplyWallpaper.Busy,"reapply before manager exit");Require(library.ApplyWallpaper.Succeeded,"Reapply failed");
  Require(!failed,"Player logged errors");
  // The external test verifies that this Runtime survives the manager's normal exit.
  File.WriteAllText(Path.Combine(output,"result.txt"),"PASS real Evo apply controls for "+ArtworkCatalog.All.Count+" scenes; remote synthetic tracking; no local Tracker; preview and calibration release/restore; no-wallpaper stop; manager exit with active wallpaper; two-edge raw capture/fit/center residual/JSONL/undo/context invalidation/cancel/write failure/compact/fullscreen; raw-slot race probe="+CalibrationRuntime.Argument("--vertical-context-race","false"));
  if(CalibrationRuntime.Argument("--desktop-full-exit-test")=="true"){
   var dirty=runtime.Session.Draft;dirty.manual.offsetX+=.001;runtime.Session.BeginEdit();runtime.Session.Edit(dirty);runtime.Session.CommitEdit();
   Require(runtime.Session.Dirty,"Full exit fixture needs an unsaved draft");File.WriteAllText(Path.Combine(output,"ready-for-full-exit.txt"),"active wallpaper and dirty manager draft");
   while(true)yield return null;
  }
  runtime.Session.Discard();Application.Quit(0);
 }
 IEnumerator VerticalMeasurements(CalibrationDemoView view){
  // Freeze remote polling only in this explicit test. Feed fixed pinhole-camera
  // measurements at the already validated raw slot; no production injection API.
  manager.enabled=false;yield return new WaitForSeconds(.3f);
  var pipe=manager.Client.Tracking.Pipeline;var slot=typeof(TrackingPipeline).GetField("raw",Hidden);
  var sample=pipe.RawLatest;Require(sample!=null,"Missing synthetic descriptor");
  var viewport=(ViewportMetrics)typeof(CalibrationRuntime).GetField("viewport",Hidden).GetValue(runtime);
  if(CalibrationRuntime.Argument("--vertical-context-race")=="true")ContextRace(pipe,sample);
  double c=viewport.CenterY,h=viewport.Height;
  Func<double,double> observed=y=>{double t=10*Math.PI/180,dy=.17-y;return 2*450.0/480*(dy*Math.Cos(t)-.65*Math.Sin(t))/(dy*Math.Sin(t)+.65*Math.Cos(t));};
  Action<double> feed=v=>slot.SetValue(pipe,new RawTrackingSample(sample.Stream,++verticalFrame,QpcClock.Now,sample.Eye,sample.Head,sample.Descriptor,new BinocularObservation(0,v,100)));
  string before=Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Session.Draft.manual);
  try{
   double cx=viewport.CenterX,w=viewport.Width;
   Func<double,double> observedX=x=>{double t=10*Math.PI/180,dx=.09-x;return 2*500.0/640*(dx*Math.Cos(t)-.65*Math.Sin(t))/(dx*Math.Sin(t)+.65*Math.Cos(t));};
   Action<double> feedX=u=>slot.SetValue(pipe,new RawTrackingSample(sample.Stream,++verticalFrame,QpcClock.Now,sample.Eye,sample.Head,sample.Descriptor,new BinocularObservation(u,0,100)));
   feedX(observedX(cx));yield return null;Click("横向跟随");yield return null;
   yield return CapturePoint("记录左沿",observedX(cx-w/2),feedX);Require(!runtime.CanSolveVertical,"One horizontal point enabled fit");
   yield return CapturePoint("记录右沿",observedX(cx+w/2),feedX);Click("联合求解");yield return null;
   var horizontal=runtime.Session.Draft.manual;
   Require(Math.Abs(horizontal.Map(new BinocularObservation(observedX(cx-w/2),0,100))[0]-cx+w/2)<1e-9,"Fit missed fixed physical left");
   Require(Math.Abs(horizontal.Map(new BinocularObservation(observedX(cx+w/2),0,100))[0]-cx-w/2)<1e-9,"Fit missed fixed physical right");
   yield return CapturePoint("复核中心",observedX(cx),feedX);
   var horizontalRecords=File.ReadAllLines(runtime.VerticalRecordPath).Select(JObject.Parse).ToArray();
   var xCenter=horizontalRecords.Last(x=>(string)x["point"]=="center");
   Require((string)xCenter["axis"]=="x"&&Math.Abs((double)xCenter["residual_x_nominal_m"])>1e-5,"Horizontal center residual missing");
   File.Copy(runtime.VerticalRecordPath,Path.Combine(output,"horizontal-measurements.jsonl"));yield return Shot("manager-horizontal-fit.png");
   Click("手动微调");yield return null;Click("撤销");yield return null;
   Require(before==Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Session.Draft.manual),"Horizontal fit undo must restore both parameters");
   Click("两点校准");yield return null;
   feed(observed(c));yield return null;Click("纵向跟随");yield return null;
   yield return CapturePoint("记录上沿",observed(c+h/2),feed);
   Require(!runtime.CanSolveVertical,"One point must not enable fit");
   yield return CapturePoint("记录下沿",observed(c-h/2),feed);
   Require(runtime.CanSolveVertical,"Both points must enable fit");
   Click("联合求解");yield return null;
   var m=runtime.Session.Draft.manual;
   Require(Math.Abs(m.Map(new BinocularObservation(0,observed(c+h/2),100))[1]-c-h/2)<1e-9,"Fit missed fixed physical top");
   Require(Math.Abs(m.Map(new BinocularObservation(0,observed(c-h/2),100))[1]-c+h/2)<1e-9,"Fit missed fixed physical bottom");
   yield return CapturePoint("复核中心",observed(c),feed);
   var records=File.ReadAllLines(runtime.VerticalRecordPath).Select(JObject.Parse).ToArray();
   var center=records.Last(x=>(string)x["point"]=="center");
   Require((double)center["residual_y_nominal_m"]<-.00001,"Tilted camera center residual was hidden");
   Require(((JArray)center["samples"]).Count>=8&&center["context"]["source"]!=null,"Raw sample/source records missing");
   Require(records.Any(x=>(string)x["event"]=="solve"&&x["before"]!=null&&x["after"]!=null),"Fit audit missing");
   File.Copy(runtime.VerticalRecordPath,Path.Combine(output,"vertical-measurements.jsonl"));
   yield return Shot("manager-vertical-fit.png");
   Click("手动微调");yield return null;Click("撤销");yield return null;
   Require(before==Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Session.Draft.manual),"Fit undo must restore both parameters");
   Require(!runtime.CanSolveVertical,"Undo must invalidate sampled fit");Click("两点校准");yield return null;
   yield return BeginPoint("记录上沿",observed(c+h/2),feed);yield return null;
   runtime.CancelVerticalMeasurements();yield return null;yield return null;
   Require(!runtime.Capturing&&!runtime.CanSolveVertical,"Cancelled capture leaked measurements");
   var recordPath=typeof(CalibrationRuntime).GetField("<VerticalRecordPath>k__BackingField",Hidden);string goodPath=runtime.VerticalRecordPath;
   string blocked=Path.Combine(output,"write-failure-directory");Directory.CreateDirectory(blocked);recordPath.SetValue(runtime,blocked);expectWriteFailure=true;
   try{yield return BeginPoint("记录上沿",observed(c+h/2),feed);yield return FeedFor(1.5f,observed(c+h/2),feed);
    Require(!runtime.Capturing&&runtime.CanEdit&&!runtime.CanSolveVertical&&runtime.VerticalMeasurementStatus.Contains("写入失败")&&expectedWriteFailures==1,"Write failure must stay visible and leave editing recoverable");
   }finally{recordPath.SetValue(runtime,goodPath);expectWriteFailure=false;}
   // A viewport move invalidates completed points and interrupts a capture.
   yield return CapturePoint("记录上沿",observed(c+h/2),feed);
   runtime.SetViewport(new ViewportMetrics(viewport.Width,viewport.Height,viewport.CenterX,viewport.CenterY+.01),runtime.DisplayId);
   typeof(CalibrationRuntime).GetMethod("UpdateVerticalContext",Hidden).Invoke(runtime,null);
   Require(!runtime.CanSolveVertical&&runtime.VerticalMeasurementStatus.Contains("变化"),"Viewport change must invalidate points");
   Screen.SetResolution(800,640,false);yield return FeedFor(.8f,observed(c),feed);
   foreach(string name in new[]{"记录上沿","记录下沿","联合求解","复核中心"}){
    var corners=new Vector3[4];FindObjectsOfType<Evo.UI.Button>().Single(x=>x.name==name).GetComponent<RectTransform>().GetWorldCorners(corners);
    Require(corners.All(p=>p.x>=0&&p.y>=0&&p.x<=Screen.width&&p.y<=Screen.height),"Compact vertical control clipped: "+name);
   }
   yield return Shot("manager-vertical-compact.png");Click("全屏校准");yield return FeedFor(.8f,observed(c),feed);yield return Shot("manager-vertical-fullscreen.png");
   Click("返回控制台");yield return FeedFor(.8f,observed(c),feed);
   Click("横向跟随");yield return FeedFor(.3f,observedX(cx),feedX);
   foreach(string name in new[]{"记录左沿","记录右沿","联合求解","复核中心"}){
    var corners=new Vector3[4];FindObjectsOfType<Evo.UI.Button>().Single(x=>x.name==name).GetComponent<RectTransform>().GetWorldCorners(corners);
    Require(corners.All(p=>p.x>=0&&p.y>=0&&p.x<=Screen.width&&p.y<=Screen.height),"Compact horizontal control clipped: "+name);
   }
   yield return Shot("manager-horizontal-compact.png");
  }finally{runtime.CancelVerticalMeasurements();manager.enabled=true;}
  yield return Until(()=>runtime.Fresh,"remote tracking restored after edge fixtures");
 }
 ulong verticalFrame=100000;
 IEnumerator PointerApplyProbe(){
  yield return Until(()=>manager.Client.Available,"restored-config discovery");
  if(CalibrationRuntime.Argument("--desktop-pointer-fullscreen")=="true"){
   Screen.SetResolution(Screen.currentResolution.width,Screen.currentResolution.height,FullScreenMode.FullScreenWindow);
   yield return new WaitForSecondsRealtime(1);
  }
  var profile=runtime.Store.List().First(p=>p.schema_version==2&&p.manual.Bound);
  library.Select(ArtworkCatalog.All[5]);library.SelectTarget(profile.manual.display_id);library.SelectCalibrationPreset(profile.id);yield return null;
  manager.enabled=false;
  try{
   yield return Until(()=>!(bool)typeof(DesktopManager).GetField("polling",Hidden).GetValue(manager)&&!(bool)typeof(DesktopManager).GetField("connecting",Hidden).GetValue(manager),"poll quiescence before reconnect probe");
   manager.Client.Disconnect();yield return null;
   Require(!FindObjectsOfType<Evo.UI.Button>().Single(x=>x.name=="应用到桌面").IsInteractable(),"Disconnected apply was not disabled");
   yield return Task(manager.Client.Connect());yield return null;
   Require(FindObjectsOfType<Evo.UI.Button>().Single(x=>x.name=="应用到桌面").IsInteractable(),"Same-state reconnect left apply disabled");
  }finally{manager.enabled=true;}
  var button=FindObjectsOfType<Evo.UI.Button>().Single(x=>x.name=="应用到桌面");Require(button.IsInteractable(),"Restored apply disabled");
  Canvas.ForceUpdateCanvases();var corners=new Vector3[4];button.GetComponent<RectTransform>().GetWorldCorners(corners);
  var input=new PointerEventData(EventSystem.current){position=(corners[0]+corners[2])/2,button=PointerEventData.InputButton.Left};
  var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(input,hits);
  Require(hits.Count>0&&hits[0].gameObject.transform.IsChildOf(button.transform),"Restored apply raycast blocked: "+(hits.Count==0?"none":hits[0].gameObject.name));
  yield return Shot("restored-before-apply.png");
  var target=hits[0].gameObject;ExecuteEvents.ExecuteHierarchy(target,input,ExecuteEvents.pointerDownHandler);
  yield return new WaitForSecondsRealtime(.2f);
  Require(button&&button.IsActive(),"Apply button rebuilt between pointer down and up");
  hits.Clear();EventSystem.current.RaycastAll(input,hits);Require(hits.Count>0&&hits[0].gameObject.transform.IsChildOf(button.transform),"Apply target changed while held");
  ExecuteEvents.ExecuteHierarchy(target,input,ExecuteEvents.pointerUpHandler);ExecuteEvents.ExecuteHierarchy(target,input,ExecuteEvents.pointerClickHandler);
  yield return Until(()=>library.ApplyWallpaper.Request!=null&&!library.ApplyWallpaper.Busy,"restored apply acknowledgement");
  Require(library.ApplyWallpaper.Succeeded,library.ApplyWallpaper.Message);Require(!runtime.CameraStarted,"Restored apply started manager Tracker");
  yield return Shot("restored-apply.png");File.WriteAllText(Path.Combine(output,"result.txt"),"PASS restored preset; default invisible pause; pointer raycast/down/200ms/up/click; desktop application acknowledged");
  runtime.Session.Discard();Application.Quit();
 }
 IEnumerator CapturePoint(string button,double v,Action<double> feed){
  yield return BeginPoint(button,v,feed);
  yield return FeedFor(1.5f,v,feed);
  Require(!runtime.Capturing,"Point capture did not finish");
  Require(runtime.VerticalMeasurementStatus.Contains("已记录")||runtime.VerticalMeasurementStatus.Contains("中心残差"),runtime.VerticalMeasurementStatus);
 }
 IEnumerator BeginPoint(string button,double value,Action<double> feed){
  // A native screenshot can outlast raw freshness while this fixture has
  // remote polling disabled. Keep feeding until LateUpdate refreshes the UI.
  float deadline=Time.realtimeSinceStartup+5;
  while(true){feed(value);yield return null;var control=FindObjectsOfType<Evo.UI.Button>().Single(x=>x.name==button);if(control.IsInteractable()){feed(value);Click(button);yield break;}Require(Time.realtimeSinceStartup<deadline,"Fixed input did not enable "+button);}
 }
 IEnumerator FeedFor(float seconds,double v,Action<double> feed){float deadline=Time.realtimeSinceStartup+seconds;while(Time.realtimeSinceStartup<deadline){feed(v);yield return null;}}
 void ContextRace(TrackingPipeline pipe,RawTrackingSample sample){
  var slot=typeof(TrackingPipeline).GetField("raw",Hidden);var context=typeof(CalibrationRuntime).GetMethod("VerticalContext",Hidden);
  using(var stop=new System.Threading.CancellationTokenSource()){
   var thread=new System.Threading.Thread(()=>{while(!stop.IsCancellationRequested){slot.SetValue(pipe,null);System.Threading.Thread.Yield();slot.SetValue(pipe,sample);System.Threading.Thread.Yield();}});thread.Start();
   try{for(int i=0;i<20000;i++)context.Invoke(runtime,null);}
   finally{stop.Cancel();thread.Join();slot.SetValue(pipe,sample);}
  }
 }
 IEnumerator Task(Task task){while(!task.IsCompleted)yield return null;if(task.IsFaulted)throw task.Exception.GetBaseException();}
 IEnumerator Until(Func<bool> condition,string name){float deadline=Time.realtimeSinceStartup+25;while(!condition()){if(failed)throw new Exception("Player logged errors while "+name);if(Time.realtimeSinceStartup>deadline)throw new TimeoutException(name);yield return null;}yield return null;}
 IEnumerator Shot(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name));yield return null;}
 static void Click(string name){
  var button=FindObjectsOfType<Evo.UI.Button>().First(x=>x.name==name);if(!button.IsInteractable())throw new Exception("disabled UI: "+name);
  Canvas.ForceUpdateCanvases();var corners=new Vector3[4];button.GetComponent<RectTransform>().GetWorldCorners(corners);
  var input=new PointerEventData(EventSystem.current){position=(corners[0]+corners[2])/2,button=PointerEventData.InputButton.Left};
  var hits=new List<RaycastResult>();EventSystem.current.RaycastAll(input,hits);
  if(hits.Count==0||!hits[0].gameObject.transform.IsChildOf(button.transform))throw new Exception("UI click blocked: "+name+" by "+(hits.Count==0?"no raycast hit":hits[0].gameObject.name));
  var target=hits[0].gameObject;
  ExecuteEvents.ExecuteHierarchy(target,input,ExecuteEvents.pointerDownHandler);
  ExecuteEvents.ExecuteHierarchy(target,input,ExecuteEvents.pointerUpHandler);
  ExecuteEvents.ExecuteHierarchy(target,input,ExecuteEvents.pointerClickHandler);
 }
 void OnDestroy(){Application.logMessageReceived-=Log;}
}
