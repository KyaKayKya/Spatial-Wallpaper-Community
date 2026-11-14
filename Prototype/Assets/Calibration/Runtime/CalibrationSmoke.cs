using System;
using System.IO;
using System.Collections;
using UnityEngine;
using SpatialTracking;
using System.Linq;
using TMPro;

// Explicit automation mode; never enabled during an ordinary user session.
public sealed class CalibrationSmoke:MonoBehaviour {
 public CalibrationRuntime runtime;string output;
 IEnumerator Start(){
  output=CalibrationRuntime.Argument("--calibration-smoke");if(string.IsNullOrEmpty(output))yield break;
  Directory.CreateDirectory(output);var stack=new System.Collections.Generic.Stack<IEnumerator>();stack.Push(Run());
  while(stack.Count>0){
   object next=null;bool more=false;
   try{more=stack.Peek().MoveNext();if(more)next=stack.Peek().Current;}
   catch(Exception e){File.WriteAllText(Path.Combine(output,"result.txt"),"FAIL "+e);runtime.Session.Discard();Application.Quit(2);yield break;}
   if(!more){stack.Pop();continue;}if(next is IEnumerator nested){stack.Push(nested);continue;}yield return next;
  }
 }
 static void Require(bool value,string message){if(!value)throw new Exception(message);}
 IEnumerator Run(){
  float start=Time.realtimeSinceStartup;
  while((!runtime.Fresh||!runtime.ViewportValid)&&Time.realtimeSinceStartup-start<25)yield return null;
  Require(runtime.Fresh&&runtime.ViewportValid,"No fresh binocular measurement / viewport: fresh="+runtime.Fresh+" viewport="+runtime.ViewportValid+" screen="+Screen.width+"x"+Screen.height+" "+runtime.Diagnostic);
  yield return null;yield return null;Require(runtime.CanEdit,"First-use device binding must be automatic");
  Require(!FindObjectsOfType<Evo.UI.Button>().Any(x=>x.name=="绑定当前设备"||x.name=="选择预设"),"Advanced controls leaked into first-use flow");
  var guideView=FindObjectOfType<CalibrationDemoView>();
  Require(!FindObjectsOfType<Evo.UI.Button>().First(x=>x.name=="查看示范").IsInteractable(),"Unimplemented demo must not pretend available");
  yield return Screenshot("calibration.png");
  string[] names={"offsetX","offsetY","scaleX","scaleY","scaleZ"};double[] values={.1,-.05,4,5,6.5};
  for(int i=0;i<5;i++){
   Click(CalibrationGuide.Names[i]);Enter(values[i].ToString(System.Globalization.CultureInfo.InvariantCulture));yield return null;
   double actual=(double)typeof(FiveParameterMapping).GetField(names[i]).GetValue(runtime.Session.Draft.manual);
   Require(Math.Abs(actual-values[i])<1e-8,"Numeric binding: "+names[i]);
  }
  Enter("NaN");yield return null;Require(runtime.Session.Draft.manual.scaleZ==6.5,"Invalid input polluted draft");Enter("6.5");
  Enter("0.1");yield return null;yield return null;Require(runtime.CanEdit&&!runtime.CanSave,"Unsafe eye must allow corrective edits, prevent save");
  Click("撤销");yield return null;yield return null;Require(runtime.CanSave,"Undo did not restore safe mapping");
  var view=FindObjectOfType<CalibrationDemoView>();var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;
  // Keyboard path and adjacent controls use the same undo transaction.
  Click("水平位置");Enter("0.15");yield return null;
  bool accepted=(bool)typeof(CalibrationDemoView).GetMethod("HandleUndoShortcut",flags).Invoke(view,null);
  Require(accepted&&Math.Abs(runtime.Session.Draft.manual.offsetX-.1)<1e-8,"Shortcut did not undo last adjustment");
  var undoButton=FindObjectsOfType<Evo.UI.Button>().First(x=>x.name=="撤销");
  var resetButton=FindObjectsOfType<Evo.UI.Button>().First(x=>x.name=="恢复本项");
  Require(undoButton.transform.parent==resetButton.transform.parent,"Undo must be adjacent to reset");
  Require(undoButton.GetComponent<CalibrationTooltip>().Label.Contains("Ctrl+Z"),"Undo tooltip missing shortcut");
  var input=FindObjectsOfType<TMP_InputField>().First(x=>x.name=="Parameter value");
  input.ActivateInputField();yield return null;
  Require(!(bool)typeof(CalibrationDemoView).GetMethod("HandleUndoShortcut",flags).Invoke(view,null),"Typing must not undo calibration");input.DeactivateInputField();
  Click("横向跟随");yield return null;yield return Screenshot("guided-third.png");
  Click("距离校正");yield return null;
  Require(FindObjectsOfType<Evo.UI.Button>().Any(x=>x.name=="保存")&&!FindObjectsOfType<Evo.UI.Button>().Any(x=>x.name=="下一步"),"Fifth step must say save");
  var draftBeforeSettings=Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Session.Draft);
  typeof(CalibrationDemoView).GetField("currentPage",flags).SetValue(view,"设置");
  typeof(CalibrationDemoView).GetMethod("Build",flags).Invoke(view,null);
  float previewStart=Time.realtimeSinceStartup;
  while(Time.realtimeSinceStartup-previewStart<8){
   var image=(UnityEngine.UI.RawImage)typeof(CalibrationDemoView).GetField("cameraPreview",flags).GetValue(view);
   if(image&&image.enabled&&image.texture)break;
   yield return null;
  }
  var previewImage=(UnityEngine.UI.RawImage)typeof(CalibrationDemoView).GetField("cameraPreview",flags).GetValue(view);
  Require(previewImage.enabled&&previewImage.texture.width==640,"Settings preview did not receive/decode an inference frame");
  var previewBox=(RectTransform)previewImage.transform.parent;
  Require(previewBox.sizeDelta.x<=740,"Settings video container spans excessive blank width");
  foreach(float ratio in new[]{4f/3,16f/9,3f/4}){
   typeof(CalibrationDemoView).GetMethod("FitCameraPreview",flags).Invoke(view,new object[]{ratio});
   Canvas.ForceUpdateCanvases();var rect=previewImage.rectTransform;
   Require(Vector3.Distance(rect.TransformPoint(rect.rect.center),previewBox.TransformPoint(previewBox.rect.center))<.1f,"Settings image must be centered at every aspect ratio");
   Require(Mathf.Abs(rect.rect.width/rect.rect.height-ratio)<.001f,"Settings image aspect distorted");
  }
  typeof(CalibrationDemoView).GetMethod("FitCameraPreview",flags).Invoke(view,new object[]{4f/3});
  Require(runtime.CameraModes.Count==2,"Supported capture modes were not negotiated");
  Click("分辨率");yield return null;Click("1920 × 1080");
  float modeStart=Time.realtimeSinceStartup;
  while(Time.realtimeSinceStartup-modeStart<20){
   var active=runtime.ActualCameraMode;
   if(runtime.Fresh&&active!=null&&(int)active["width_px"]==1920)break;
   yield return null;
  }
  Require(runtime.Fresh&&(int)runtime.ActualCameraMode["width_px"]==1920&&(double)runtime.ActualCameraMode["fps"]==30,"Requested HD capture mode did not take effect");
  Require(!runtime.Compatible&&!runtime.CanSave,"Mode switch silently reused incompatible pixel-IPD calibration");
  string settingsFile=Path.Combine(CalibrationRuntime.Argument("--data-root"),"settings.json");
  Require((string)Newtonsoft.Json.Linq.JObject.Parse(File.ReadAllText(settingsFile))["camera_mode_id"]=="synthetic-1920-1080-30","Selected capture mode not persisted");
  var modeProbe=new GameObject("Capture settings restart probe");var restoredMode=modeProbe.AddComponent<CalibrationRuntime>();restoredMode.enabled=false;
  Require(restoredMode.CameraModeId=="synthetic-1920-1080-30"&&restoredMode.CameraInferenceHz==30,"Capture mode/FPS preferences not restored after restart");DestroyImmediate(modeProbe);
  runtime.ApplyCameraMode("removed-mode");
  Require(runtime.CameraModeId=="synthetic-1920-1080-30"&&runtime.CameraInferenceHz==30&&!runtime.Busy,"Stale mode selection polluted current camera preferences");
  yield return new WaitForSeconds(.6f);
  previewImage=(UnityEngine.UI.RawImage)typeof(CalibrationDemoView).GetField("cameraPreview",flags).GetValue(view);
  Require(previewImage.texture.width==640&&previewImage.texture.height==360,"HD preview did not adapt to native 16:9 geometry");
  var hdBox=(RectTransform)previewImage.transform.parent;
  Require(Mathf.Abs(hdBox.rect.width/hdBox.rect.height-16f/9)<.001f,"HD preview container retained the old aspect ratio");
  yield return Screenshot("settings-hd.png");
  Click("分辨率");yield return null;Click("系统当前模式");
  modeStart=Time.realtimeSinceStartup;
  while(Time.realtimeSinceStartup-modeStart<20){
   var active=runtime.ActualCameraMode;
   if(runtime.Fresh&&active!=null&&(int)active["width_px"]==640&&runtime.Compatible)break;
   yield return null;
  }
  Require(runtime.Fresh&&runtime.Compatible&&runtime.CameraModeId==null,"Automatic capture mode restore failed");
  yield return new WaitForSeconds(.6f);
  yield return Screenshot("settings-preview.png");
  Screen.SetResolution(1000,700,false);yield return new WaitForSeconds(.6f);Canvas.ForceUpdateCanvases();
  previewImage=(UnityEngine.UI.RawImage)typeof(CalibrationDemoView).GetField("cameraPreview",flags).GetValue(view);
  var previewCorners=new Vector3[4];previewImage.rectTransform.GetWorldCorners(previewCorners);
  Require(previewCorners.All(p=>p.x>=0&&p.y>=0&&p.x<=Screen.width&&p.y<=Screen.height),"Settings video clipped in small window");
  yield return Screenshot("settings-small.png");
  Screen.SetResolution(800,640,false);yield return new WaitForSeconds(.6f);Canvas.ForceUpdateCanvases();
  previewImage=(UnityEngine.UI.RawImage)typeof(CalibrationDemoView).GetField("cameraPreview",flags).GetValue(view);
  previewImage.rectTransform.GetWorldCorners(previewCorners);
  Require(previewCorners.All(p=>p.x>=0&&p.y>=0&&p.x<=Screen.width&&p.y<=Screen.height),"Settings stacked video clipped in compact window");
  yield return Screenshot("settings-compact.png");
  typeof(CalibrationDemoView).GetField("currentPage",flags).SetValue(view,"校准");
  typeof(CalibrationDemoView).GetMethod("Build",flags).Invoke(view,null);yield return null;
  Require(draftBeforeSettings==Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Session.Draft),"Settings preview changed calibration draft");
  runtime.SetPauseWhenInvisible(false);Require(!runtime.PauseWhenInvisible,"Pause preference setter");
  var preferencesProbe=new GameObject("Run preference persistence probe");var preferences=preferencesProbe.AddComponent<CalibrationRuntime>();preferences.enabled=false;
  Require(!preferences.PauseWhenInvisible,"Pause preference did not persist");DestroyImmediate(preferencesProbe);runtime.SetPauseWhenInvisible(true);
  bool quit=(bool)typeof(CalibrationDemoView).GetMethod("WantsToQuit",flags).Invoke(view,null);Require(!quit,"Unsaved exit was not guarded");
  Click("保存后离开");yield return null;Click("关闭");yield return null;
  Require(typeof(CalibrationDemoView).GetField("afterSave",flags).GetValue(view)==null,"Cancelled naming retained exit action");
  foreach(var size in new[]{new Vector2Int(1366,768),new Vector2Int(1000,700),new Vector2Int(1600,900)}){
   string before=Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Session.Draft.manual);Screen.SetResolution(size.x,size.y,false);yield return new WaitForSeconds(.6f);Canvas.ForceUpdateCanvases();
   foreach(var field in FindObjectsOfType<TMP_InputField>()){
    var corners=new Vector3[4];field.GetComponent<RectTransform>().GetWorldCorners(corners);
    Require(corners.All(p=>p.x>=0&&p.y>=0&&p.x<=Screen.width&&p.y<=Screen.height),"Parameter clipped after resize "+size);
   }
   Require(before==Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Session.Draft.manual),"Resize rewrote device parameters");yield return Screenshot("window-"+size.x+".png");
  }
  string manualBefore=Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Session.Draft.manual);
  Click("全屏校准");yield return new WaitForSeconds(.8f);Require(runtime.CanEdit&&runtime.ViewportValid,"Fullscreen lost binding");
  Require(manualBefore==Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Session.Draft.manual),"Fullscreen rewrote mapping");
  Click("纵向跟随");Enter("5.2");yield return null;Require(Math.Abs(runtime.Session.Draft.manual.scaleY-5.2)<1e-6,"Fullscreen editing");
  yield return new WaitForEndOfFrame();
  var selected=FindObjectsOfType<Evo.UI.Button>().First(x=>x.name=="纵向跟随");
  Require(selected.GetComponent<CalibrationButtonVisual>().StepCircle.color==CalibrationWidgets.Blue,"Selected parameter highlight disagrees with active input");
  yield return Screenshot("fullscreen.png");Click("返回控制台");yield return new WaitForSeconds(.8f);
  Click("恢复本项");yield return null;Require(runtime.Session.Draft.manual.scaleY==4.38,"Restore selected parameter");Click("撤销");yield return null;Require(runtime.Session.Draft.manual.scaleY==5.2,"Undo restore");
  Click("进阶");yield return null;Click("另存为");yield return null;
  var name=FindObjectsOfType<TMP_InputField>().First(x=>x.transform.parent.name=="Dialog");name.text="五参数软件验证";Click("保存");yield return null;
  Require(runtime.Applied==null&&!runtime.ShowingExhibit,"Save-as must not apply before finish");
  string savedId=runtime.Session.Draft.id;Click("水平位置");Enter("0.2");yield return null;Click("进阶");yield return null;Click("取消修改");yield return null;
  Require(runtime.Session.Draft.manual.offsetX==.1&&runtime.Applied==null,"Cancel must restore selected saved preset without applying it");
  Click("进阶");yield return null;Click("选择预设");yield return null;Click("五参数软件验证");yield return null;
  Require(runtime.Session.Draft.id==savedId&&runtime.Applied==null,"Preset switch must not apply");
  Click("距离校正");yield return null;Click("保存");yield return new WaitForSeconds(.8f);
  Require(runtime.Applied!=null&&runtime.Applied.schema_version==2&&runtime.ShowingExhibit,"Save/apply failed");Require(runtime.Store.List().Any(p=>p.id==runtime.Applied.id),"Saved preset missing");
  var appliedBefore=Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Applied);
  runtime.SetExhibit(false);yield return null;yield return null;
  var unsaved=runtime.Session.Draft;unsaved.manual.offsetX+=.02;runtime.Session.BeginEdit();runtime.Session.Edit(unsaved);runtime.Session.CommitEdit();yield return null;
  Require(runtime.Save(runtime.Session.Draft.name,false,false),"Save without apply failed");
  Require(appliedBefore==Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Applied),"Save without finish changed Applied");
  var probe=new GameObject("Restart persistence probe");var restarted=probe.AddComponent<CalibrationRuntime>();restarted.enabled=false;
  Require(appliedBefore==Newtonsoft.Json.JsonConvert.SerializeObject(restarted.Applied),"Restart applied a saved but unfinished revision");DestroyImmediate(probe);
  runtime.SetExhibit(true);yield return null;yield return null;
  var mismatched=runtime.Session.Draft;mismatched.manual.display_id="different-display";runtime.Session.BeginEdit();runtime.Session.Edit(mismatched);runtime.Session.CommitEdit();yield return null;yield return null;
  Require(!runtime.CanEdit&&!runtime.CanSave&&runtime.projection.TrackingFresh,"Applied render and draft compatibility must be independent");
  Require(appliedBefore==Newtonsoft.Json.JsonConvert.SerializeObject(runtime.Applied),"Draft changed Applied");runtime.Session.Discard();yield return null;
  runtime.Session.Discard();runtime.NewPreset();runtime.SetExhibit(false);
  typeof(CalibrationDemoView).GetField("currentPage",flags).SetValue(view,"校准");typeof(CalibrationDemoView).GetMethod("Build",flags).Invoke(view,null);
  runtime.enabled=true;view.enabled=true;yield return null;yield return null;yield return null;
  Require(runtime.CanSave,"New first-use draft not ready");
  for(int i=0;i<4;i++){Click("下一步");yield return null;}
  Click("全屏校准");yield return new WaitForSeconds(.8f);yield return Screenshot("guided-save-fullscreen.png");
  Click("保存");yield return new WaitForSeconds(.8f);
  Require(!(bool)typeof(CalibrationDemoView).GetField("fullscreen",flags).GetValue(view),"Saving must exit fullscreen calibration");
  Require(((RectTransform)typeof(CalibrationDemoView).GetField("canvas",flags).GetValue(view)).gameObject.activeInHierarchy&&!GameObject.Find("Fullscreen controls"),"Saving left hidden canvas or old controls");
  Require(runtime.Applied!=null&&runtime.Applied.name.StartsWith("默认校准"),"First-use save requested preset naming");

  yield return Screenshot("showcase.png");yield return CaptureGeometry(output,view);
  File.WriteAllText(Path.Combine(output,"result.txt"),"PASS guided-first-save/direct-mode-dropdown/shortcut-focus/tooltip/adjacent-undo-reset/run-preference; five numeric controls/invalid input/safe correction/exit-cancel/resize/fullscreen/edit/restore/undo/schema2-save/apply/restart-applied-snapshot/5-aspect-7-eye geometry/opaque-depth-fade. Synthetic tracking; human accuracy not measured.");
  runtime.Session.Discard();Application.Quit(0);
 }
 IEnumerator Screenshot(string name){yield return new WaitForEndOfFrame();ScreenCapture.CaptureScreenshot(Path.Combine(output,name));yield return null;}
 static void Enter(string text){var input=FindObjectsOfType<TMP_InputField>().First(x=>x.name=="Parameter value");input.text=text;input.onEndEdit.Invoke(text);}
 IEnumerator CaptureGeometry(string output,CalibrationDemoView view){
  runtime.enabled=false;view.enabled=false;
  var projection=runtime.projection;var camera=projection.GetComponent<Camera>();
  runtime.modelRoot.gameObject.SetActive(false);runtime.room.gameObject.SetActive(true);var room=runtime.room;
  var oldTexture=camera.targetTexture;var target=new RenderTexture(960,540,24,RenderTextureFormat.ARGB32);target.Create();camera.targetTexture=target;camera.rect=new Rect(0,0,1,1);
  int checks=0;float maxError=0;string[] ratios={"16x9","16x10","4x3","21x9","9x16"};float[] aspects={16f/9,16f/10,4f/3,21f/9,9f/16};
  for(int ratio=0;ratio<aspects.Length;ratio++){
   float w=.53f,h=w/aspects[ratio];room.SetDimensions(w,h);projection.displayWidth=w;projection.displayHeight=h;
   Require((room.Cube.localScale-Vector3.one*room.Cube.localScale.x).sqrMagnitude<1e-10,"Cube stretched");
   Require((room.Cube.localPosition-new Vector3(0,0,room.depth/2)).sqrMagnitude<1e-10,"Cube is not at the room center");
   target.Release();target.width=960;target.height=Mathf.RoundToInt(960/aspects[ratio]);target.Create();
   string[] names={"front","left","right","up","down","near","far"};
   var eyes=new[]{new Vector3(0,0,-.6f),new Vector3(-w/2,0,-.6f),new Vector3(w/2,0,-.6f),new Vector3(0,h/2,-.6f),new Vector3(0,-h/2,-.6f),new Vector3(0,0,-.15f),new Vector3(0,0,-1.2f)};
   for(int i=0;i<eyes.Length;i++){
    Require(projection.SetEye(eyes[i]),"Projection rejected eye");yield return new WaitForEndOfFrame();
    var corners=room.Aperture();for(int j=0;j<4;j++){
     var actual=camera.WorldToViewportPoint(room.transform.TransformPoint(corners[j]));var expected=new Vector2(j==1||j==2?1:0,j>=2?1:0);
     float error=Vector2.Scale(new Vector2(actual.x,actual.y)-expected,new Vector2(target.width,target.height)).magnitude;maxError=Mathf.Max(maxError,error);Require(error<.01f,"Player aperture moved");checks++;
    }
    var old=RenderTexture.active;RenderTexture.active=target;var pixels=new Texture2D(target.width,target.height,TextureFormat.RGBA32,false);
    pixels.ReadPixels(new Rect(0,0,target.width,target.height),0,0);pixels.Apply();
    if(i==0){var background=pixels.GetPixel(Mathf.RoundToInt(target.width*.72f),target.height/2);Require(background.a>.99f,"Depth fade must remain opaque in the UI preview texture");Require(Mathf.Max(background.r,background.g,background.b)<.025f,"Back wall must fade fully into darkness");var center=camera.WorldToViewportPoint(room.Cube.position);Require(Mathf.Abs(center.x-.5f)<1e-5&&Mathf.Abs(center.y-.5f)<1e-5,"Centered cube must project to horizontal and vertical symmetry center");}
    File.WriteAllBytes(Path.Combine(output,ratios[ratio]+"-"+names[i]+".png"),pixels.EncodeToPNG());Destroy(pixels);RenderTexture.active=old;
   }
  }
  File.WriteAllText(Path.Combine(output,"player-geometry.txt"),"PASS "+checks+" aperture corners; max error "+maxError+" pixels; 35 actual room views; cube uniform.");
  camera.targetTexture=oldTexture;target.Release();Destroy(target);
 }
 static void Click(string name){var button=FindObjectsOfType<Evo.UI.Button>().First(x=>x.gameObject.name==name);if(!button.IsInteractable())throw new Exception("Disabled control: "+name);button.onClick.Invoke();}
}
