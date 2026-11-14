using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace SpatialTracking {
 // Network thread owns validation. A single immutable slot crosses to Unity's main thread.
 public sealed class TrackingPipeline {
  readonly object gate=new object();readonly TrackingCalibration calibration;
  readonly bool rawOnly;
  public bool WantsBinocular{get{return rawOnly;}}
  bool binocularEnabled,negotiatedBinocular;
  RawTrackingSample raw;
  JObject descriptor;
  public RawTrackingSample RawLatest{get{lock(gate)return raw;}}
  public JObject Descriptor{get{lock(gate)return descriptor==null?null:(JObject)descriptor.DeepClone();}}
  TrackingSnapshot latest=new TrackingSnapshot(0,false);long version;
  string stream;ulong lastFrame,lastCapture;bool compatible;
  public long Accepted{get;private set;}public long Rejected{get;private set;}
  public string Status{get;private set;}="STARTING";
  public TrackingSnapshot Latest{get{lock(gate)return latest;}}
  public TrackingPipeline(TrackingCalibration value){if(value==null||!value.Valid())throw new InvalidDataException("INVALID_CALIBRATION");calibration=value;}
  // Calibration hosts consume validated raw measurements before a mapping exists.
  public TrackingPipeline(){rawOnly=true;}
  public void Invalidate(string reason){lock(gate){latest=new TrackingSnapshot(++version,false);raw=null;Status=reason;}}
  public void Disconnect(){stream=null;compatible=false;binocularEnabled=negotiatedBinocular=false;lock(gate)descriptor=null;Invalidate("DISCONNECTED");}
  public void SetNegotiatedFeatures(JArray selected){
   if(selected==null||selected.Count>1||selected.Count==1&&(!rawOnly||TrackingWire.Text(selected[0])!=BinocularObservation.Feature))throw new InvalidDataException("UNSUPPORTED_CAPABILITY");
   Disconnect();negotiatedBinocular=selected.Count==1;
  }
  public bool SetStream(JObject descriptor,string instance){
   if(descriptor==null){stream=null;compatible=false;Invalidate("NO_STREAM");return false;}
   try{
    string id=TrackingWire.Id(descriptor["stream_id"]);
    if(TrackingWire.Id(descriptor["tracker_instance_id"])!=instance)throw new InvalidDataException("SOURCE_MISMATCH");
    TrackingWire.Decimal(descriptor["intent_revision"]);
    if(TrackingWire.Text(descriptor["clock_domain"])!=QpcClock.Domain)throw new InvalidDataException("CLOCK_DOMAIN_MISMATCH");
    string origin=TrackingWire.Text(descriptor["timestamp_origin"]);
    if(origin!="frame_arrival"&&origin!="device_capture")throw new InvalidDataException("INVALID_TIMESTAMP_ORIGIN");
    foreach(var key in new[]{"camera_id","coordinate_frame","length_unit","scale_source","head_model_id","estimator_id","intrinsics_id","geometry_id","intrinsics_source"})TrackingWire.Text(descriptor[key]);
    if(rawOnly&&((string)descriptor["coordinate_frame"]!="camera_upright_v1"||(string)descriptor["length_unit"]!="model_m"||(string)descriptor["scale_source"]!="canonical_face_prior"))throw new InvalidDataException("UNSUPPORTED_COORDINATE_SYSTEM");
    var mode=descriptor["actual_mode"] as JObject;
    if(mode==null||mode["mirrored"]?.Type!=JTokenType.Boolean||(bool)mode["mirrored"])throw new InvalidDataException("INVALID_GEOMETRY");
    foreach(var key in new[]{"source_width_px","source_height_px","width_px","height_px"})
     if(mode[key]?.Type!=JTokenType.Integer||TrackingWire.Number(mode[key])<=0||TrackingWire.Number(mode[key])>16384)throw new InvalidDataException("INVALID_GEOMETRY");
    if(!mode.ContainsKey("mode_id")||(mode["mode_id"].Type!=JTokenType.Null&&mode["mode_id"].Type!=JTokenType.String)||TrackingWire.Number(mode["fps"])<0)throw new InvalidDataException("INVALID_GEOMETRY");
    TrackingWire.Text(mode["pixel_format"]);
    if(mode["rotation_deg"]?.Type!=JTokenType.Integer||Array.IndexOf(new[]{0,90,180,270},(int)mode["rotation_deg"])<0)throw new InvalidDataException("INVALID_GEOMETRY");
    var crop=TrackingWire.Vector(mode["crop_rect"],4);
    foreach(double x in crop)if(x!=Math.Floor(x))throw new InvalidDataException("INVALID_GEOMETRY");
    if(crop[0]<0||crop[1]<0||crop[2]<=0||crop[3]<=0||crop[0]+crop[2]>(double)mode["source_width_px"]||crop[1]+crop[3]>(double)mode["source_height_px"])throw new InvalidDataException("INVALID_GEOMETRY");
    if(Array.IndexOf(new[]{"prior","calibrated","device_metadata"},(string)descriptor["intrinsics_source"])<0)throw new InvalidDataException("INVALID_INTRINSICS_SOURCE");
    var features=descriptor["optional_features"] as JArray;
    if(features==null||features.Count!=(negotiatedBinocular?1:0)||features.Count==1&&TrackingWire.Text(features[0])!=BinocularObservation.Feature)throw new InvalidDataException("UNSUPPORTED_CAPABILITY");
    binocularEnabled=features.Count==1;
    if(binocularEnabled&&(int)mode["rotation_deg"]!=0)throw new InvalidDataException("INVALID_GEOMETRY");
    bool match=rawOnly||calibration.Matches(descriptor);
    if(stream!=id){lastFrame=lastCapture=0;stream=id;Invalidate("NEW_STREAM");}
    lock(gate)this.descriptor=(JObject)descriptor.DeepClone();
    compatible=match;if(!match)Invalidate("CALIBRATION_MISMATCH");
    return match;
   }catch(InvalidDataException e){stream=null;compatible=false;Invalidate(e.Message);return false;}
  }
  public bool Offer(JObject sample,ulong sent,ulong received){
   try{
    if(!compatible||stream==null||TrackingWire.Id(sample["stream_id"])!=stream)return Reject("UNKNOWN_STREAM");
    ulong frame=TrackingWire.Decimal(sample["frame_id"]),capture=TrackingWire.Decimal(sample["captured_at_us"]),processed=TrackingWire.Decimal(sample["processed_at_us"]);
    if(frame<=lastFrame||capture<=lastCapture)return Reject("OUT_OF_ORDER");
    if(capture>received+2000||processed<capture||processed>sent+2000||sent>received+2000||received<capture||received-capture>200000)return Reject("INVALID_TIMESTAMP");
    string state=TrackingWire.Text(sample["tracking_state"]);
    if(state!="tracking"&&state!="degraded"&&state!="lost"&&state!="searching")return Reject("INVALID_STATE");
    var validity=sample["validity"] as JObject;
    if(validity==null||validity["head_pose"]?.Type!=JTokenType.Boolean||validity["eye_position"]?.Type!=JTokenType.Boolean)return Reject("INVALID_VALIDITY");
    bool eyeValid=(bool)validity["eye_position"],headValid=(bool)validity["head_pose"];
    if(!headValid&&sample["head_pose"]?.Type!=JTokenType.Null)return Reject("INVALID_HEAD_POSE");
    if(headValid){
     var pose=sample["head_pose"] as JObject;if(pose==null)return Reject("INVALID_HEAD_POSE");
     TrackingWire.Vector(pose["position"],3);var q=TrackingWire.Vector(pose["orientation_xyzw"],4);
     double norm=0;foreach(double x in q)norm+=x*x;if(Math.Abs(norm-1)>.001)return Reject("INVALID_QUATERNION");
    }
    if(!eyeValid&&sample["eye_position"]?.Type!=JTokenType.Null)return Reject("INVALID_EYE");
    if((state=="lost"||state=="searching")&&(eyeValid||headValid))return Reject("INVALID_VALIDITY");
    var quality=sample["quality"] as JObject;
    var extensions=sample["extensions"] as JObject;
    if(quality==null||!(quality["flags"] is JArray)||extensions==null)return Reject("INVALID_QUALITY");
    if(extensions.Count!=(binocularEnabled?1:0)||binocularEnabled&&!extensions.ContainsKey(BinocularObservation.Feature))return Reject("UNSUPPORTED_CAPABILITY");
    var binocular=binocularEnabled?BinocularObservation.Read(extensions[BinocularObservation.Feature]):null;
    if((state=="lost"||state=="searching")&&binocular!=null)return Reject("INVALID_VALIDITY");
    foreach(var flag in (JArray)quality["flags"])if(Array.IndexOf(new[]{"no_face","partial_face","low_geometry_quality","intrinsics_prior"},TrackingWire.Text(flag))<0)return Reject("INVALID_QUALITY");
    if(quality["reprojection_error_px"]==null)return Reject("INVALID_QUALITY");
    if(quality["reprojection_error_px"].Type!=JTokenType.Null&&TrackingWire.Number(quality["reprojection_error_px"])<0)return Reject("INVALID_QUALITY");
    double[] eye=null;
    double[] rawEye=null,head=null;
    if(eyeValid){rawEye=TrackingWire.Vector(sample["eye_position"],3);eye=rawOnly?rawEye:calibration.Map(rawEye);if(!rawOnly&&(!TrackingCalibration.SafeEye(eye)||!TrackingCalibration.SafeEye(calibration.ToView(eye[0],eye[1],eye[2]))))return Reject("EYE_OUT_OF_RANGE");}
    if(headValid)head=TrackingWire.Vector(sample["head_pose"]["orientation_xyzw"],4);
    lastFrame=frame;lastCapture=capture;
    lock(gate){latest=new TrackingSnapshot(++version,eyeValid,stream,frame,capture,processed,sent,received,eye);raw=eyeValid||binocular!=null?new RawTrackingSample(stream,frame,capture,rawEye,head,descriptor,binocular,headValid?TrackingWire.Vector(sample["head_pose"]["position"],3):null):null;Status=eyeValid||binocular!=null?"TRACKING":"LOST";Accepted++;}
    return true;
   }catch(InvalidDataException e){return Reject(e.Message);}
  }
  bool Reject(string reason){Rejected++;Status=reason;return false;}
 }
}
