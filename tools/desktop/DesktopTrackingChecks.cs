using System;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Desktop;
static class DesktopTrackingChecks {
 static void Check(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static int Main(){
  string stream=Guid.NewGuid().ToString(),instance=Guid.NewGuid().ToString();ulong now=QpcClock.Now;
  var descriptor=JObject.FromObject(new{stream_id=stream,tracker_instance_id=instance,camera_id="simulation",intent_revision="1",coordinate_frame="camera_upright_v1",length_unit="model_m",scale_source="canonical_face_prior",head_model_id="head",estimator_id="synthetic-eye-v1",intrinsics_id="K",geometry_id="G",intrinsics_source="prior",clock_domain=QpcClock.Domain,timestamp_origin="frame_arrival",actual_mode=new{mode_id=(object)null,source_width_px=640,source_height_px=480,width_px=640,height_px=480,fps=60,pixel_format="RGB8",rotation_deg=0,crop_rect=new[]{0,0,640,480},mirrored=false},optional_features=new[]{BinocularObservation.Feature}});
  var raw=new RawTrackingSample(stream,1,now,new[]{0.0,0,.6},new[]{.1,0,0,Math.Sqrt(.99)},descriptor,new BinocularObservation(.1,-.2,100),new[]{.02,.01,.5});
  var codec=new DesktopTrackingState();codec.Accept(DesktopTrackingState.Pack(raw,now+100),now+200);
  Check(codec.Pipeline.RawLatest.Binocular.IpdPixels==100,"raw-binocular-roundtrip");
  Check(codec.Pipeline.RawLatest.Head!=null&&Math.Abs(codec.Pipeline.RawLatest.Head[0]-.1)<1e-9,"head-orientation-preserved-through-desktop-relay");
  Check(codec.Pipeline.RawLatest.HeadPosition[0]==.02,"original-head-position-preserved-without-substituting-eye");
  codec.Accept(DesktopTrackingState.Pack(raw,now+100),now+300);Check(codec.Pipeline.Accepted==1,"same-frame-does-not-refresh-freshness");
  var stale=new DesktopTrackingState();stale.Accept(DesktopTrackingState.Pack(raw,now+100),now+300000);Check(stale.Pipeline.RawLatest==null,"stale-relay-cannot-revive-measurement");
  codec.Accept(null,now+400);Check(codec.Pipeline.RawLatest==null,"remote-loss-invalidates-pending-measurement");return 0;
 }
}
