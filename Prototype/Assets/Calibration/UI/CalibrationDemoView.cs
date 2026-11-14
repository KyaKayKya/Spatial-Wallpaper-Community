using System;
using System.Globalization;
using System.Collections.Generic;
using System.Linq;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using SpatialTracking;

[DefaultExecutionOrder(-100)]
public sealed partial class CalibrationDemoView:MonoBehaviour {
 public CalibrationRuntime runtime;
 public ICalibrationService Service {get;set;}
 public TMP_FontAsset font;
 CalibrationWidgets ui;RectTransform canvas,page,parameters,modal;
 TMP_Text status,hint,presetLabel;RawImage preview;RenderTexture texture;
 RawImage cameraPreview;Texture2D cameraTexture;TMP_Text cameraInfo;float cameraReceived;string cameraSummary;
 float cameraMaxWidth,cameraMaxHeight,cameraTop,cameraLeft;
 readonly List<Action> refreshParameters=new List<Action>();
 readonly List<Selectable> editable=new List<Selectable>();
 readonly Dictionary<CalibrationTool,CalibrationButtonVisual> toolVisuals=new Dictionary<CalibrationTool,CalibrationButtonVisual>();
 Evo.UI.Button save,reference,undo,restore,restoreAll;
 float pageWidth,pageHeight,previewHeight,previewTop;
 int layoutWidth,layoutHeight;
 string currentPage="校准";bool refreshing;
 bool quitApproved;Action afterSave;
 void Start(){
  library=GetComponent<LibraryPreviewController>();if(library)currentPage="作品库";
  if(Service==null)Service=runtime;
  runtime.projection.viewportManagedExternally=true;
  ui=new CalibrationWidgets(font);
  var go=new GameObject("CalibrationCanvas",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));canvas=(RectTransform)go.transform;go.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
  var scaler=go.GetComponent<CanvasScaler>();scaler.uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;scaler.scaleFactor=DisplayViewport.UiScale;
  if(!FindObjectOfType<EventSystem>())new GameObject("EventSystem",typeof(EventSystem),typeof(StandaloneInputModule));
  Service.Changed+=Refresh;
  Application.wantsToQuit+=WantsToQuit;
  Build();
 }
 void Clear(Transform root){for(int i=root.childCount-1;i>=0;i--){root.GetChild(i).gameObject.SetActive(false);Destroy(root.GetChild(i).gameObject);}}
 void Build(){
  if(currentPage!="校准")runtime.CancelVerticalMeasurements();
  var desktop=GetComponent<SpatialWallpaper.Desktop.DesktopManager>();if(desktop&&desktop.enabled)desktop.SetPagePreview(currentPage=="校准"||currentPage=="展示柜");
  CloseChoices();
  runtime.SetCameraPreview(currentPage=="设置");
  cameraPreview=null;cameraInfo=null;cameraSummary=null;if(cameraTexture){Destroy(cameraTexture);cameraTexture=null;}
  layoutWidth=Screen.width;layoutHeight=Screen.height;
  float scale=library&&currentPage=="作品库"?Mathf.Max(1,DisplayViewport.UiScale):DisplayViewport.UiScale;canvas.GetComponent<CanvasScaler>().scaleFactor=scale;
  float width=Screen.width/scale,height=Screen.height/scale,nav=width<1100?(library?56:128):184;
  pageWidth=Mathf.Max(360,width-nav-48);pageHeight=Mathf.Max(520,height-48);
  previewTop=132;previewHeight=Mathf.Max(120,pageHeight-previewTop-232);
  CloseChoices();guideContent=null;verticalFeedback=null;nextStep=null;previousStep=null;guideTitle=null;demoButton=null;hint=null;presetLabel=null;cameraDropdown=null;resolutionDropdown=null;rateDropdown=null;
  Clear(canvas);modal=null;editable.Clear();refreshParameters.Clear();toolVisuals.Clear();save=null;reference=null;undo=null;restore=null;restoreAll=null;preview=null;
  var background=ui.Panel(canvas,"Background",0,0,1440,960,CalibrationWidgets.Surface);background.anchorMin=Vector2.zero;background.anchorMax=Vector2.one;background.offsetMin=background.offsetMax=Vector2.zero;
  ui.Panel(canvas,"Navigation",0,0,nav,height,new Color32(234,240,246,255));
  ui.Text(canvas,nav<100?"SW":"Spatial\nWallpaper",16,24,nav-24,64,20);
  if(library)BuildCompactNavigation(nav);
  else {
  ui.Button(canvas,library?"本地作品库":"展示柜体验",12,128,nav-24,()=>Leave(()=>NavigateLibrary()),currentPage=="作品库"||currentPage=="作品预览");
  ui.Button(canvas,"校准",12,180,nav-24,()=>{if(library){if(currentPage=="作品预览")resumeCameraPreview=library.CameraMode;library.ClosePreview();}runtime.SetExhibit(false);currentPage="校准";Build();},currentPage=="校准");
  ui.Button(canvas,"设置",12,232,nav-24,()=>Leave(()=>{if(library){if(currentPage=="作品预览")resumeCameraPreview=library.CameraMode;library.ClosePreview();}currentPage="设置";Build();}),currentPage=="设置");
  }
  page=ui.Rect(canvas,"Page",nav+24,24,pageWidth,pageHeight);
  ui.Text(page,currentPage=="作品库"?"作品库":currentPage=="作品预览"?library.Selected.Name:currentPage=="展示柜"?"白色展示柜":currentPage=="设置"?"设置":"空间校准",0,0,currentPage=="作品库"?104:480,40,24);
  status=ui.Text(page,"",0,52,pageWidth,40,14,CalibrationWidgets.Muted);
  if(library&&(currentPage=="作品库"||currentPage=="作品预览")){BuildLibrarySurface();Refresh();return;}
  if(currentPage=="设置")BuildSettings();
  else {
   BuildPreview();
   if(currentPage=="校准")BuildCalibration();
   else {
    ui.Button(page,"返回校准",0,pageHeight-100,132,()=>{runtime.SetExhibit(false);currentPage="校准";Build();});
    ui.Button(page,"全屏体验",148,pageHeight-100,132,()=>ToggleFullscreen());
    ui.Text(page,"已应用："+(Service.Applied?.name??"尚未保存校准")+" · 移动头部观察空间",0,pageHeight-48,pageWidth,40,14,CalibrationWidgets.Muted);
   }
  }
  Refresh();
 }
 public void ShowExhibitPage(){currentPage="展示柜";Build();}
 void BuildPreview(){
  float height=currentPage=="展示柜"?pageHeight-previewTop-128:previewHeight;
  var container=ui.Panel(page,"Preview",0,previewTop,pageWidth,height,new Color32(226,231,237,255));
  float w=pageWidth,h=height;
  if(currentPage=="展示柜"){w=Mathf.Min(w,height*16/9);h=w*9/16;}
  var r=ui.Rect(container,"Spatial image",(pageWidth-w)/2,(height-h)/2,w,h);preview=r.gameObject.AddComponent<RawImage>();
  if(!texture){texture=new RenderTexture(1600,900,24,RenderTextureFormat.ARGB32);texture.antiAliasing=2;texture.Create();}
  preview.texture=texture;var camera=runtime.projection.GetComponent<Camera>();camera.targetTexture=texture;camera.rect=new Rect(0,0,1,1);
 }
 void BuildCalibration(){BuildGuidedCalibration();}
 void BuildTools(Transform parent,float y,float width){BuildGuideTools(parent,y,width);}
 void BindOrMigrate(){
  if(Service.Session.Draft.schema_version!=1){Service.BindCurrentSource();Refresh();return;}
  Modal("迁移旧预设",out var box);ui.Text(box,"旧映射不能等价转换为五参数。\n创建默认五参数草稿，原文件与旋转值保持保留。",24,72,512,100,14);
  ui.Button(box,"创建五参数草稿",24,208,240,()=>{var next=Service.Session.Draft.CreateManualDraft();CloseModal();Service.SelectPreset(next);Build();},true);
 }
 void BuildParameters(){
  Clear(parameters);refreshParameters.Clear();editable.Clear();
  var tool=Service.Session.Tool;
  if(tool==CalibrationTool.OffsetX)Field("水平位置",0,-1,1,p=>p.manual.offsetX,(p,v)=>p.manual.offsetX=v);
  if(tool==CalibrationTool.OffsetY)Field("垂直位置",0,-1,1,p=>p.manual.offsetY,(p,v)=>p.manual.offsetY=v);
  if(tool==CalibrationTool.ScaleX)Field("横向跟随",0,.05,30,p=>p.manual.scaleX,(p,v)=>p.manual.scaleX=v);
  if(tool==CalibrationTool.ScaleY)Field("纵向跟随",0,.05,30,p=>p.manual.scaleY,(p,v)=>p.manual.scaleY=v);
  if(tool==CalibrationTool.ScaleZ)Field("距离校正",0,.1,30,p=>p.manual.scaleZ,(p,v)=>p.manual.scaleZ=v);
 }
 void Field(string label,int index,double min,double max,Func<CalibrationProfile,double> get,Action<CalibrationProfile,double> set){
  float x=0,width=parameters.sizeDelta.x;
  var input=ui.Input(parameters,"",width-224,0,112);input.gameObject.name="Parameter value";
  var slider=ui.Slider(parameters,x,2,width-248,(float)min,(float)max,(float)get(Service.Session.Draft),v=>{
   if(refreshing||!Service.CanEdit)return;Service.Session.BeginEdit();EditParameter(v,set);Service.NotifyEdit();
  },()=>Service.Session.BeginEdit(),()=>{Service.Session.CommitEdit();Refresh();});
  input.onEndEdit.AddListener(text=>{
   if(refreshing||!Service.CanEdit||!string.IsNullOrEmpty(Input.compositionString))return;
   if(!double.TryParse(text,NumberStyles.Float,CultureInfo.InvariantCulture,out double value)||value<min||value>max||!TrackingCalibration.Finite(value)){input.GetComponent<Image>().color=new Color32(255,218,213,255);Service.Report("请输入范围内的有效数值");return;}
   Service.Session.BeginEdit();bool ok=EditParameter(value,set);Service.Session.CommitEdit();input.GetComponent<Image>().color=ok?Color.white:new Color32(255,218,213,255);if(ok)Service.Report("参数已更新");
  });
  undo=IconButton(parameters,"撤销","undo",width-96,0,UndoAdjustment);
  restore=IconButton(parameters,"恢复本项","reset",width-48,0,()=>{Service.Session.CommitEdit();try{Service.Session.RestoreTool(runtime.ViewCenter.x,runtime.ViewCenter.y);Service.NotifyEdit();}catch(InvalidOperationException e){Service.Report(e.Message);}});
  editable.Add(slider);editable.Add(input);refreshParameters.Add(()=>{double v=get(Service.Session.Draft);slider.SetValueWithoutNotify((float)v);if(!input.isFocused)input.SetTextWithoutNotify(v.ToString("0.000",CultureInfo.InvariantCulture));});
 }
 void BuildSettings(){BuildSettingsSurface();}
 bool EditParameter(double value,Action<CalibrationProfile,double> set){
  var tool=Service.Session.Tool;bool ok;
  if(tool==CalibrationTool.ScaleX||tool==CalibrationTool.ScaleY)ok=Service.Session.AdjustFollow(tool,value,tool==CalibrationTool.ScaleX?runtime.ViewCenter.x:runtime.ViewCenter.y);
  else{var p=Service.Session.Draft;set(p,value);ok=Service.Session.Edit(p);}
  if(!ok)Service.Report("中心补偿超出有效范围，请先重新调整位置");return ok;
 }
 void Refresh(){
  if(!status)return;refreshing=true;
  if(library&&(currentPage=="作品库"||currentPage=="作品预览")){RefreshLibrary();refreshing=false;return;}
  status.text=Service.Busy?"正在切换摄像头…":!Service.Fresh?(Service.CameraId==null?"请先在设置中选择摄像头":Service.Message):!runtime.ViewportValid?"请将窗口移到同一显示器":!Service.Compatible?"设备已变化，请在进阶中选择或新建校准":"";
  if(!string.IsNullOrEmpty(operationError))status.text=operationError;
  if(currentPage=="设置")status.text="";
  if(Service.CanEdit&&!Service.CanSave)status.text="当前参数对应的眼位超出安全范围，请继续调整或撤销；暂不能保存。";
  if(presetLabel)presetLabel.text=(string.IsNullOrWhiteSpace(Service.Session.Draft.name)?"未命名预设":Service.Session.Draft.name)+(Service.Session.Dirty?" · 未保存":"");
  if(reference)reference.SetInteractable(Service.Session.Draft.schema_version==1||Service.Fresh&&!Service.Busy);
  if(save)save.SetInteractable(Service.CanSave);
  if(undo)undo.SetInteractable(Service.CanEdit&&Service.Session.CanUndo);
  if(restore)restore.SetInteractable(Service.CanEdit);
  if(restoreAll)restoreAll.SetInteractable(Service.CanEdit);
  foreach(var control in editable)if(control)control.interactable=Service.CanEdit;
  foreach(var action in refreshParameters)action();
  foreach(var item in toolVisuals)if(item.Value)item.Value.Selected=item.Key==Service.Session.Tool;
  if(currentPage=="校准")RefreshGuide();
  if(currentPage=="设置")RefreshSettingsSurface();
  refreshing=false;
 }
 void ShowPresets(){ShowPresetPage(0);}
 void ShowPresetPage(int index){
  Modal("校准预设",out var box);try{var presets=Service.Store.List();int first=index*4;for(int i=first;i<Math.Min(first+4,presets.Length);i++){var p=presets[i];ui.Button(box,p.name,24,64+(i-first)*48,512,()=>{CloseModal();Leave(()=>{Service.SelectPreset(p);Build();});});}
   ui.Text(box,"选择后可调整，第五步保存时生效。",24,264,512,28,12,CalibrationWidgets.Muted);
   ui.Button(box,"创建新预设",24,312,180,()=>{CloseModal();Leave(()=>{Service.NewPreset();Build();});});
   if(index>0)ui.Button(box,"上一页",284,312,116,()=>ShowPresetPage(index-1));if(first+4<presets.Length)ui.Button(box,"下一页",412,312,124,()=>ShowPresetPage(index+1));
  }catch(Exception){ui.Text(box,"预设读取失败，请检查本地文件。",24,64,512,40);}
 }
 void Save(bool asCopy,bool finish){if(!Service.CanEdit){Service.Report("需要有效追踪后才能保存");return;}if(asCopy||string.IsNullOrWhiteSpace(Service.Session.Draft.name)){NamePreset(asCopy,finish);return;}if(Service.Save(Service.Session.Draft.name,false,finish)&&finish)CompleteCalibration();}
 void CompleteCalibration(){if(fullscreen)ToggleFullscreen();if(library){if(ContinueLibraryApplication())return;if(resumeCameraPreview){resumeCameraPreview=false;EnterArtworkPreview(true);}else NavigateLibrary();}else{currentPage="展示柜";Build();}}
 void NamePreset(bool asCopy,bool finish){Modal(asCopy?"另存为预设":"为预设命名",out var box);var input=ui.Input(box,asCopy?Service.Session.Draft.name+" 副本":Service.Session.Draft.name,24,72,512);var error=ui.Text(box,"",24,200,512,72,14,new Color32(180,35,24,255));ui.Button(box,"取消",24,144,120,()=>{afterSave=null;CloseModal();});ui.Button(box,"保存",416,144,120,()=>{if(Service.Save(input.text,asCopy,finish)){CloseModal();if(afterSave!=null){var next=afterSave;afterSave=null;next();}else if(finish)CompleteCalibration();else Refresh();}else{input.GetComponent<Image>().color=new Color32(255,218,213,255);error.text=Service.Message;}},true);input.ActivateInputField();}
 void Leave(Action action){
  Service.Session.CommitEdit();if(!Service.Session.Dirty){action();return;}
  Modal("有未保存的修改",out var box);ui.Text(box,"离开前要保存本次调整吗？",24,64,512,40);
  ui.Button(box,"不保存离开",24,136,152,()=>{CloseModal();Service.Session.Discard();Service.NotifyEdit();action();});
  ui.Button(box,"继续编辑",196,136,152,CloseModal);
  ui.Button(box,"保存后离开",368,136,168,()=>{if(string.IsNullOrWhiteSpace(Service.Session.Draft.name)){CloseModal();afterSave=action;NamePreset(false,false);return;}if(Service.Save(Service.Session.Draft.name,false,false)){CloseModal();action();}},true);
 }
 void Modal(string title,out RectTransform box){CloseModal();float scale=canvas.GetComponent<Canvas>().scaleFactor,w=Screen.width/scale,h=Screen.height/scale;modal=ui.Panel(canvas,"Modal overlay",0,0,w,h,new Color(0,0,0,.25f));box=ui.Panel(modal,"Dialog",(w-560)/2,(h-420)/2,560,420,CalibrationWidgets.Surface);ui.Text(box,title,24,16,440,40,20);ui.Button(box,"关闭",448,16,88,()=>{afterSave=null;CloseModal();});}
 void CloseModal(){if(modal){modal.gameObject.SetActive(false);Destroy(modal.gameObject);modal=null;}}
 bool fullscreen;GameObject fullscreenControls;int windowWidth,windowHeight;
 void ToggleFullscreen(){
  fullscreen=!fullscreen;
  if(fullscreen){
   windowWidth=Screen.width;windowHeight=Screen.height;
   canvas.gameObject.SetActive(false);runtime.projection.GetComponent<Camera>().targetTexture=null;Screen.SetResolution(Screen.currentResolution.width,Screen.currentResolution.height,FullScreenMode.FullScreenWindow);
   BuildFullscreenControls();
  }else{if(fullscreenControls){fullscreenControls.SetActive(false);Destroy(fullscreenControls);}canvas.gameObject.SetActive(true);Screen.SetResolution(windowWidth,windowHeight,FullScreenMode.Windowed);Build();}
 }
 void BuildFullscreenControls(){
  layoutWidth=Screen.width;layoutHeight=Screen.height;
  if(fullscreenControls){fullscreenControls.SetActive(false);Destroy(fullscreenControls);}
  fullscreenControls=new GameObject("Fullscreen controls",typeof(RectTransform),typeof(Canvas),typeof(CanvasScaler),typeof(GraphicRaycaster));fullscreenControls.GetComponent<Canvas>().renderMode=RenderMode.ScreenSpaceOverlay;
  var scale=fullscreenControls.GetComponent<CanvasScaler>();scale.uiScaleMode=CanvasScaler.ScaleMode.ConstantPixelSize;scale.scaleFactor=DisplayViewport.UiScale;
  float w=Screen.width/scale.scaleFactor,h=Screen.height/scale.scaleFactor,tray=currentPage=="校准"?284:0;
  if(library&&currentPage=="作品预览"){
   BuildFullscreenArtworkDrag(fullscreenControls.transform,w,h);
   var back=IconButton(fullscreenControls.transform,"返回作品预览","back",16,16,ToggleFullscreen);
   Destroy(back.GetComponent<CalibrationButtonVisual>());back.GetComponent<Graphic>().color=Color.clear;
   back.GetComponentInChildren<Image>().color=Color.white;
   return;
  }
  var panel=ui.Panel(fullscreenControls.transform,"Controls",0,h-tray,w,tray,CalibrationWidgets.Surface);
  ui.Button(panel,"返回控制台",w-184,12,160,ToggleFullscreen);
  if(library&&currentPage=="作品预览")BuildFullscreenArtworkDrag(fullscreenControls.transform,w,h-tray);
  if(currentPage=="校准"){
   refreshParameters.Clear();editable.Clear();BuildTools(panel,12,w-208);
   var guide=ui.Rect(panel,"Guide",24,64,w-48,208);BuildGuideControls(guide,0,w-48,164);Refresh();
  }
 }
 void Update(){
  if(library&&!fullscreen&&!Screen.fullScreen&&(Screen.width<800||Screen.height<640)){Screen.SetResolution(Mathf.Max(800,Screen.width),Mathf.Max(640,Screen.height),FullScreenMode.Windowed);return;}
  UpdateCameraPreview();
  RefreshVerticalControls();
  if(currentPage=="校准"&&ReferenceEquals(Service,runtime))runtime.InitializeFirstCalibration();
  if(Input.GetKeyDown(KeyCode.Z)&&(Input.GetKey(KeyCode.LeftControl)||Input.GetKey(KeyCode.RightControl)))HandleUndoShortcut();
  if(choicePopup&&Input.GetKeyDown(KeyCode.Escape))CloseChoices();
  if(currentPage=="设置")RefreshSettingsSurface();
  if(fullscreen&&Input.GetKeyDown(KeyCode.Escape))ToggleFullscreen();
  if(fullscreen&&(layoutWidth!=Screen.width||layoutHeight!=Screen.height))BuildFullscreenControls();
  if(!fullscreen&&!modal&&(layoutWidth!=Screen.width||layoutHeight!=Screen.height)){Service.Session.CommitEdit();Build();}
 }
 void UpdateCameraPreview(){
  if(currentPage!="设置"||!cameraPreview)return;
  var frame=runtime.TakeCameraPreview();
  if(frame!=null){
   if(!cameraTexture)cameraTexture=new Texture2D(2,2,TextureFormat.RGB24,false);
   if(!ImageConversion.LoadImage(cameraTexture,frame.Jpeg,false)){cameraInfo.text="预览图像解码失败";cameraPreview.enabled=false;return;}
   cameraTexture.wrapMode=TextureWrapMode.Clamp;cameraPreview.texture=cameraTexture;cameraPreview.enabled=true;
   FitCameraPreview((float)frame.Width/frame.Height);
   cameraReceived=Time.realtimeSinceStartup;
   cameraSummary=(frame.FaceFound?"已识别人脸":"未识别人脸")+" · 瞳距 "+(frame.IpdPixels?.ToString("0.0",CultureInfo.InvariantCulture)??"—")+" px · 推理 "+frame.InferenceMs.ToString("0.0",CultureInfo.InvariantCulture)+" ms";
   cameraInfo.text=frame.FaceFound?"已连接":"未识别人脸";
  }else if(cameraSummary!=null&&Time.realtimeSinceStartup-cameraReceived>1){cameraPreview.enabled=false;cameraInfo.text="预览中断";}
 }
 void FitCameraPreview(float aspect){
  var box=(RectTransform)cameraPreview.transform.parent;var r=cameraPreview.rectTransform;
  float w=Mathf.Min(cameraMaxWidth,cameraMaxHeight*aspect),h=w/aspect;box.sizeDelta=new Vector2(w,h);
  r.anchorMin=r.anchorMax=r.pivot=new Vector2(.5f,.5f);r.anchoredPosition=Vector2.zero;r.sizeDelta=new Vector2(w,h);

 }

 void LateUpdate(){
  if(!runtime.projection)return;
  if(library&&(currentPage=="作品库"||currentPage=="作品预览")){MeasureLibrary();return;}
  if(fullscreen){
   float controls=currentPage=="校准"?284*DisplayViewport.UiScale:64*DisplayViewport.UiScale;
   float w=Screen.width,h=Screen.height-controls;
   if(currentPage=="展示柜"){w=Mathf.Min(w,h*16/9);h=w*9/16;}
   var aperture=new Rect((Screen.width-w)/2,controls+(Screen.height-controls-h)/2,w,h);
   runtime.projection.GetComponent<Camera>().rect=new Rect(aperture.x/Screen.width,aperture.y/Screen.height,w/Screen.width,h/Screen.height);
   Measure(aperture);return;
  }
  if(!preview||!preview.gameObject.activeInHierarchy)return;
  var corners=new Vector3[4];preview.rectTransform.GetWorldCorners(corners);
  var apertureRect=new Rect(corners[0].x,corners[0].y,corners[2].x-corners[0].x,corners[2].y-corners[0].y);
  Measure(apertureRect);
  int tw=Mathf.Max(1,Mathf.RoundToInt(apertureRect.width)),th=Mathf.Max(1,Mathf.RoundToInt(apertureRect.height));
  if(texture.width!=tw||texture.height!=th){texture.Release();texture.width=tw;texture.height=th;texture.Create();}
 }
 void Measure(Rect aperture){
  var profile=runtime.ShowingExhibit?Service.Applied:Service.Session.Draft;
  double monitorWidth=profile!=null&&profile.schema_version==2?profile.manual.display_width:.53;
  if(DisplayViewport.TryMeasure(aperture,monitorWidth,out var metrics,out var display))runtime.SetViewport(metrics,display);
  else runtime.SetViewport(null,null);
 }
 bool WantsToQuit(){if(quitApproved||!Service.Session.Dirty)return true;if(fullscreen)ToggleFullscreen();Leave(()=>{quitApproved=true;Application.Quit();});return false;}
 public void QuitFromDesktop(){quitApproved=true;Application.Quit();}
 void OnDestroy(){Application.wantsToQuit-=WantsToQuit;if(runtime)runtime.SetCameraPreview(false);if(cameraTexture)Destroy(cameraTexture);if(Service!=null)Service.Changed-=Refresh;if(texture){texture.Release();Destroy(texture);}if(canvas)Destroy(canvas.gameObject);if(fullscreenControls)Destroy(fullscreenControls);}
}
