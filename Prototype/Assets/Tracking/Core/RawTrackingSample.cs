using Newtonsoft.Json.Linq;

namespace SpatialTracking {
 // Immutable across the network/main-thread boundary. Arrays and JSON are copied on access.
 public sealed class RawTrackingSample {
  public readonly string Stream;
  public readonly ulong Frame,Captured;
  readonly double[] eye,head,headPosition;
  readonly JObject descriptor;
  public readonly BinocularObservation Binocular;
  public double[] Eye{get{return eye==null?null:(double[])eye.Clone();}}
  public double[] Head{get{return head==null?null:(double[])head.Clone();}}
  public double[] HeadPosition{get{return headPosition==null?null:(double[])headPosition.Clone();}}
  public JObject Descriptor{get{return (JObject)descriptor.DeepClone();}}
  public RawTrackingSample(string stream,ulong frame,ulong captured,double[] point,double[] orientation,JObject source,BinocularObservation binocular=null,double[] headPoint=null){
   Stream=stream;Frame=frame;Captured=captured;eye=point==null?null:(double[])point.Clone();head=orientation==null?null:(double[])orientation.Clone();headPosition=headPoint==null?null:(double[])headPoint.Clone();descriptor=(JObject)source.DeepClone();Binocular=binocular;
  }
  public bool Fresh(ulong now){return now>=Captured&&now-Captured<=200000;}
 }
}
