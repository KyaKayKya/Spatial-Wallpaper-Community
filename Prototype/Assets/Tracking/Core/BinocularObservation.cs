using System;
using Newtonsoft.Json.Linq;

namespace SpatialTracking {
 public sealed class BinocularObservation {
  public const string Feature="binocular_center_v1";
  public readonly double U,V,IpdPixels;
  public BinocularObservation(double u,double v,double ipd){U=u;V=v;IpdPixels=ipd;}
  public bool Valid(){return TrackingCalibration.Finite(U)&&TrackingCalibration.Finite(V)&&Math.Abs(U)<=1&&Math.Abs(V)<=1&&TrackingCalibration.Finite(IpdPixels)&&IpdPixels>=1&&IpdPixels<=32768;}
  public static BinocularObservation Read(JToken value){
   if(value==null||value.Type==JTokenType.Null)return null;
   var uv=TrackingWire.Vector(value["center_uv"],2);
   var result=new BinocularObservation(uv[0],uv[1],TrackingWire.Number(value["ipd_px"]));
   if(!result.Valid())throw new System.IO.InvalidDataException("INVALID_BINOCULAR_OBSERVATION");
   return result;
  }
 }
}
