using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;

public static class CoreChecks {
 static int checks;
 static void Check(bool ok,string why){checks++;if(!ok)throw new Exception(why);}
 static JObject Sample(string stream,ulong frame,ulong capture,bool valid=true){
  return JObject.FromObject(new {stream_id=stream,frame_id=frame.ToString(),captured_at_us=capture.ToString(),
   processed_at_us=(capture+1000).ToString(),tracking_state=valid?"tracking":"lost",
   validity=new {head_pose=false,eye_position=valid},head_pose=(object)null,
   eye_position=valid?new double[]{-.1,.08,.735}:null,
   quality=new {flags=new string[0],reprojection_error_px=(object)null},extensions=new {}});
 }
 public static int Main(){
  try {
   var type=typeof(CoreChecks).Assembly.GetType("SpatialTracking.TrackingPipeline");
   Check(type!=null,"C# tracking pipeline is missing");
   Run();Console.WriteLine("PASS "+checks+" C# boundary/calibration/recovery checks");return 0;
  }catch(Exception e){Console.Error.WriteLine("FAIL "+e.Message);return 1;}
 }
 sealed class Fragmented:MemoryStream {public Fragmented(byte[] data):base(data){}public override int Read(byte[] b,int o,int n){return base.Read(b,o,Math.Min(1,n));}}
 static void Run(){
  // Later checks are dynamically bound, so the missing-feature baseline can execute.
  dynamic calibration=Activator.CreateInstance(typeof(CoreChecks).Assembly.GetType("SpatialTracking.TrackingCalibration"));
  calibration.camera_id="simulation";calibration.estimator_id="synthetic-eye-v1";
  calibration.head_model_id="head";calibration.intrinsics_id="K";calibration.geometry_id="G";
  dynamic pipe=Activator.CreateInstance(typeof(CoreChecks).Assembly.GetType("SpatialTracking.TrackingPipeline"),new object[]{calibration});
  calibration.view_center_m=new double[]{.05,-.02,0};
  double[] view=calibration.ToView(.1,.05,-.7);
  Check(Math.Abs(view[0]-.05)<1e-9&&Math.Abs(view[1]-.07)<1e-9&&Math.Abs(view[2]+.7)<1e-9,"screen-to-image rectangle offset wrong");
  calibration.view_center_m=new double[]{1e100,0,0};
  Check(!calibration.Valid(),"unusable view rectangle center accepted");
  calibration.view_center_m=new double[]{0,0,0};
  string stream=Guid.NewGuid().ToString(),instance=Guid.NewGuid().ToString();
  var descriptor=JObject.FromObject(new {stream_id=stream,tracker_instance_id=instance,camera_id="simulation",
   intent_revision="1",coordinate_frame="camera_upright_v1",length_unit="model_m",
   scale_source="canonical_face_prior",head_model_id="head",estimator_id="synthetic-eye-v1",
   intrinsics_id="K",geometry_id="G",intrinsics_source="prior",clock_domain=SpatialTracking.QpcClock.Domain,
   timestamp_origin="frame_arrival",actual_mode=new {mode_id=(object)null,source_width_px=640,
   source_height_px=480,width_px=640,height_px=480,fps=60,pixel_format="RGB8",rotation_deg=0,
   crop_rect=new[]{0,0,640,480},mirrored=false},optional_features=new string[0]});
  var broken=(JObject)descriptor.DeepClone();((JObject)broken["actual_mode"]).Remove("crop_rect");
  Check(!pipe.SetStream(broken,instance),"incomplete geometry descriptor accepted");
  Check(pipe.SetStream(descriptor,instance),"compatible descriptor rejected");
  Check(pipe.Offer(Sample(stream,1,1000000),1002000UL,1003000UL),"valid eye rejected");
  dynamic snap=pipe.Latest;
  Check(Math.Abs((double)snap.X-.1)<1e-9&&Math.Abs((double)snap.Y-.05)<1e-9&&Math.Abs((double)snap.Z+.7)<1e-9,"axis/unit/calibration mapping wrong");
  Check(!pipe.Offer(Sample(stream,1,1000000),1002000UL,1004000UL),"duplicate refresh");
  Check(!pipe.Offer(Sample(stream,2,999000),1002000UL,1004000UL),"capture time reversal");
  Check(!pipe.Offer(Sample(Guid.NewGuid().ToString(),3,1002000),1003000UL,1004000UL),"old stream accepted");
  Check(!pipe.Offer(Sample(stream,4,500000),1003000UL,1004000UL),"stale sample accepted");
  Check(!pipe.Offer(Sample(stream,5,1500000),1501000UL,1004000UL),"future sample accepted");
  Check(pipe.Offer(Sample(stream,6,1010000,false),1012000UL,1013000UL),"loss not consumed");
  Check(!(bool)pipe.Latest.Valid,"loss reused previous valid eye");
  Check(pipe.SetStream(descriptor,instance),"same descriptor refresh");
  Check(!pipe.Offer(Sample(stream,5,1011000),1012000UL,1013000UL),"snapshot reset frame ordering");
  descriptor["stream_id"]=Guid.NewGuid().ToString();
  Check(pipe.SetStream(descriptor,instance),"ordinary reconnect discarded calibration");
  Check(pipe.Offer(Sample((string)descriptor["stream_id"],1,1020000),1021000UL,1022000UL),"new stream did not reset order");
  descriptor["intrinsics_id"]="other";
  Check(!pipe.SetStream(descriptor,instance),"incompatible geometry reused calibration");
  Check(!(bool)pipe.Latest.Valid,"incompatible stream kept pending eye");
  var codec=typeof(CoreChecks).Assembly.GetType("SpatialTracking.TrackingWire");
  var decode=codec.GetMethod("Decode");
  foreach(var bad in new[]{"{\"x\":1,\"x\":2}","{\"x\":NaN}","{\"x\":1,}","{'x':1}","{} {}","/*x*/{}","{\"x\":1e999}"}){
   bool rejected=false;try{decode.Invoke(null,new object[]{System.Text.Encoding.UTF8.GetBytes(bad)});}catch(TargetInvocationException){rejected=true;}
   Check(rejected,"invalid JSON accepted: "+bad);
  }
  // Production framing must tolerate TCP partial reads and reject bounds before allocation.
  var write=codec.GetMethod("Write");var read=codec.GetMethod("Read");
  var bytes=new MemoryStream();write.Invoke(null,new object[]{bytes,JObject.Parse("{\"n\":7}")});
  var fragmented=new Fragmented(bytes.ToArray());
  Check((int)((JObject)read.Invoke(null,new object[]{fragmented}))["n"]==7,"TCP partial read broke framing");
  foreach(var raw in new[]{new byte[]{0,1,0,1},new byte[]{0,0,0,0},new byte[]{0,0,0,2,123}}){
   bool rejected=false;try{read.Invoke(null,new object[]{new MemoryStream(raw)});}catch(TargetInvocationException){rejected=true;}
   Check(rejected,"invalid/truncated frame accepted");
  }
  var smoother=typeof(CoreChecks).Assembly.GetType("SpatialTracking.EyeSmoother");
  dynamic filter=Activator.CreateInstance(smoother,new object[]{new double[]{0,0,-.6}});
  descriptor["intrinsics_id"]="K";pipe.SetStream(descriptor,instance);
  pipe.Offer(Sample((string)descriptor["stream_id"],2,2000000),2001000UL,2002000UL);
  filter.Advance(pipe.Latest,2003000UL,.016);
  Check((double)filter.X>0&&(double)filter.X<.1,"recovery snapped");
  double held=filter.X;filter.Advance(pipe.Latest,2250000UL,.016);
  Check(Math.Abs((double)filter.X-held)<1e-9,"short loss did not hold");
  for(int i=0;i<120;i++)filter.Advance(pipe.Latest,3000000UL+(ulong)i*16667,.016667);
  Check(Math.Abs((double)filter.X)<.0001,"long loss did not return to reference");
  Check(!(bool)filter.Fresh,"heartbeat revived stale measurement");
 }
}
