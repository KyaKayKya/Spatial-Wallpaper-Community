using System;
using System.IO;
using Newtonsoft.Json;
using SpatialTracking;

public static class ValidateCalibrationProfile {
 public static int Main(string[] args) {
  if(ViewportGeometry.ClientIntersectsMonitor(-32000,-32000,1366,768,0,0,1920,1080))throw new Exception("Minimized window accepted");
  if(ViewportGeometry.ClientIntersectsMonitor(100,100,0,0,0,0,1920,1080))throw new Exception("Empty window accepted");
  if(!ViewportGeometry.ClientIntersectsMonitor(164,187,1000,650,0,0,1920,1080))throw new Exception("Visible window rejected");
  if(!ViewportGeometry.ClientIntersectsMonitor(-1800,100,1000,650,-1920,0,0,1080))throw new Exception("Negative-coordinate monitor rejected");
  var p=JsonConvert.DeserializeObject<CalibrationProfile>(File.ReadAllText(args[0]));
  if(p==null||!p.Valid()||!p.reference_ready||p.mapping.measured)throw new Exception("Invalid experimental profile");
  var points=new[]{p.mapping.q0,new[]{p.mapping.q0[0]+.1,p.mapping.q0[1],p.mapping.q0[2]},new[]{p.mapping.q0[0],p.mapping.q0[1]+.1,p.mapping.q0[2]},new[]{p.mapping.q0[0],p.mapping.q0[1],p.mapping.q0[2]+.1}};
  var before=new double[points.Length][];for(int n=0;n<points.Length;n++)before[n]=p.mapping.Map(points[n]);p.Rebuild();
  if(!p.Valid())throw new Exception("Rebuild invalidated profile");
  for(int n=0;n<points.Length;n++){
   var after=p.mapping.Map(points[n]);
   for(int i=0;i<3;i++)if(Math.Abs(before[n][i]-after[i])>1e-10||(n==0&&Math.Abs(after[i]-p.mapping.e0[i])>1e-10))throw new Exception("Mapping changed");
  }
  Console.WriteLine("PASS: actual CalibrationProfile validates, rebuild preserves mapping, reference maps to E0");
  return 0;
 }
}
