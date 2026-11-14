using System;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace SpatialTracking {
 public sealed class FiveParameterMapping {
  public const string Kind="five_parameter_v1";
  public const string Input="normalized_unmirrored_iris_v1";
  // One reference scene unit is 0.1 nominal metres. It is fixed, not viewport-relative.
  public const double ReferenceUnit=.1;
  public double offsetX,offsetY,scaleX=4.38,scaleY=4.38,scaleZ=6.7;
  public string input_type=Input;
  public JObject source;
  public string display_id;
  public double display_width=.53;
  public bool Valid(){
   return input_type==Input&&In(offsetX,-1,1)&&In(offsetY,-1,1)&&In(scaleX,.05,30)&&In(scaleY,.05,30)&&In(scaleZ,.1,30)&&In(display_width,.1,2)&&(source==null?display_id==null:SourceValid());
  }
  bool SourceValid(){
   if(string.IsNullOrWhiteSpace(display_id)||!(source["actual_mode"] is JObject mode)||mode["mirrored"]?.Type!=JTokenType.Boolean||(bool)mode["mirrored"]||mode["rotation_deg"]?.Type!=JTokenType.Integer||(double)mode["rotation_deg"]!=0)return false;
   foreach(var key in Keys)if(source[key]?.Type!=JTokenType.String||string.IsNullOrWhiteSpace((string)source[key]))return false;
   foreach(var key in new[]{"width_px","height_px","source_width_px","source_height_px"})if(mode[key]?.Type!=JTokenType.Integer||(double)mode[key]<=0||(double)mode[key]>16384)return false;
   return true;
  }
  static bool In(double value,double min,double max){return TrackingCalibration.Finite(value)&&value>=min&&value<=max;}
  public double[] Map(BinocularObservation input){
   if(!Valid()||input==null||!input.Valid())throw new ArgumentException("INVALID_BINOCULAR_MAPPING");
   return new[]{-(input.U+offsetX)*scaleX*ReferenceUnit,-(input.V+offsetY)*scaleY*ReferenceUnit,-(100/input.IpdPixels)*scaleZ*ReferenceUnit};
  }
  static readonly string[] Keys={"camera_id","head_model_id","estimator_id","intrinsics_id","geometry_id"};
  public void Bind(JObject descriptor,string display){
   if(!Supports(descriptor)||string.IsNullOrWhiteSpace(display))throw new ArgumentException("SOURCE_MISMATCH");
   source=new JObject();foreach(var key in Keys)source[key]=TrackingWire.Text(descriptor[key]);
   source["actual_mode"]=descriptor["actual_mode"].DeepClone();display_id=display;
  }
  public bool Bound{get{return Valid()&&source!=null&&!string.IsNullOrWhiteSpace(display_id)&&source["actual_mode"] is JObject;}}
  public bool Matches(JObject descriptor,string display){
   if(!Bound||display!=display_id||!Supports(descriptor))return false;
   foreach(var key in Keys)if(!JToken.DeepEquals(source[key],descriptor[key]))return false;
   return JToken.DeepEquals(source["actual_mode"],descriptor["actual_mode"]);
  }
  public static bool Supports(JObject descriptor){
   if(descriptor==null||!(descriptor["optional_features"] is JArray features))return false;
   if(!features.Any(x=>x.Type==JTokenType.String&&(string)x==BinocularObservation.Feature))return false;
   var mode=descriptor["actual_mode"] as JObject;
   return mode!=null&&(bool?)mode["mirrored"]==false&&(int?)mode["rotation_deg"]==0;
  }
 }
}
