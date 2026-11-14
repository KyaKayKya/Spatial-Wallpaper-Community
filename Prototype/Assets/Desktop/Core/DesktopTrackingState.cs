using Newtonsoft.Json.Linq;
using SpatialTracking;
namespace SpatialWallpaper.Desktop {
 public sealed class DesktopTrackingState {
  public readonly TrackingPipeline Pipeline=new TrackingPipeline();string stream;ulong frame;
  public static JObject Pack(RawTrackingSample raw,ulong now){
   if(raw==null||!raw.Fresh(now))return null;
   var binocular=raw.Binocular;
   var head=raw.Head;var headPosition=raw.HeadPosition;bool hasHead=head!=null&&headPosition!=null;
   return new JObject{["descriptor"]=raw.Descriptor,["sent"]=now.ToString(),["sample"]=new JObject{
    ["stream_id"]=raw.Stream,["frame_id"]=raw.Frame.ToString(),["captured_at_us"]=raw.Captured.ToString(),["processed_at_us"]=raw.Captured.ToString(),
    ["tracking_state"]="tracking",["validity"]=new JObject{["eye_position"]=raw.Eye!=null,["head_pose"]=hasHead},
    ["eye_position"]=raw.Eye==null?JValue.CreateNull():(JToken)new JArray(raw.Eye),["head_pose"]=hasHead?(JToken)new JObject{["position"]=new JArray(headPosition),["orientation_xyzw"]=new JArray(head)}:JValue.CreateNull(),
    ["quality"]=new JObject{["flags"]=new JArray(),["reprojection_error_px"]=null},["extensions"]=new JObject{[BinocularObservation.Feature]=binocular==null?JValue.CreateNull():(JToken)new JObject{["center_uv"]=new JArray(binocular.U,binocular.V),["ipd_px"]=binocular.IpdPixels}}
   }};
  }
  public void Accept(JObject state,ulong now){
   if(state==null){Pipeline.Invalidate("REMOTE_SOURCE_UNAVAILABLE");return;}
   var descriptor=(JObject)state["descriptor"];string id=TrackingWire.Id(descriptor["stream_id"]);var sample=(JObject)state["sample"];ulong next=TrackingWire.Decimal(sample["frame_id"]);
   if(stream!=id){Pipeline.SetNegotiatedFeatures(new JArray(BinocularObservation.Feature));if(!Pipeline.SetStream(descriptor,TrackingWire.Id(descriptor["tracker_instance_id"])))throw new System.IO.InvalidDataException("desktop-invalid-tracking-descriptor");stream=id;frame=0;}
   if(next==frame)return;frame=next;Pipeline.Offer(sample,TrackingWire.Decimal(state["sent"]),now);
  }
 }
}
