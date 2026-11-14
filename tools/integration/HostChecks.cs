using System;
using System.Diagnostics;
using System.Reflection;
using System.Threading;
using SpatialTracking;

public static class HostChecks {
 static void Require(bool ok,string reason){if(!ok)throw new Exception(reason);}
 static bool Wait(Func<bool> test,int timeout){var sw=Stopwatch.StartNew();while(sw.ElapsedMilliseconds<timeout){if(test())return true;Thread.Sleep(10);}return false;}
 public static int Main(string[] args){
  try{
   var calibration=TrackingCalibration.Load(args[2]);var pipeline=new TrackingPipeline(calibration);
   using(var host=new RuntimeTrackingHost(pipeline,args[0],args[1],"simulate","simulation","","faults")){
    host.Start();
    Require(Wait(()=>pipeline.Latest.Valid,10000),"No authenticated real TCP sample: "+host.Diagnostic);
    Require(pipeline.Latest.Z<-.49&&pipeline.Latest.Z>-.71,"camera coordinates bypassed calibration");
    Require(Wait(()=>!pipeline.Latest.Valid,6000),"explicit no-face did not invalidate slot");
    Require(Wait(()=>pipeline.Latest.Valid,4000),"no-face/silence recovery failed");
    Require(pipeline.Rejected>0,"fault samples did not exercise age/order rejection");
    var oldStream=pipeline.Latest.Stream;
    // Simulate an actual owned Tracker crash, without a test-only production hook.
    var process=(Process)typeof(RuntimeTrackingHost).GetField("child",BindingFlags.NonPublic|BindingFlags.Instance).GetValue(host);
    process.Kill();
    Require(Wait(()=>host.Connections>=2&&pipeline.Latest.Valid&&pipeline.Latest.Stream!=oldStream,12000),"supervisor failed to restart and resnapshot");
    Require(pipeline.Latest.Z<-.49&&pipeline.Latest.Z>-.71,"reconnect lost saved calibration");
    Console.WriteLine($"PASS real C#/Python TCP; accepted={pipeline.Accepted}; rejected={pipeline.Rejected}; connections={host.Connections}; restarts={host.Restarts}");
   }
   return 0;
  }catch(Exception e){Console.Error.WriteLine("FAIL "+e);return 1;}
 }
}
