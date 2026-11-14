using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace SpatialTracking {
 public enum CalibrationTool { Level, View, Depth, Follow, OffsetX, OffsetY, ScaleX, ScaleY, ScaleZ }

 public sealed class CalibrationProfile {
  public int schema_version=2;
  public string mapping_type=FiveParameterMapping.Kind;
  public FiveParameterMapping manual=new FiveParameterMapping();
  public string migrated_from;
  public string id=Guid.NewGuid().ToString("N"),name="";
  public long revision;
  public TrackingCalibration mapping=new TrackingCalibration();
  public double world_pitch,world_roll,follow_yaw,follow_pitch,follow_roll;
  public double[] base_q={0,1,0,0};
  public double base_scale=1,amplitude=1;
  public bool reference_ready;
  public string display_units="nominal_m";
  public CalibrationProfile Copy(){return JsonConvert.DeserializeObject<CalibrationProfile>(JsonConvert.SerializeObject(this));}
  public bool CanUse(string display,string camera,string mode,JObject descriptor){
   if(schema_version!=2||!Valid()||!manual.Bound||manual.display_id!=display)return false;
   if(camera!=null&&((string)manual.source["camera_id"]!=camera||(string)manual.source["actual_mode"]["mode_id"]!=mode))return false;
   return descriptor==null||manual.Matches(descriptor,display);
  }
  public bool Valid(){
   if(string.IsNullOrEmpty(id)||revision<0||display_units!="nominal_m")return false;
   if(schema_version==2)return mapping_type==FiveParameterMapping.Kind&&manual!=null&&manual.Valid()&&world_pitch==0&&world_roll==0;
   if(schema_version!=1||mapping==null||!mapping.Valid()||base_q==null||base_q.Length!=4)return false;
   double norm=0;foreach(double n in base_q){if(!TrackingCalibration.Finite(n))return false;norm+=n*n;}
   if(Math.Abs(norm-1)>.0001)return false;
   foreach(double n in new[]{world_pitch,world_roll,follow_yaw,follow_pitch,follow_roll,base_scale,amplitude})if(!TrackingCalibration.Finite(n))return false;
   return Math.Abs(world_pitch)<=8&&Math.Abs(world_roll)<=5&&Math.Abs(follow_yaw)<=90&&Math.Abs(follow_pitch)<=90&&Math.Abs(follow_roll)<=90&&base_scale>0&&amplitude>=.25&&amplitude<=3;
  }
  public void Rebuild(){if(schema_version==1){mapping.scale=base_scale*amplitude;mapping.q_xyzw=Multiply(Euler(follow_pitch,follow_yaw,follow_roll),base_q);}}
  // The affine transform cannot be converted losslessly. Preserve its file and start a new draft.
  public CalibrationProfile CreateManualDraft(){return new CalibrationProfile{name=name+" 五参数",migrated_from=id};}
  public static double[] Euler(double pitch,double yaw,double roll){
   double r=Math.PI/360;
   return Multiply(Multiply(new[]{0.0,Math.Sin(yaw*r),0.0,Math.Cos(yaw*r)},new[]{Math.Sin(pitch*r),0.0,0.0,Math.Cos(pitch*r)}),new[]{0.0,0.0,Math.Sin(roll*r),Math.Cos(roll*r)});
  }
  public static double[] Multiply(double[] a,double[] b){return new[]{a[3]*b[0]+a[0]*b[3]+a[1]*b[2]-a[2]*b[1],a[3]*b[1]-a[0]*b[2]+a[1]*b[3]+a[2]*b[0],a[3]*b[2]+a[0]*b[1]-a[1]*b[0]+a[2]*b[3],a[3]*b[3]-a[0]*b[0]-a[1]*b[1]-a[2]*b[2]};}
  public void BindReference(double[] eye,double[] head,JObject descriptor){
   mapping.q0=(double[])eye.Clone();
   foreach(string key in new[]{"camera_id","head_model_id","estimator_id","intrinsics_id","geometry_id","coordinate_frame","length_unit","scale_source"})typeof(TrackingCalibration).GetField(key).SetValue(mapping,TrackingWire.Text(descriptor[key]));
   // Face approximately parallel to the screen is an explicit initialization assumption.
   if(head!=null)base_q=Multiply(new[]{0.0,1.0,0.0,0.0},new[]{-head[0],-head[1],-head[2],head[3]});
   reference_ready=true;mapping.measured=false;Rebuild();
  }
 }
}
