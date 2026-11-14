using System;
using System.IO;
using System.Reflection;
using SpatialTracking;
using Newtonsoft.Json.Linq;

public static class CalibrationChecks {
 static int count;
 static void Check(bool value,string name){if(!value)throw new Exception(name);count++;}
 public static int Main(){
  try {
   Check(typeof(CalibrationProfile).Assembly.GetType("SpatialTracking.FiveParameterMapping")!=null,"Missing five-parameter mapping");
   var assembly=typeof(TrackingCalibration).Assembly;
   var session=assembly.GetType("SpatialTracking.CalibrationSession");
   Check(session!=null,"Missing reusable calibration session");
   Run();ManualChecks();StabilityChecks();GuideChecks();CenterChecks();PresetCompatibilityChecks();VerticalFitChecks();HorizontalFitChecks();
   Console.WriteLine("PASS "+count+" calibration checks");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}
 }
 static void HorizontalFitChecks(){
  var solve=typeof(CalibrationSession).GetMethod("SolveHorizontal");Check(solve!=null,"Missing two-edge horizontal fit");
  foreach(double c in new[]{0.0,.04,-.05})foreach(double w in new[]{.16,.4})foreach(double tilt in new[]{0.0,10.0}){
   Func<double,double> observed=x=>{double t=tilt*Math.PI/180,dx=.09-x;return 2*500.0/640*(dx*Math.Cos(t)-.65*Math.Sin(t))/(dx*Math.Sin(t)+.65*Math.Cos(t));};
   double left=observed(c-w/2),right=observed(c+w/2);var view=new ViewportMetrics(w,.22,c,-.03);
   var p=new CalibrationProfile();p.manual.offsetX=.035;p.manual.scaleX=.862;p.manual.offsetY=.1;p.manual.scaleZ=7;
   var s=new CalibrationSession(p);
   Check((bool)solve.Invoke(s,new object[]{left,right,view}),"Physical horizontal fit accepted");var m=s.Draft.manual;
   Check(Math.Abs(m.Map(new BinocularObservation(left,0,100))[0]-c+w/2)<1e-10,"Physical left maps to aperture left");
   Check(Math.Abs(m.Map(new BinocularObservation(right,0,100))[0]-c-w/2)<1e-10,"Physical right maps to aperture right");
   Check(m.offsetY==.1&&m.scaleY==p.manual.scaleY&&m.scaleZ==7,"Fit changes horizontal parameters only");
   double error=m.Map(new BinocularObservation(observed(c),0,100))[0]-c;
   Check(tilt==0?Math.Abs(error)<1e-10:Math.Abs(error)>1e-5,"Horizontal tilt center residual remains measurable");
   s.Undo();Check(s.Draft.manual.offsetX==.035&&s.Draft.manual.scaleX==.862&&!s.CanUndo,"Horizontal pair undoes atomically");
  }
  foreach(var pair in new[]{new[]{.2,.2},new[]{.2,.3},new[]{double.NaN,.2},new[]{1.1,.2},new[]{.100001,.1}}){
   var s=new CalibrationSession(new CalibrationProfile());Check(!(bool)solve.Invoke(s,new object[]{pair[0],pair[1],new ViewportMetrics(.4,.22,.04,0)})&&!s.Dirty&&!s.CanUndo,"Invalid horizontal fit rejected atomically");
  }
 }
 static void VerticalFitChecks(){
  var solve=typeof(CalibrationSession).GetMethod("SolveVertical");Check(solve!=null,"Missing two-edge vertical fit");
  foreach(double c in new[]{0.0,.04,-.05})foreach(double h in new[]{.08,.22})foreach(double tilt in new[]{0.0,10.0}){
   // Independent camera, fixed physical eye positions; never invert the mapping to generate inputs.
   Func<double,double> observed=y=>{double t=tilt*Math.PI/180,dy=.17-y;return 2*450.0/480*(dy*Math.Cos(t)-.65*Math.Sin(t))/(dy*Math.Sin(t)+.65*Math.Cos(t));};
   double top=observed(c+h/2),bottom=observed(c-h/2);var view=new ViewportMetrics(.4,h,.02,c);
   var p=new CalibrationProfile();p.manual.offsetY=.357;p.manual.scaleY=.862;p.manual.offsetX=.1;p.manual.scaleZ=7;
   var s=new CalibrationSession(p);
   Check((bool)solve.Invoke(s,new object[]{top,bottom,view}),"Physical edge fit accepted");
   var m=s.Draft.manual;
   Check(Math.Abs(m.Map(new BinocularObservation(0,top,100))[1]-c-h/2)<1e-10,"Independent physical top maps to aperture top");
   Check(Math.Abs(m.Map(new BinocularObservation(0,bottom,100))[1]-c+h/2)<1e-10,"Independent physical bottom maps to aperture bottom");
   Check(m.offsetX==.1&&m.scaleX==p.manual.scaleX&&m.scaleZ==7,"Fit changes vertical parameters only");
   double centerError=m.Map(new BinocularObservation(0,observed(c),100))[1]-c;
   Check(tilt==0?Math.Abs(centerError)<1e-10:Math.Abs(centerError)>1e-5,"Tilted camera residual must remain measurable, not silently zeroed");
   s.Undo();Check(s.Draft.manual.offsetY==.357&&s.Draft.manual.scaleY==.862&&!s.CanUndo,"Two fitted parameters undo in one operation");
  }
  foreach(var pair in new[]{new[]{.2,.2},new[]{.3,.2},new[]{double.NaN,.2},new[]{-1.1,.2},new[]{.1,.100001}}){
   var s=new CalibrationSession(new CalibrationProfile());
   Check(!(bool)solve.Invoke(s,new object[]{pair[0],pair[1],new ViewportMetrics(.4,.22,0,.04)})&&!s.Dirty&&!s.CanUndo,"Invalid edge fit rejected atomically");
  }
 }
 static void PresetCompatibilityChecks(){
  var method=typeof(CalibrationProfile).GetMethod("CanUse");Check(method!=null,"Missing live preset compatibility policy");
  var source=JObject.FromObject(new {camera_id="camera-a",head_model_id="head",estimator_id="estimator",intrinsics_id="intrinsics",geometry_id="geometry",actual_mode=new {mode_id="mode-a",width_px=640,height_px=480,source_width_px=640,source_height_px=480,mirrored=false,rotation_deg=0},optional_features=new[]{BinocularObservation.Feature}});
  var p=new CalibrationProfile();p.manual.Bind(source,"screen-a");
  Func<string,string,string,bool> use=(display,camera,mode)=>(bool)method.Invoke(p,new object[]{display,camera,mode,source});
  Check(use("screen-a","camera-a","mode-a"),"Current compatible preset accepted");
  Check(!use("screen-b","camera-a","mode-a"),"Other screen rejected");
  Check(!use("screen-a","camera-b","mode-a"),"Changed camera rejects stale descriptor preset");
  Check(!use("screen-a","camera-a","mode-b"),"Changed capture mode rejects stale descriptor preset");
 }
 static void CenterChecks(){
  var adjust=typeof(CalibrationSession).GetMethod("AdjustFollow");
  Check(adjust!=null,"Follow editing must preserve the calibrated viewport center");
  foreach(double center in new[]{0.0,.047,-.04})foreach(var tool in new[]{CalibrationTool.ScaleX,CalibrationTool.ScaleY}){
   var p=new CalibrationProfile();p.manual.offsetX=.035;p.manual.offsetY=.357;
   bool x=tool==CalibrationTool.ScaleX;double old=x?p.manual.scaleX:p.manual.scaleY,offset=x?p.manual.offsetX:p.manual.offsetY;
   double neutral=-offset-center/(old*.1);
   var s=new CalibrationSession(p);s.BeginEdit();
   Check((bool)adjust.Invoke(s,new object[]{tool,.862,center}),"Compensated edit accepted");s.CommitEdit();
   var m=s.Draft.manual;double[] eye=m.Map(new BinocularObservation(x?neutral:0,x?0:neutral,100));
   Check(Math.Abs(eye[x?0:1]-center)<1e-10,"Changing gain must not move neutral eye");
   double half=.025,delta=half/(.862*.1);
   foreach(double side in new[]{-1.0,1.0}){
    eye=m.Map(new BinocularObservation(x?neutral-side*delta:0,x?0:neutral-side*delta,100));
    Check(Math.Abs(eye[x?0:1]-center-side*half)<1e-10,"Both viewport boundaries remain symmetric");
   }
   s.Undo();Check(s.Draft.manual.scaleY==p.manual.scaleY&&s.Draft.manual.offsetY==p.manual.offsetY&&!s.CanUndo,"Gain and offset undo together");
   s.BeginEdit();adjust.Invoke(s,new object[]{tool,.862,center});s.CommitEdit();s.Tool=tool;s.RestoreTool(x?center:0,x?0:center);
   var restored=s.Draft.manual;Check(Math.Abs((x?restored.scaleX:restored.scaleY)-old)<1e-10&&Math.Abs((x?restored.offsetX:restored.offsetY)-offset)<1e-10,"Restore follow preserves original center");
   s.Undo();Check(Math.Abs((x?s.Draft.manual.scaleX:s.Draft.manual.scaleY)-.862)<1e-10,"Compensated restore is undoable");
  }
  var invalid=new CalibrationSession(new CalibrationProfile());
  Check(!(bool)adjust.Invoke(invalid,new object[]{CalibrationTool.ScaleY,.05,1.0})&&!invalid.Dirty,"Out-of-range compensation rejected without partial edits");
 }
 static void GuideChecks(){
  var guide=typeof(CalibrationProfile).Assembly.GetType("SpatialTracking.CalibrationGuide");
  Check(guide!=null,"Missing guided calibration policy");
  var eligible=guide.GetMethod("CanInitialize");
  Check((bool)eligible.Invoke(null,new object[]{new CalibrationProfile()}),"First draft initializes without preset setup");
  Check(!(bool)eligible.Invoke(null,new object[]{new CalibrationProfile{revision=1}}),"Saved draft must not silently rebind");
  Check(!(bool)eligible.Invoke(null,new object[]{new CalibrationProfile{schema_version=1}}),"Legacy requires explicit migration");
  var shortcut=guide.GetMethod("AcceptUndoShortcut");
  Check((bool)shortcut.Invoke(null,new object[]{true,false,false}),"Calibration shortcut accepted");
  Check(!(bool)shortcut.Invoke(null,new object[]{true,true,false}),"Text editing retains its undo");
  Check(!(bool)shortcut.Invoke(null,new object[]{true,false,true}),"Modal blocks calibration undo");
  Check(!(bool)shortcut.Invoke(null,new object[]{false,false,false}),"Other pages cannot undo calibration");
 }
 static void StabilityChecks(){
  var previewData=new JObject{["stream_id"]=Guid.NewGuid().ToString(),["frame_id"]="1",["captured_at_us"]="1000",["width"]=320,["height"]=240,["mirrored"]=true,["face_found"]=false,["inference_ms"]=2.5,["ipd_px"]=null,["jpeg"]=Convert.ToBase64String(new byte[]{255,216,255,192,0,8,8,0,240,1,64,1,255,217})};
  Check(new CameraPreviewFrame(previewData).Width==320,"preview validates JPEG dimensions before decoding");
  previewData["width"]=319;bool mismatch=false;
  try{new CameraPreviewFrame(previewData);}catch(InvalidDataException){mismatch=true;}Check(mismatch,"preview rejects mismatched encoded dimensions");
  previewData["width"]=641;bool oversized=false;
  try{new CameraPreviewFrame(previewData);}catch(InvalidDataException){oversized=true;}Check(oversized,"preview rejects oversized image metadata");
  var f=new BinocularSmoother();ulong t=1000000;
  f.Update("a",t,new BinocularObservation(0,0,100));
  double noise=0;
  for(int i=1;i<=120;i++){t+=33333;var v=f.Update("a",t,new BinocularObservation(i%2==0?.004:-.004,0,i%2==0?102:98));if(i>30)noise+=Math.Abs(v.IpdPixels-100);}
  Check(noise/90<.3,"stationary IPD jitter suppressed");
  var before=f.Update("a",t,new BinocularObservation(1,1,200));
  Check(Math.Abs(before.U)<.004,"duplicate capture cannot advance filter");
  for(int i=0;i<5;i++){t+=33333;f.Update("a",t,new BinocularObservation(.3,0,120));}
  var moved=f.Update("a",t,new BinocularObservation(.3,0,120));
  Console.WriteLine("Stability: mean IPD jitter="+(noise/90)+"px; 167ms step U="+moved.U+" IPD="+moved.IpdPixels);
  Check(moved.U>.28&&moved.IpdPixels>118,"large movement settles within 167ms");
  var recovered=f.Update("a",t+5000000,new BinocularObservation(.3,0,122));
  Check(recovered.IpdPixels<121,"dropout recovery cannot bypass observation smoothing");
  Check(f.Update("b",t+33333,new BinocularObservation(0,0,80)).IpdPixels==80,"new stream resets observations");
  var lowRate=new BinocularSmoother();var highRate=new BinocularSmoother();
  lowRate.Update("c",1000000,new BinocularObservation(0,0,100));highRate.Update("c",1000000,new BinocularObservation(0,0,100));
  BinocularObservation low=null,high=null;
  for(int i=1;i<=30;i++)low=lowRate.Update("c",(ulong)(1000000+i*33334),new BinocularObservation(.004,0,101));
  for(int i=1;i<=60;i++)high=highRate.Update("c",(ulong)(1000000+i*16667),new BinocularObservation(.004,0,101));
  Check(Math.Abs(low.IpdPixels-high.IpdPixels)<1e-8&&Math.Abs(low.U-high.U)<1e-8,"quiet filtering independent of capture rate");
  var eye=new EyeSmoother(new[]{0.0,0,-.67},true);
  for(int i=0;i<60;i++)eye.Advance(new TrackingSnapshot(1,true,"a",(ulong)i,t,eye:new[]{.1,0,-.9}),t,.016);
  double held=eye.Z;
  for(int i=1;i<300;i++)eye.Advance(null,t+(ulong)i*16000,.016);
  Check(eye.Z==held&&!eye.Fresh,"manual tracking loss holds view without reporting fresh");
 }
 static void Run(){
  var p=new CalibrationProfile{schema_version=1};var s=new CalibrationSession(p);
  var copy=s.Draft;copy.mapping.e0[0]=.2;Check(s.Draft.mapping.e0[0]==0,"draft detached");
  s.BeginEdit();Check(s.Edit(copy),"valid view edit");copy=s.Draft;copy.mapping.e0[0]=.3;s.Edit(copy);s.CommitEdit();s.Undo();Check(s.Draft.mapping.e0[0]==0&&!s.CanUndo,"one gesture one undo");
  copy=s.Draft;copy.mapping.e0[2]=double.NaN;Check(!s.Edit(copy)&&s.Draft.mapping.e0[2]==-.6,"reject NaN");
  copy=s.Draft;copy.amplitude=0;Check(!s.Edit(copy),"reject zero scale");
  copy=s.Draft;copy.world_pitch=9;Check(!s.Edit(copy),"constrain level");
  copy=s.Draft;copy.mapping.e0[0]=.1;copy.world_pitch=3;s.BeginEdit();s.Edit(copy);s.CommitEdit();s.Tool=CalibrationTool.Level;s.RestoreTool();Check(s.Draft.world_pitch==0&&s.Draft.mapping.e0[0]==.1,"restore one tool");s.Undo();Check(s.Draft.world_pitch==3,"undo restore");
  var d=new JObject();foreach(var key in new[]{"camera_id","head_model_id","estimator_id","intrinsics_id","geometry_id"})d[key]=key;
  d["coordinate_frame"]="camera_upright_v1";d["length_unit"]="model_m";d["scale_source"]="canonical_face_prior";
  for(int i=0;i<18;i++){ulong t=(ulong)(1000000+i*50000);s.SampleReference(new RawTrackingSample("stream",(ulong)i,t,new[]{0.0,.03,.635},new[]{0.0,0.0,0.0,1.0},d),t);}
  Check(s.Draft.reference_ready,"stable reference initializes");Check(s.Draft.mapping.Matches(d),"source matches");
  var at=s.Draft.mapping.Map(new[]{0.0,.03,.635});Check(Math.Abs(at[0]-.1)<1e-8&&Math.Abs(at[2]+.6)<1e-8,"reference maps to e0");
  var moved=s.Draft.mapping.Map(new[]{.1,.03,.635});Check(moved[0]<at[0],"camera to screen horizontal sign");
  var q=s.Draft.mapping.q0;s.Tool=CalibrationTool.Depth;copy=s.Draft;copy.mapping.e0[2]=-.8;s.Edit(copy);Check(s.Draft.mapping.q0[2]==q[2],"editing does not recenter");
  var original=s.Draft;var discard=new CalibrationSession(original);copy=discard.Draft;copy.world_pitch=5;discard.BeginEdit();discard.Edit(copy);discard.CommitEdit();discard.Discard();Check(!discard.Dirty&&!discard.CanUndo&&discard.Draft.world_pitch==original.world_pitch,"discard restores selected preset, not applied preset");
  var directory=Path.Combine(Path.GetTempPath(),"spatial-calibration-check-"+Guid.NewGuid().ToString("N"));
  try{var store=new CalibrationStore(directory);var saved=store.Save(s.Draft,"桌面",false);Check(saved.revision==1&&store.List().Length==1,"save and reload");
   var second=store.Save(saved,"站立",true);Check(second.id!=saved.id&&store.List().Length==2,"save as isolated");
   bool duplicate=false;try{store.Save(saved,"站立",true);}catch(InvalidDataException){duplicate=true;}Check(duplicate,"reject duplicate name");
   store.Save(saved,"桌面",false);bool conflict=false;try{store.Save(saved,"桌面",false);}catch(InvalidDataException){conflict=true;}Check(conflict,"stale revision rejected");
  }finally{Directory.Delete(directory,true);}
  var pipe=new TrackingPipeline();string stream=Guid.NewGuid().ToString(),instance=Guid.NewGuid().ToString();
  var descriptor=JObject.FromObject(new {stream_id=stream,tracker_instance_id=instance,camera_id="simulation",intent_revision="1",coordinate_frame="camera_upright_v1",length_unit="model_m",scale_source="canonical_face_prior",head_model_id="head",estimator_id="synthetic",intrinsics_id="K",geometry_id="G",intrinsics_source="prior",clock_domain=QpcClock.Domain,timestamp_origin="frame_arrival",actual_mode=new {mode_id=(object)null,source_width_px=640,source_height_px=480,width_px=640,height_px=480,fps=60,pixel_format="RGB8",rotation_deg=0,crop_rect=new[]{0,0,640,480},mirrored=false},optional_features=new string[0]});
  Check(pipe.SetStream(descriptor,instance),"raw stream needs no prior calibration");
  var bad=(JObject)descriptor.DeepClone();bad["length_unit"]="mm";Check(!new TrackingPipeline().SetStream(bad,instance),"raw host rejects unsupported units");
  var sample=JObject.FromObject(new {stream_id=stream,frame_id="1",captured_at_us="1000000",processed_at_us="1001000",tracking_state="tracking",validity=new{head_pose=true,eye_position=true},head_pose=new{position=new[]{0.0,0,.6},orientation_xyzw=new[]{0.0,0,0,1}},eye_position=new[]{.1,.03,.635},quality=new{flags=new string[0],reprojection_error_px=(object)null},extensions=new{}});
  Check(pipe.Offer(sample,1002000,1003000),"raw valid sample accepted");
  var raw=pipe.RawLatest;Check(raw.Eye[0]==.1,"raw position is not mapped");var array=raw.Eye;array[0]=9;Check(raw.Eye[0]==.1,"raw arrays immutable");
  var metadata=raw.Descriptor;metadata["camera_id"]="mutated";Check((string)raw.Descriptor["camera_id"]=="simulation","descriptor immutable");
  Check(!raw.Fresh(1300000),"old raw sample cannot refresh tracking");
  Check(!pipe.Offer(sample,1002000,1003000),"raw duplicate rejected");pipe.Disconnect();Check(pipe.RawLatest==null,"disconnect clears raw sample");
  descriptor["optional_features"]=new JArray(BinocularObservation.Feature);Check(!new TrackingPipeline().SetStream(descriptor,instance),"unnegotiated binocular stream must be rejected");var binocularPipe=new TrackingPipeline();binocularPipe.SetNegotiatedFeatures(new JArray(BinocularObservation.Feature));
  var silent=(JObject)descriptor.DeepClone();silent["optional_features"]=new JArray();Check(!binocularPipe.SetStream(silent,instance),"selected binocular feature must not silently disappear");
  Check(binocularPipe.SetStream(descriptor,instance),"negotiated binocular stream accepted");
  sample["extensions"]=new JObject{[BinocularObservation.Feature]=new JObject{["center_uv"]=new JArray(.2,-.1),["ipd_px"]=80}};
  sample["validity"]["head_pose"]=false;sample["validity"]["eye_position"]=false;sample["head_pose"]=null;sample["eye_position"]=null;
  Check(binocularPipe.Offer(sample,1002000,1003000)&&binocularPipe.RawLatest.Eye==null&&binocularPipe.RawLatest.Binocular.IpdPixels==80,"binocular raw input independent of PnP");
  sample["frame_id"]="2";sample["captured_at_us"]="1001000";sample["extensions"][BinocularObservation.Feature]["ipd_px"]=0;
  Check(!binocularPipe.Offer(sample,1002000,1003000)&&binocularPipe.RawLatest.Frame==1,"invalid IPD cannot refresh sample");
  sample["extensions"][BinocularObservation.Feature]=null;sample["tracking_state"]="lost";
  Check(binocularPipe.Offer(sample,1002000,1003000)&&binocularPipe.RawLatest==null,"lost observation clears raw slot");
  binocularPipe.Disconnect();Check(!binocularPipe.SetStream(descriptor,instance),"reconnection must negotiate features again");
  var filter=new EyeSmoother(new[]{0.0,0,-.6});filter.SetReference(new[]{.1,.1,-.8});for(int i=0;i<120;i++)filter.Advance(null,(ulong)(2000000+i*16000),.016);Check(Math.Abs(filter.Z+.8)<.001,"loss uses updated reference");
 }
 static void ManualChecks(){
  var m=new FiveParameterMapping{offsetX=.2,offsetY=-.1,scaleX=2,scaleY=3,scaleZ=4};
  var eye=m.Map(new BinocularObservation(.3,.4,80));
  Check(Math.Abs(eye[0]+.1)<1e-10&&Math.Abs(eye[1]+.09)<1e-10&&Math.Abs(eye[2]+.5)<1e-10,"offset before scale, image down and screen inward axes");
  var far=m.Map(new BinocularObservation(.3,.4,40));Check(Math.Abs(far[2]-eye[2]*2)<1e-10&&far[0]==eye[0],"inverse IPD absolute distance with independent axes");
  Check(!new BinocularObservation(0,0,0).Valid()&&!new BinocularObservation(double.NaN,0,80).Valid(),"zero IPD and NaN rejected");
  m.scaleX=0;Check(!m.Valid(),"positive X scale");m.scaleX=2;m.scaleY=double.PositiveInfinity;Check(!m.Valid(),"finite Y scale");
  Check(typeof(CalibrationSession).Assembly.GetType("SpatialTracking.ViewportMetrics")!=null,"Missing physical-pixel viewport transform");
  ViewportMetrics full,small,dpi;
  Check(ViewportGeometry.TryMap(-1920,0,1920,1080,-1920,0,1920,1080,1920,1080,0,0,1920,1080,.53,out full),"negative desktop monitor");
  Check(ViewportGeometry.TryMap(-1920,0,1920,1080,-1820,100,1200,800,1200,800,200,100,800,450,.53,out small),"sidebar and black bars excluded");
  Check(ViewportGeometry.TryMap(-1920,0,1920,1080,-1820,0,1200,900,800,600,200/1.5,100/1.5,800/1.5,450/1.5,.53,out dpi),"fractional DPI");
  Check(Math.Abs(small.Width-dpi.Width)<1e-8&&Math.Abs(small.Height-dpi.Height)<1e-8&&Math.Abs(small.CenterY-dpi.CenterY)<1e-8,"DPI render and physical coordinates agree");
  var displayEye=new[]{.07,-.04,-.8};var view=small.ToView(displayEye);var fullEye=full.ToView(displayEye);
  Check(Math.Abs(view[0]+small.CenterX-fullEye[0])<1e-10&&Math.Abs(view[1]+small.CenterY-fullEye[1])<1e-10&&view[2]==fullEye[2],"one display mapping across window resize/fullscreen, subtract once");
  Check(!ViewportGeometry.TryMap(0,0,1920,1080,-32000,-32000,0,0,1200,800,0,0,1000,600,.53,out dpi),"minimized client rejected");
  Check(!ViewportGeometry.TryMap(0,0,1920,1080,1500,100,1200,800,1200,800,0,0,1000,600,.53,out dpi),"cross-monitor preview rejected");
  var descriptor=JObject.FromObject(new{camera_id="camera",head_model_id="head",estimator_id="mediapipe",intrinsics_id="K",geometry_id="G",actual_mode=new{mode_id=(object)null,width_px=640,height_px=480,source_width_px=640,source_height_px=480,fps=30,pixel_format="RGB8",rotation_deg=0,mirrored=false,crop_rect=new[]{0,0,640,480}},optional_features=new[]{BinocularObservation.Feature}});
  var profile=new CalibrationProfile();profile.manual.Bind(descriptor,"monitor-id");
  Check(profile.manual.Matches(descriptor,"monitor-id")&&!profile.manual.Matches(descriptor,"other-monitor"),"display identity is independent of window");
  var changed=(JObject)descriptor.DeepClone();changed["actual_mode"]["width_px"]=1280;
  Check(!profile.manual.Matches(changed,"monitor-id"),"pixel IPD mode mismatch rejected");
  var s=new CalibrationSession(profile);var original=s.Draft;s.BeginEdit();var p=s.Draft;p.manual.offsetX=.25;s.Edit(p);p=s.Draft;p.manual.offsetX=.3;s.Edit(p);s.CommitEdit();s.Undo();Check(s.Draft.manual.offsetX==0&&!s.CanUndo,"manual gesture coalesced");
  p=s.Draft;p.manual.offsetX=.25;p.manual.scaleZ=8;s.BeginEdit();s.Edit(p);s.CommitEdit();s.Tool=CalibrationTool.OffsetX;s.RestoreTool();Check(s.Draft.manual.offsetX==0&&s.Draft.manual.scaleZ==8,"restore one of five parameters only");s.Undo();Check(s.Draft.manual.offsetX==.25,"restore is undoable");
  s.RestoreAll();Check(!s.Dirty&&s.Draft.manual.scaleZ==original.manual.scaleZ,"restore all to session entry");s.Undo();Check(s.Draft.manual.scaleZ==8,"restore all undoable");s.Discard();Check(!s.Dirty&&!s.CanUndo,"cancel clears manual draft");
  p=s.Draft;p.manual.scaleZ=0;Check(!s.Edit(p)&&s.Draft.manual.scaleZ>0,"invalid edit preserves valid draft");
  var directory=Path.Combine(Path.GetTempPath(),"spatial-five-check-"+Guid.NewGuid().ToString("N"));
  try{
   var store=new CalibrationStore(directory);var old=new CalibrationProfile{schema_version=1,reference_ready=true,world_pitch=3,world_roll=2};var legacy=store.Save(old,"旧预设",false);string file=Path.Combine(directory,legacy.id+".json");byte[] before=File.ReadAllBytes(file);
   var next=legacy.CreateManualDraft();Check(next.schema_version==2&&next.id!=legacy.id&&!next.reference_ready&&next.world_pitch==0&&next.world_roll==0,"explicit migration creates unbound manual draft with no leveling");
   next.manual.Bind(descriptor,"monitor-id");var saved=store.Save(next,"新预设",false);
   Check(saved.schema_version==2&&store.List().Length==2&&Convert.ToBase64String(before)==Convert.ToBase64String(File.ReadAllBytes(file)),"legacy bytes preserved, separate schema2 save/reload");
   var reload=Array.Find(store.List(),x=>x.id==saved.id);Check(reload.manual.Matches(descriptor,"monitor-id"),"source identity survives reload");
   var detached=saved.Copy();detached.manual.offsetX=.4;Check(saved.manual.offsetX!=detached.manual.offsetX,"applied copies detached");
   var malformed=saved.Copy();malformed.manual.source=new JObject();Check(!malformed.Valid(),"malformed persisted binding rejected");
   File.WriteAllText(Path.Combine(directory,"not-a-profile.json"),"{}");bool rejected=false;
   try{store.List();}catch(InvalidDataException){rejected=true;}Check(rejected,"missing schema must not synthesize a random preset");
  }finally{Directory.Delete(directory,true);}
 }
}
