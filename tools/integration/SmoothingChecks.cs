using System;
using System.IO;
using Newtonsoft.Json.Linq;
using SpatialTracking;

public static class SmoothingChecks {
 public static int Main(string[] args){
  var f=new EyeSmoother(new double[]{0,0,-.6});int reached=-1;
  double inputNoise=0,outputNoise=0;
  for(int i=0;i<600;i++){
   ulong time=1000000+(ulong)i*16667;
   double target=i>=60&&i<240?.1:0;
   if(i>=360)target=.001*Math.Sin(i*Math.PI*.8);
   f.Advance(new TrackingSnapshot(i,true,"s",(ulong)i,time,time,time,time,new[]{target,0,-.6}),time,1.0/60);
   if(i>=60&&i<240&&f.X>=.09&&reached<0)reached=i-60;
   if(i>=420){inputNoise+=target*target;outputNoise+=f.X*f.X;}
  }
  var report=new JObject{["step90_ms"]=reached*1000.0/60,["noise_input_rms_mm"]=Math.Sqrt(inputNoise/180)*1000,["noise_output_rms_mm"]=Math.Sqrt(outputNoise/180)*1000};
  Console.WriteLine(report);if(args.Length>0)File.WriteAllText(args[0],report.ToString());
  return reached>=0&&reached<=6&&outputNoise<inputNoise?0:1;
 }
}
