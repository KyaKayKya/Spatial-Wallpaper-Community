using System.IO;
using Newtonsoft.Json.Linq;
using SpatialTracking;

namespace SpatialWallpaper.Mita {
 // Adapt the existing calibration formats, without inventing another eye mapping.
 public sealed class MitaCalibration {
  public readonly TrackingCalibration Legacy;
  public readonly CalibrationProfile Profile;
  public bool NeedsBinocular=>Profile!=null;
  public MitaCalibration(JObject json){
   if(json.ContainsKey("applied_profile"))json=json["applied_profile"] as JObject;
   if(json==null)throw new InvalidDataException("MISSING_APPLIED_CALIBRATION");
   if(json.ContainsKey("schema_version")){
    Profile=json.ToObject<CalibrationProfile>();
    if(Profile.schema_version!=2||!Profile.Valid()||!Profile.manual.Bound)throw new InvalidDataException("INVALID_BOUND_CALIBRATION_PROFILE");
   }else{
    foreach(string key in new[]{"q0","e0","q_xyzw","scale"})if(!json.ContainsKey(key))throw new InvalidDataException("UNRECOGNIZED_CALIBRATION");
    Legacy=json.ToObject<TrackingCalibration>();
    if(!Legacy.Valid())throw new InvalidDataException("INVALID_CALIBRATION");
   }
  }
  public TrackingSnapshot Map(RawTrackingSample raw,ulong now,string display){
   if(raw==null||raw.Binocular==null||!raw.Fresh(now)||!Profile.manual.Matches(raw.Descriptor,display))return new TrackingSnapshot(0,false);
   var eye=Profile.manual.Map(raw.Binocular);
   return TrackingCalibration.SafeEye(eye)?new TrackingSnapshot(0,true,raw.Stream,raw.Frame,raw.Captured,eye:eye):new TrackingSnapshot(0,false);
  }
 }
}
