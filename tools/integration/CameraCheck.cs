using System;
using System.Threading;
using SpatialTracking;
public static class CameraCheck {
 public static int Main(string[] args){
  var pipeline=new TrackingPipeline();
  using(var host=new RuntimeTrackingHost(pipeline,args[0],args[1],"camera",args[2])){
   host.Start();
   for(int i=0;i<250;i++){
    if(pipeline.RawLatest!=null||host.SourceError!=null)break;
    Thread.Sleep(100);
   }
   Console.WriteLine("host="+host.Diagnostic+" source="+host.SourceState+" error="+host.SourceError+" pipeline="+pipeline.Status+" accepted="+pipeline.Accepted);
   return pipeline.RawLatest!=null?0:2;
  }
 }
}
