using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using UnityEngine;

public sealed partial class CalibrationRuntime {
 JObject verticalTop,verticalBottom;
 string verticalContext,verticalMessage="依次记录两个边界，再联合求解；之后回到中心复核。";
 CalibrationSession verticalSession;
 bool HorizontalEdges=>Session.Tool==CalibrationTool.ScaleX;
 string EdgePair=>HorizontalEdges?"左右沿":"上下沿";
 int verticalGeneration;
 public string VerticalRecordPath{get;private set;}
 public string VerticalMeasurementStatus=>verticalMessage;
 public bool CanSolveVertical=>CanEdit&&verticalTop!=null&&verticalBottom!=null;
 string VerticalContext(){
  var raw=CurrentRaw;
  if(viewport==null||raw==null)return null;
  return new JObject{["display_id"]=DisplayId,["source"]=raw.Descriptor,["camera_id"]=CameraId,["camera_mode_id"]=CameraModeId,
   ["viewport"]=new JObject{["width"]=viewport.Width,["height"]=viewport.Height,["center_x"]=viewport.CenterX,["center_y"]=viewport.CenterY},
   ["axis"]=HorizontalEdges?"x":"y",["profile_id"]=Session.Draft.id,["profile_revision"]=Session.Draft.revision,["manual"]=JObject.FromObject(Session.Draft.manual)}.ToString(Formatting.None);
 }
 void UpdateVerticalContext(){
  string next=VerticalContext();
  if(next==verticalContext&&ReferenceEquals(verticalSession,Session))return;
  bool hadPoints=verticalTop!=null||verticalBottom!=null||Capturing;
  verticalGeneration++;verticalTop=verticalBottom=null;verticalContext=next;verticalSession=Session;
  if(hadPoints){verticalMessage="追踪、视口、设备或参数已变化，请重新记录"+EdgePair+"。";Changed?.Invoke();}
 }
 public void CancelVerticalMeasurements(){
  verticalGeneration++;verticalTop=verticalBottom=null;
  verticalMessage="依次记录"+EdgePair+"，再联合求解；之后回到中心复核。";
 }
 public void CaptureVerticalPoint(string point){
  if(point!="center"&&(HorizontalEdges?(point!="left"&&point!="right"):(point!="top"&&point!="bottom")))throw new ArgumentException("INVALID_EDGE_POINT");
  if(!CanEdit||ShowingExhibit)return;
  UpdateVerticalContext();StartCoroutine(CaptureVertical(point));
 }
 IEnumerator CaptureVertical(string point){
  if(point=="top"||point=="left")verticalTop=null;
  if(point=="bottom"||point=="right")verticalBottom=null;
  Capturing=true;
  try{
  int generation=verticalGeneration;
  bool horizontal=HorizontalEdges;
  string label=point=="top"?"上沿":point=="bottom"?"下沿":point=="left"?"左沿":point=="right"?"右沿":"中心";
  verticalMessage="保持眼睛正对三维区域"+label+"，保持前后距离，正在记录…";Changed?.Invoke();
  var samples=new List<RawTrackingSample>();float deadline=Time.realtimeSinceStartup+3;
  while(Time.realtimeSinceStartup<deadline){
   UpdateVerticalContext();
   if(generation!=verticalGeneration){Capturing=false;Changed?.Invoke();yield break;}
   var raw=CurrentRaw;
   if(!Fresh||!Compatible||!ViewportValid||raw?.Binocular==null||!raw.Fresh(QpcClock.Now)){
    Capturing=false;verticalMessage="测量中断：需要连续有效的双眼追踪，请重试。";Changed?.Invoke();yield break;
   }
   if(samples.Count==0||raw.Frame!=samples[samples.Count-1].Frame){samples.Add(raw);}
   if(samples.Count>=8&&raw.Captured-samples[0].Captured>=800000)break;
   yield return null;
  }
  Capturing=false;
  if(samples.Count<8||samples[samples.Count-1].Captured-samples[0].Captured<800000){verticalMessage="有效样本不足，请保持位置后重新记录。";Changed?.Invoke();yield break;}
  double u=0,v=0,ipd=0,min=double.PositiveInfinity,max=double.NegativeInfinity;
  var rows=new JArray();foreach(var raw in samples){
   var o=raw.Binocular;u+=o.U/samples.Count;v+=o.V/samples.Count;ipd+=o.IpdPixels/samples.Count;min=Math.Min(min,o.V);max=Math.Max(max,o.V);
   rows.Add(new JObject{["frame_id"]=raw.Frame.ToString(),["captured_at_us"]=raw.Captured.ToString(),["u"]=o.U,["v"]=o.V,["ipd_px"]=o.IpdPixels});
  }
  var record=new JObject{["event"]="measurement",["point"]=point,["count"]=samples.Count,["mean_u"]=u,["mean_v"]=v,["mean_ipd_px"]=ipd,["v_range"]=max-min,["samples"]=rows};
  if(point=="center"){
   string axis=horizontal?"x":"y";double center=horizontal?viewport.CenterX:viewport.CenterY;
   double mapped=Session.Draft.manual.Map(new BinocularObservation(u,v,ipd))[horizontal?0:1],residual=mapped-center;
   record["mapped_"+axis]=mapped;record["expected_"+axis]=center;record["residual_"+axis+"_nominal_m"]=residual;
   record["residual_"+axis+"_viewport_pixels"]=residual/(horizontal?viewport.Width:viewport.Height)*(horizontal?projection.GetComponent<Camera>().pixelWidth:projection.GetComponent<Camera>().pixelHeight);
   verticalMessage=string.Format(System.Globalization.CultureInfo.InvariantCulture,"中心残差（{0}）{1:+0.00;-0.00;0} 名义毫米（非实测）；原始记录已保存。",horizontal?"横向":"纵向",residual*1000);
  }
  // Write evidence before accepting a point; a failed write must remain visible.
  if(!WriteVerticalRecord(record)){Changed?.Invoke();yield break;}
  if(point=="top"||point=="left")verticalTop=record;
  if(point=="bottom"||point=="right")verticalBottom=record;
  if(point!="center")verticalMessage=label+"已记录（"+samples.Count+" 帧）；"+(verticalTop!=null&&verticalBottom!=null?"可以联合求解。":"请记录另一沿。");
  Changed?.Invoke();
  }finally{Capturing=false;Changed?.Invoke();}
 }
 public void SolveVerticalPoints(){
  UpdateVerticalContext();if(!CanSolveVertical)return;
  var before=Session.Draft;var candidate=new CalibrationSession(before);
  bool horizontal=HorizontalEdges;string coordinate=horizontal?"mean_u":"mean_v";
  double first=(double)verticalTop[coordinate],second=(double)verticalBottom[coordinate];
  if(!(horizontal?candidate.SolveHorizontal(first,second,viewport):candidate.SolveVertical(first,second,viewport))){
   verticalMessage="求解失败：请确认记录的是三维区域"+EdgePair+"，且采样间距足够；参数超限时请重新采样。";WriteVerticalRecord(new JObject{["event"]="solve_rejected",["reason"]=verticalMessage});Changed?.Invoke();return;
  }
  var after=candidate.Draft;
  var record=new JObject{["event"]="solve",[horizontal?"left":"top"]=verticalTop.DeepClone(),[horizontal?"right":"bottom"]=verticalBottom.DeepClone(),["before"]=JObject.FromObject(before.manual),["after"]=JObject.FromObject(after.manual)};
  if(horizontal){record["expected_left_x"]=viewport.CenterX-viewport.Width/2;record["expected_right_x"]=viewport.CenterX+viewport.Width/2;}
  else{record["expected_top_y"]=viewport.CenterY+viewport.Height/2;record["expected_bottom_y"]=viewport.CenterY-viewport.Height/2;}
  if(!WriteVerticalRecord(record)){Changed?.Invoke();return;}
  if(!(horizontal?Session.SolveHorizontal(first,second,viewport):Session.SolveVertical(first,second,viewport)))throw new InvalidOperationException("EDGE_FIT_CHANGED_DURING_APPLY");
  verticalContext=VerticalContext();
  verticalMessage=EdgePair+"已联合求解（仅修改草稿，可撤销）；回到三维区域中心后点击复核中心。";Changed?.Invoke();
 }
 bool WriteVerticalRecord(JObject record){
  try{
   if(VerticalRecordPath==null){
    string folder=Path.Combine(Path.GetDirectoryName(settingsPath),"measurements");Directory.CreateDirectory(folder);
    VerticalRecordPath=Path.Combine(folder,"vertical-"+DateTime.UtcNow.ToString("yyyyMMdd-HHmmss")+"-"+Guid.NewGuid().ToString("N")+".jsonl");
    using(var file=new FileStream(VerticalRecordPath,FileMode.CreateNew,FileAccess.Write)){}
   }
   record["utc"]=DateTime.UtcNow.ToString("O");record["time_us"]=QpcClock.Now.ToString();record["build_guid"]=Application.buildGUID;
   record["context"]=JObject.Parse(verticalContext);record["axis"]=HorizontalEdges?"x":"y";record["units"]="nominal_m";
   File.AppendAllText(VerticalRecordPath,record.ToString(Formatting.None)+Environment.NewLine);return true;
  }catch(Exception e)when(e is IOException||e is UnauthorizedAccessException){
   Debug.LogError("Vertical calibration measurement write failed: "+e);verticalMessage="测量记录写入失败："+e.Message;return false;
  }
 }
 public void OpenVerticalRecords(){
  string folder=Path.Combine(Path.GetDirectoryName(settingsPath),"measurements");
  try{Directory.CreateDirectory(folder);System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(folder){UseShellExecute=true});}
  catch(Exception e)when(e is IOException||e is UnauthorizedAccessException||e is System.ComponentModel.Win32Exception){Debug.LogError(e);Report("无法打开测量目录："+e.Message);}
 }
}
