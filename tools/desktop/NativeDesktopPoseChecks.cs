using System;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Desktop;
static class NativeDesktopPoseChecks {
 static int checks;
 static void Yes(bool value,string label){if(!value)throw new Exception(label);checks++;}
 static void No(Action action,string label){try{action();}catch(ArgumentException){checks++;return;}throw new Exception("accepted:"+label);}
 static JObject Descriptor()=>JObject.FromObject(new{stream_id=Guid.NewGuid().ToString(),tracker_instance_id=Guid.NewGuid().ToString(),camera_id="simulation",intent_revision="1",coordinate_frame="camera_upright_v1",length_unit="model_m",scale_source="canonical_face_prior",head_model_id="head",estimator_id="synthetic-eye-v1",intrinsics_id="K",geometry_id="G",intrinsics_source="prior",clock_domain=QpcClock.Domain,timestamp_origin="frame_arrival",actual_mode=new{mode_id=(object)null,source_width_px=640,source_height_px=480,width_px=640,height_px=480,fps=60,pixel_format="RGB8",rotation_deg=0,crop_rect=new[]{0,0,640,480},mirrored=false},optional_features=new[]{BinocularObservation.Feature}});
 static RawTrackingSample Raw(JObject descriptor,ulong frame,ulong time)=>new RawTrackingSample((string)descriptor["stream_id"],frame,time,new[]{0.0,0,.6},null,descriptor,new BinocularObservation(.1,-.2,100));
 static int Main(){
  var source=Descriptor();var profile=new CalibrationProfile();profile.manual.Bind(source,"left");
  var left=new DesktopDisplay{Id="left",X=-1920,Y=0,Width=1920,Height=1080,RefreshNumerator=60,RefreshDenominator=1};
  var pose=new NativeDesktopPose(profile,left,true);var initial=pose.Advance(null,1000000,.016,false);
  Yes((string)initial["display_id"]=="left"&&Math.Abs((double)initial["width"]-.53)<1e-9&&Math.Abs((double)initial["height"]-.53*1080/1920)<1e-9,"physical target geometry ignores negative desktop origin");
  Yes(!(bool)initial["fresh"]&&Math.Abs((double)initial["eye"][2]+.67)<1e-9,"initial reference eye matches original calibration");
  Yes((uint?)initial["presentation"]?["numerator"]==60&&(uint?)initial["presentation"]?["denominator"]==1,"target rational refresh carried without truncation");
  ulong now=1000000;JObject live=null;for(ulong i=1;i<=80;i++){now+=16667;live=pose.Advance(Raw(source,i,now),now,1.0/60,false);}
  Yes((bool)live["fresh"]&&Math.Abs((double)live["eye"][0]+.0438)<1e-6&&Math.Abs((double)live["eye"][1]-.0876)<1e-6&&Math.Abs((double)live["eye"][2]+.67)<1e-6,"original unmirrored five-parameter mapping and smoothing retained");
  var before=live["eye"].DeepClone();var lost=pose.Advance(null,now+300000,.05,true);Yes(!(bool)lost["fresh"]&&(bool)lost["paused"]&&JToken.DeepEquals(before,lost["eye"]),"loss holds original eye while revoking freshness");
  profile.manual.scaleX=20;left.Width=1;var stable=pose.Advance(Raw(source,81,now+16667),now+16667,.016,false);Yes(Math.Abs((double)stable["width"]-.53)<1e-9&&Math.Abs((double)stable["eye"][0]+.0438)<1e-6,"caller mutation cannot change applied profile or viewport");
  var other=new CalibrationProfile();other.manual.Bind(source,"right");other.manual.scaleX=8;other.manual.display_width=.7;var right=new NativeDesktopPose(other,new DesktopDisplay{Id="right",X=0,Y=-1024,Width=1280,Height=1024},false);
  JObject second=null;for(ulong i=1;i<=80;i++){now+=16667;second=right.Advance(Raw(source,i+100,now),now,1.0/60,false);}
  Yes(Math.Abs((double)second["eye"][0]+.08)<1e-6&&Math.Abs((double)second["height"]-.56)<1e-9&&!(bool)second["keep_height"],"each target keeps its own calibration and framing");
  var changed=(JObject)source.DeepClone();changed["geometry_id"]="other";Yes(!(bool)right.Advance(Raw(changed,200,now),now,.016,false)["fresh"],"source mismatch cannot authorize wrong calibration");
  No(()=>new NativeDesktopPose(other,new DesktopDisplay{Id="left",Width=1920,Height=1080},true),"wrong target profile rejected");
  No(()=>new NativeDesktopPose(other,new DesktopDisplay{Id="right",Width=0,Height=1080},true),"invalid target dimensions rejected");
  var invalid=other.Copy();invalid.schema_version=1;invalid.manual=null;No(()=>new NativeDesktopPose(invalid,left,true),"old schema with null manual is rejected without null reference");
  pose.UpdateRefresh(new DesktopDisplay{Id="left",X=-1920,Y=0,Width=1920,Height=1080,RefreshNumerator=120000,RefreshDenominator=1001});
  var refreshed=pose.Advance(null,now+300000,.016,false);Yes((uint?)refreshed["presentation"]?["numerator"]==120000&&(uint?)refreshed["presentation"]?["denominator"]==1001&&JToken.DeepEquals(stable["eye"],refreshed["eye"]),"refresh-only change preserves original held eye");
  No(()=>pose.UpdateRefresh(new DesktopDisplay{Id="right",X=-1920,Width=1920,Height=1080}),"another target cannot change refresh policy");
  Console.WriteLine("native desktop pose checks passed: "+checks);return 0;
 }
}
