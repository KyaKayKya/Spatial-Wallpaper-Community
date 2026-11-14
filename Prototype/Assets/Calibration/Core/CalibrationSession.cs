using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace SpatialTracking {
 // UI adapters edit a copy and submit it; no UI or engine types enter this domain.
 public sealed class CalibrationSession {
  CalibrationProfile draft,entry,gesture;
  readonly Stack<CalibrationProfile> undo=new Stack<CalibrationProfile>();
  readonly List<RawTrackingSample> references=new List<RawTrackingSample>();
  public CalibrationTool Tool{get;set;}
  public CalibrationProfile Draft{get{return draft.Copy();}}
  public bool Dirty{get{return JsonConvert.SerializeObject(draft)!=JsonConvert.SerializeObject(entry);}}
  public bool CanUndo{get{return undo.Count>0;}}
  public int ReferenceCount{get{return references.Count;}}
  public CalibrationSession(CalibrationProfile profile){if(profile==null||!profile.Valid())throw new ArgumentException("INVALID_PROFILE");draft=profile.Copy();entry=profile.Copy();Tool=profile.schema_version==2?CalibrationTool.OffsetX:CalibrationTool.View;}
  public void BeginEdit(){if(gesture==null)gesture=draft.Copy();}
  public bool Edit(CalibrationProfile value){if(value==null)return false;value=value.Copy();value.Rebuild();if(!value.Valid())return false;draft=value;return true;}
  public void CommitEdit(){if(gesture==null)return;if(JsonConvert.SerializeObject(gesture)!=JsonConvert.SerializeObject(draft))undo.Push(gesture);gesture=null;}
  public void Undo(){gesture=null;if(undo.Count>0)draft=undo.Pop();}
  public bool SolveVertical(double topV,double bottomV,ViewportMetrics view){
   if(draft.schema_version!=2||view==null||!TrackingCalibration.Finite(topV)||!TrackingCalibration.Finite(bottomV)||Math.Abs(topV)>1||Math.Abs(bottomV)>1||bottomV-topV<=.000001||!TrackingCalibration.Finite(view.Height)||view.Height<=0||!TrackingCalibration.Finite(view.CenterY))return false;
   var p=Draft;
   p.manual.scaleY=view.Height/(FiveParameterMapping.ReferenceUnit*(bottomV-topV));
   p.manual.offsetY=-topV-(view.CenterY+view.Height/2)/(FiveParameterMapping.ReferenceUnit*p.manual.scaleY);
   if(!p.Valid())return false;
   CommitEdit();BeginEdit();Edit(p);CommitEdit();return true;
  }
  public bool SolveHorizontal(double leftU,double rightU,ViewportMetrics view){
   if(draft.schema_version!=2||view==null||!TrackingCalibration.Finite(leftU)||!TrackingCalibration.Finite(rightU)||Math.Abs(leftU)>1||Math.Abs(rightU)>1||leftU-rightU<=.000001||!TrackingCalibration.Finite(view.Width)||view.Width<=0||!TrackingCalibration.Finite(view.CenterX))return false;
   var p=Draft;
   p.manual.scaleX=view.Width/(FiveParameterMapping.ReferenceUnit*(leftU-rightU));
   p.manual.offsetX=-leftU-(view.CenterX-view.Width/2)/(FiveParameterMapping.ReferenceUnit*p.manual.scaleX);
   if(!p.Valid())return false;
   CommitEdit();BeginEdit();Edit(p);CommitEdit();return true;
  }
  public bool AdjustFollow(CalibrationTool tool,double value,double center){
   if(tool!=CalibrationTool.ScaleX&&tool!=CalibrationTool.ScaleY)throw new ArgumentException("NOT_FOLLOW_TOOL");
   if(!TrackingCalibration.Finite(value)||value<.05||value>30||!TrackingCalibration.Finite(center))return false;
   var p=Draft;var m=p.manual;bool x=tool==CalibrationTool.ScaleX;
   double old=x?m.scaleX:m.scaleY;
   double offset=(x?m.offsetX:m.offsetY)+center/FiveParameterMapping.ReferenceUnit*(1/old-1/value);
   if(x){m.scaleX=value;m.offsetX=offset;}else{m.scaleY=value;m.offsetY=offset;}
   return Edit(p);
  }
  public void RestoreTool(double centerX,double centerY){
   if(Tool!=CalibrationTool.ScaleX&&Tool!=CalibrationTool.ScaleY){RestoreTool();return;}
   BeginEdit();if(!AdjustFollow(Tool,Tool==CalibrationTool.ScaleX?entry.manual.scaleX:entry.manual.scaleY,Tool==CalibrationTool.ScaleX?centerX:centerY))throw new InvalidOperationException("中心补偿超出有效范围，请重新校准位置");CommitEdit();
  }
  public void RestoreTool(){
   BeginEdit();var p=Draft;
   if(p.schema_version==2){
    switch(Tool){case CalibrationTool.OffsetX:p.manual.offsetX=entry.manual.offsetX;break;case CalibrationTool.OffsetY:p.manual.offsetY=entry.manual.offsetY;break;case CalibrationTool.ScaleX:p.manual.scaleX=entry.manual.scaleX;break;case CalibrationTool.ScaleY:p.manual.scaleY=entry.manual.scaleY;break;case CalibrationTool.ScaleZ:p.manual.scaleZ=entry.manual.scaleZ;break;}
    Edit(p);CommitEdit();return;
   }
   switch(Tool){case CalibrationTool.Level:p.world_pitch=entry.world_pitch;p.world_roll=entry.world_roll;break;
    case CalibrationTool.View:p.mapping.e0[0]=entry.mapping.e0[0];p.mapping.e0[1]=entry.mapping.e0[1];break;
    case CalibrationTool.Depth:p.mapping.e0[2]=entry.mapping.e0[2];break;
    case CalibrationTool.Follow:p.follow_pitch=entry.follow_pitch;p.follow_yaw=entry.follow_yaw;p.follow_roll=entry.follow_roll;p.amplitude=entry.amplitude;break;}
   Edit(p);CommitEdit();
  }
  public void RestoreAll(){BeginEdit();draft=entry.Copy();CommitEdit();}
  public void MarkSaved(CalibrationProfile saved){draft=saved.Copy();entry=saved.Copy();undo.Clear();gesture=null;}
  public void Discard(){draft=entry.Copy();undo.Clear();gesture=null;references.Clear();}
  public void ClearReferenceSamples(){references.Clear();}
  public bool SampleReference(RawTrackingSample sample,ulong now){
   if(sample==null||!sample.Fresh(now)||sample.Head==null){references.Clear();return false;}
   if(references.Count>0){var last=references[references.Count-1];if(last.Stream!=sample.Stream||sample.Captured-last.Captured>200000)references.Clear();else if(last.Frame==sample.Frame)return false;}
   var point=sample.Eye;
   if(references.Count>0){var first=references[0].Eye;double sum=0;for(int i=0;i<3;i++)sum+=(point[i]-first[i])*(point[i]-first[i]);if(sum>.0001)references.Clear();}
   references.Add(sample);
   if(references.Count<16||sample.Captured-references[0].Captured<800000)return false;
   var mean=new double[3];foreach(var item in references){var e=item.Eye;for(int i=0;i<3;i++)mean[i]+=e[i]/references.Count;}
   var p=Draft;p.BindReference(mean,sample.Head,sample.Descriptor);BeginEdit();Edit(p);CommitEdit();references.Clear();return true;
  }
 }
}
