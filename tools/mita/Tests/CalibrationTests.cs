using System.Reflection;
using Newtonsoft.Json.Linq;
using SpatialTracking;

var type=Assembly.GetExecutingAssembly().GetType("SpatialWallpaper.Mita.MitaCalibration");
Check(type!=null,"existing applied five-parameter calibration must be supported");
dynamic Parse(JObject json)=>Activator.CreateInstance(type,new object[]{json});
void Reject(JObject value){
 try{Parse(value);throw new Exception("FAIL: invalid calibration accepted");}
 catch(TargetInvocationException e) when(e.InnerException is InvalidDataException){}
}
var descriptor=new JObject {
 ["camera_id"]="test-camera",["head_model_id"]="model",["estimator_id"]="estimator",["intrinsics_id"]="intrinsics",["geometry_id"]="geometry",
 ["actual_mode"]=new JObject{["width_px"]=640,["height_px"]=480,["source_width_px"]=640,["source_height_px"]=480,["mode_id"]=null,["rotation_deg"]=0,["mirrored"]=false},
 ["optional_features"]=new JArray(BinocularObservation.Feature)
};
var profile=new CalibrationProfile();profile.manual.Bind(descriptor,"display-a");
var json=JObject.FromObject(profile);dynamic single=Parse(json),applied=Parse(new JObject{["applied_profile"]=json.DeepClone()});
Check(single.NeedsBinocular&&applied.NeedsBinocular,"standalone and applied profiles use validated binocular input");
var observation=new BinocularObservation(.1,-.2,100);
var raw=new RawTrackingSample("stream",1,1000,null,null,descriptor,observation);
TrackingSnapshot sample=single.Map(raw,1100UL,"display-a");
var expected=profile.manual.Map(observation);
Check(sample.Valid&&Math.Abs(sample.X-expected[0])<1e-10&&Math.Abs(sample.Y-expected[1])<1e-10&&Math.Abs(sample.Z-expected[2])<1e-10,"real five-parameter values must reach the eye without affine substitution");
Check(!((TrackingSnapshot)single.Map(raw,1100UL,"display-b")).Valid,"calibration cannot follow a different physical display");
Check(!((TrackingSnapshot)single.Map(raw,300000UL,"display-a")).Valid,"stale raw eye must not count as fresh");
var other=(JObject)descriptor.DeepClone();other["camera_id"]="other-camera";
Check(!((TrackingSnapshot)single.Map(new RawTrackingSample("stream",2,1000,null,null,other,observation),1100UL,"display-a")).Valid,"wrong camera must not use saved mapping");
var missing=new RawTrackingSample("stream",3,1000,null,null,descriptor);
Check(!((TrackingSnapshot)single.Map(missing,1100UL,"display-a")).Valid,"missing binocular data must not fall back to raw affine eye");
Reject(new JObject());Reject(JObject.FromObject(new CalibrationProfile()));
var wrong=(JObject)json.DeepClone();wrong["schema_version"]=99;Reject(wrong);
var legacy=JObject.FromObject(new TrackingCalibration());
Check(!Parse(legacy).NeedsBinocular,"legacy synthetic calibration remains available");
Console.WriteLine("PASS: applied/standalone five-parameter profiles, numeric mapping, source/display/freshness rejection and legacy input");
static void Check(bool value,string message){if(!value)throw new Exception("FAIL: "+message);}
