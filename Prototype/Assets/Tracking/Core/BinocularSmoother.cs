using System;

namespace SpatialTracking {
 // Filter camera observations once per capture, before inverse-IPD depth mapping.
 public sealed class BinocularSmoother {
  string stream;ulong captured;BinocularObservation value;
  static double Blend(double previous,double next,double dt,double quietTau,double threshold){
   double motion=Math.Max(0,Math.Abs(next-previous)/threshold-1);
   double tau=Math.Max(.025,quietTau/(1+motion*motion));
   return previous+(next-previous)*(1-Math.Exp(-dt/tau));
  }
  public BinocularObservation Update(string source,ulong time,BinocularObservation input){
   if(input==null||!input.Valid())throw new ArgumentException("INVALID_BINOCULAR_OBSERVATION");
   if(value==null||stream!=source){stream=source;captured=time;return value=input;}
   if(time<=captured)return value;
   // A dropout must not turn the next sample into an unfiltered jump.
   double dt=Math.Min((time-captured)/1000000.0,.05);captured=time;
   value=new BinocularObservation(
    Blend(value.U,input.U,dt,.09,.012),
    Blend(value.V,input.V,dt,.09,.012),
    Blend(value.IpdPixels,input.IpdPixels,dt,.25,Math.Max(1,value.IpdPixels*.015)));
   return value;
  }
 }
}
