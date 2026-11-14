using System;
using System.IO;
using Newtonsoft.Json.Linq;

namespace SpatialTracking {
 // Local calibration only; never written back to Tracker or included in a scene package.
 public sealed class TrackingCalibration {
  public string camera_id,head_model_id,estimator_id,intrinsics_id,geometry_id;
  public string coordinate_frame="camera_upright_v1",length_unit="model_m",scale_source="canonical_face_prior";
  public double[] q0={0,.03,.635},e0={0,0,-.6},q_xyzw={0,1,0,0};
  public double[] view_center_m={0,0,0};
  public double scale=1;
  public bool measured;
  public bool Valid(){
   foreach(var v in new[]{q0,e0,q_xyzw,view_center_m})if(v==null)return false;
   if(view_center_m.Length!=3||view_center_m[2]!=0||q0.Length!=3||e0.Length!=3||q_xyzw.Length!=4||!Finite(scale)||scale<=0||scale>10)return false;
   foreach(var v in new[]{q0,e0,q_xyzw,view_center_m})foreach(double x in v)if(!Finite(x))return false;
   double norm=0;foreach(double x in q_xyzw)norm+=x*x;
   return Math.Abs(norm-1)<.0001&&SafeEye(e0)&&SafeEye(ToView(e0[0],e0[1],e0[2]));
  }
  public bool Matches(JObject d){
   if(!Valid())return false;
   foreach(string field in new[]{"camera_id","head_model_id","estimator_id","intrinsics_id","geometry_id","coordinate_frame","length_unit","scale_source"}){
    string expected=(string)GetType().GetField(field).GetValue(this);
    if(string.IsNullOrEmpty(expected)||TrackingWire.Text(d[field])!=expected)return false;
   }return true;
  }
  public double[] ToView(double x,double y,double z){return new[]{x-view_center_m[0],y-view_center_m[1],z};}
  public double[] Map(double[] point){
   double x=point[0]-q0[0],y=point[1]-q0[1],z=point[2]-q0[2];
   double qx=q_xyzw[0],qy=q_xyzw[1],qz=q_xyzw[2],qw=q_xyzw[3];
   double tx=2*(qy*z-qz*y),ty=2*(qz*x-qx*z),tz=2*(qx*y-qy*x);
   return new[]{e0[0]+scale*(x+qw*tx+qy*tz-qz*ty),
                e0[1]+scale*(y+qw*ty+qz*tx-qx*tz),
                e0[2]+scale*(z+qw*tz+qx*ty-qy*tx)};
  }
  public static bool Finite(double x){return !double.IsNaN(x)&&!double.IsInfinity(x);}
  public static bool SafeEye(double[] v){return Finite(v[0])&&Finite(v[1])&&Finite(v[2])&&v[2]<-.05&&v[0]*v[0]+v[1]*v[1]+v[2]*v[2]<9;}
  public static TrackingCalibration Load(string path){
   var json=TrackingWire.Decode(File.ReadAllBytes(path));
   var calibration=json.ToObject<TrackingCalibration>();
   if(!calibration.Valid())throw new InvalidDataException("INVALID_CALIBRATION");
   return calibration;
  }
 }
 public sealed class TrackingSnapshot {
  public readonly long Version;public readonly bool Valid;
  public readonly ulong Captured,Processed,Sent,Received,Frame;
  public readonly double X,Y,Z;public readonly string Stream;
  public TrackingSnapshot(long version,bool valid,string stream="",ulong frame=0,ulong captured=0,ulong processed=0,ulong sent=0,ulong received=0,double[] eye=null){
   Version=version;Valid=valid;Stream=stream;Frame=frame;Captured=captured;Processed=processed;Sent=sent;Received=received;
   if(eye!=null){X=eye[0];Y=eye[1];Z=eye[2];}
  }
 }
 public sealed class EyeSmoother {
  readonly double[] reference;readonly bool holdOnLoss;ulong lastGood;bool previousFresh;double recovery;
  public double X{get;private set;}public double Y{get;private set;}public double Z{get;private set;}
  public bool Fresh{get;private set;}
  public EyeSmoother(double[] origin):this(origin,false){}
  public EyeSmoother(double[] origin,bool holdOnLoss){this.holdOnLoss=holdOnLoss;reference=(double[])origin.Clone();X=origin[0];Y=origin[1];Z=origin[2];}
  public void SetReference(double[] origin){if(origin==null||origin.Length!=3||!TrackingCalibration.SafeEye(origin))throw new ArgumentException("INVALID_REFERENCE");Array.Copy(origin,reference,3);}
  public void Advance(TrackingSnapshot sample,ulong now,double dt){
   dt=Math.Max(0,Math.Min(dt,.05));
   Fresh=sample!=null&&sample.Valid&&now>=sample.Captured&&now-sample.Captured<=200000;
   if(Fresh&&!previousFresh)recovery=holdOnLoss?0:.35;
   double tx=X,ty=Y,tz=Z,tau=.25;
   if(Fresh){
    lastGood=sample.Captured;tx=sample.X;ty=sample.Y;tz=sample.Z;
    double distance=Math.Sqrt((tx-X)*(tx-X)+(ty-Y)*(ty-Y)+(tz-Z)*(tz-Z));
    tau=holdOnLoss?.012:recovery>0?.12:Math.Max(.010,.035/(1+distance*20));recovery-=dt;
   }else if(!holdOnLoss&&(lastGood==0||(now>=lastGood&&now-lastGood>350000))){tx=reference[0];ty=reference[1];tz=reference[2];}
   double alpha=1-Math.Exp(-dt/tau);X+=(tx-X)*alpha;Y+=(ty-Y)*alpha;Z+=(tz-Z)*alpha;previousFresh=Fresh;
  }
 }
}
