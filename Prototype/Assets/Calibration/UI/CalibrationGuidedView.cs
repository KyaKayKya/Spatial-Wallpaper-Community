using System;
using System.Linq;
using SpatialTracking;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;

public sealed partial class CalibrationDemoView {
 public event Action<CalibrationTool> DemonstrationRequested;
 Evo.UI.Button nextStep,previousStep,demoButton;TMP_Text guideTitle;string operationError;
 int Step=>Mathf.Clamp((int)Service.Session.Tool-(int)CalibrationTool.OffsetX,0,4);
 RectTransform guideContent;float guideWidth,guideFooter;bool verticalManual,horizontalManual;
 bool EdgeStep=>Step==2||Step==3;
 bool ManualEdges=>Step==2?horizontalManual:verticalManual;
 TMP_Text verticalFeedback;Evo.UI.Button captureTop,captureBottom,solveVertical,captureCenter;
 void BuildGuidedCalibration(){
  var advanced=ui.Button(page,"进阶",pageWidth-164,0,108,ShowAdvanced);
  IconButton(page,"全屏校准","fullscreen",pageWidth-40,0,ToggleFullscreen);
  BuildTools(page,76,pageWidth);
  BuildGuideControls(page,previewTop+previewHeight+12,pageWidth,pageHeight-44);
 }
 void BuildGuideControls(Transform parent,float y,float width,float footer){
  guideContent=ui.Rect(parent,"Calibration guide",0,y,width,footer-y+40);guideWidth=width;guideFooter=footer-y;
  FillGuideControls();
 }
 void FillGuideControls(){
  var parent=guideContent;float width=guideWidth,footer=guideFooter,y=0;
  refreshParameters.Clear();editable.Clear();verticalFeedback=null;captureTop=captureBottom=solveVertical=captureCenter=null;undo=restore=null;
  guideTitle=ui.Text(parent,CalibrationGuide.Names[Step],0,y,width-200,32,20);
  if(EdgeStep){
   demoButton=ui.Button(parent,ManualEdges?"两点校准":"手动微调",width-128,y,128,()=>{if(Step==2)horizontalManual=!horizontalManual;else verticalManual=!verticalManual;Clear(guideContent);FillGuideControls();Refresh();});
  }else{
  demoButton=ui.Button(parent,"查看示范",width-128,y,128,()=>DemonstrationRequested?.Invoke(Service.Session.Tool));
  if(DemonstrationRequested==null)AddTooltip(demoButton.gameObject,"示范动效待接入");
  demoButton.SetInteractable(DemonstrationRequested!=null);
  }
  hint=ui.Text(parent,CalibrationGuide.Instructions[Step],0,y+40,width,54,16);hint.enableWordWrapping=true;
  if(EdgeStep&&!ManualEdges){
   bool horizontal=Step==2;
   hint.text="眼睛正对三维区域"+(horizontal?"左沿／右沿":"上沿／下沿")+"时分别记录，保持前后距离。\n联合求解后回到区域中心复核；记录不会自动保存或应用预设。";hint.fontSize=14;hint.rectTransform.sizeDelta=new Vector2(width,44);
   float cell=(width-24)/4;
   captureTop=ui.Button(parent,horizontal?"记录左沿":"记录上沿",0,y+88,cell,()=>runtime.CaptureVerticalPoint(horizontal?"left":"top"));
   captureBottom=ui.Button(parent,horizontal?"记录右沿":"记录下沿",cell+8,y+88,cell,()=>runtime.CaptureVerticalPoint(horizontal?"right":"bottom"));
   solveVertical=ui.Button(parent,"联合求解",2*(cell+8),y+88,cell,runtime.SolveVerticalPoints,true);
   captureCenter=ui.Button(parent,"复核中心",3*(cell+8),y+88,cell,()=>runtime.CaptureVerticalPoint("center"));
   verticalFeedback=ui.Text(parent,"",0,y+132,width,28,12,CalibrationWidgets.Muted);
   AddTooltip(verticalFeedback.gameObject,"中心残差使用名义屏幕单位，不代表真实毫米精度。记录可从进阶 → 打开测量记录访问。");
  }else{
  parameters=ui.Rect(parent,"Parameters",0,y+104,width,40);BuildParameters();
  }
  previousStep=ui.Button(parent,"上一步",width-248,footer,112,()=>SelectStep(Step-1));
  nextStep=ui.Button(parent,Step==4?"保存":"下一步",width-124,footer,124,()=>{if(Step==4)SaveGuided();else SelectStep(Step+1);},true);
 }
 void SelectStep(int index){
  if(runtime.Capturing)runtime.CancelVerticalMeasurements();
  Service.Session.CommitEdit();Service.Session.Tool=(CalibrationTool)((int)CalibrationTool.OffsetX+Mathf.Clamp(index,0,4));
  Clear(guideContent);FillGuideControls();Refresh();
 }
 void BuildGuideTools(Transform parent,float y,float width){
  float cell=(width-8*4)/5;
  toolVisuals.Clear();
  for(int i=0;i<5;i++){
   int index=i;var tool=(CalibrationTool)((int)CalibrationTool.OffsetX+i);
   var b=ui.Button(parent,CalibrationGuide.Names[i],i*(cell+8),y,cell,()=>SelectStep(index),i==Step);b.name=CalibrationGuide.Names[i];
   var visual=b.GetComponent<CalibrationButtonVisual>();visual.label.fontSize=width<650?13:16;visual.label.alignment=TextAlignmentOptions.MidlineLeft;
   visual.label.rectTransform.anchoredPosition=new Vector2(42,0);visual.label.rectTransform.sizeDelta=new Vector2(cell-42,40);
   var circle=ui.Icon(b.transform,"Circle",2,4,32,Color.white);
   visual.StepCircle=circle;visual.StepNumber=ui.Text(circle.transform,(i+1).ToString(),0,0,32,32,16);visual.StepNumber.alignment=TextAlignmentOptions.Center;
   toolVisuals[tool]=visual;
  }
 }
 void RefreshGuide(){
  if(guideTitle)guideTitle.text=CalibrationGuide.Names[Step];
  if(hint&&!(EdgeStep&&!ManualEdges))hint.text=CalibrationGuide.Instructions[Step];
  if(previousStep)previousStep.SetInteractable(Step>0);
  if(nextStep){nextStep.name=Step==4?"保存":"下一步";nextStep.GetComponentInChildren<TMP_Text>().text=nextStep.name;nextStep.SetInteractable(Step==4?Service.CanSave:Service.CanEdit);}
  if(demoButton)demoButton.SetInteractable(EdgeStep||DemonstrationRequested!=null);
  RefreshVerticalControls();
 }
 void RefreshVerticalControls(){
  if(!verticalFeedback)return;
  verticalFeedback.text=runtime.VerticalMeasurementStatus;
  captureTop.SetInteractable(Service.CanEdit);captureBottom.SetInteractable(Service.CanEdit);
  solveVertical.SetInteractable(runtime.CanSolveVertical);captureCenter.SetInteractable(Service.CanEdit);
 }
 void UndoAdjustment(){Service.Session.CommitEdit();if(!Service.CanEdit||!Service.Session.CanUndo)return;Service.Session.Undo();Service.NotifyEdit();}
 bool HandleUndoShortcut(){
  bool editing=FindObjectsOfType<TMP_InputField>().Any(x=>x.isFocused);
  if(!CalibrationGuide.AcceptUndoShortcut(currentPage=="校准",editing,modal!=null||choicePopup!=null))return false;
  UndoAdjustment();return true;
 }
 void SaveGuided(){
  Service.Session.CommitEdit();if(!Service.CanSave){Service.Report("请先完成当前校准");return;}
  string name=Service.Session.Draft.name;
  if(string.IsNullOrWhiteSpace(name)){
   try{var names=Service.Store.List().Select(p=>p.name).ToArray();name="默认校准";int n=2;while(names.Contains(name))name="默认校准 "+n++;}
   catch(Exception e){Debug.LogWarning(e);operationError="无法读取校准文件，修改仍保留";Service.Report(operationError);return;}
  }
  operationError=null;
  if(Service.Save(name,false,true))CompleteCalibration();
  else {operationError=Service.Message;Refresh();}
 }
 void ShowAdvanced(){
  Modal("进阶",out var box);
  ui.Button(box,"选择预设",24,72,248,()=>{CloseModal();ShowPresets();});
  ui.Button(box,"新建校准",288,72,248,()=>{CloseModal();Leave(()=>{Service.NewPreset();Build();});});
  ui.Button(box,"另存为",24,128,248,()=>{CloseModal();Save(true,false);});
  var bind=ui.Button(box,Service.Session.Draft.schema_version==1?"迁移旧预设":"绑定当前设备",288,128,248,()=>{CloseModal();BindOrMigrate();});
  bind.SetInteractable(Service.Session.Draft.schema_version==1||Service.Fresh&&!Service.Busy);
  ui.Button(box,"恢复全部",24,208,248,()=>{CloseModal();Service.Session.RestoreAll();Service.NotifyEdit();});
  ui.Button(box,"取消修改",288,208,248,()=>{CloseModal();Service.Session.Discard();Service.NotifyEdit();});
  ui.Button(box,"打开测量记录",24,264,248,()=>runtime.OpenVerticalRecords());
 }
 Evo.UI.Button IconButton(Transform parent,string name,string icon,float x,float y,Action action){
  var button=ui.Button(parent,"",x,y,40,action);button.name=name;
  ui.Icon(button.transform,icon=="fullscreen"?"maximize":icon=="refresh"?"Refresh":icon=="reset"?"rotate-ccw":icon=="undo"?"undo-2":"Arrow Left",9,9,22,CalibrationWidgets.Ink);
  AddTooltip(button.gameObject,name);
  return button;
 }
 void AddTooltip(GameObject target,string text){var tip=target.AddComponent<CalibrationTooltip>();tip.Label=text;tip.Font=font;}
}
