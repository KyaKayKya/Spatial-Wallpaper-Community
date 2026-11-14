using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Threading;
using Newtonsoft.Json.Linq;
using SpatialTracking;

static class TrackingHostChecks {
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static Process Child(RuntimeTrackingHost host)=>(Process)typeof(RuntimeTrackingHost).GetField("child",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(host);
 static int Main(string[] args){
  RuntimeTrackingHost host=null;Process retained=null;int code=1;var result=new JObject();
  try{
   var pipeline=new TrackingPipeline(TrackingCalibration.Load(args[2]));
   host=new RuntimeTrackingHost(pipeline,args[0],args[1],"simulate","simulation");host.Start();
   var deadline=Stopwatch.StartNew();
   while(pipeline.Latest==null||!pipeline.Latest.Valid){Need(deadline.ElapsedMilliseconds<10000,"tracking-host-never-produced-valid-sample");Thread.Sleep(10);}
   var child=Child(host);retained=Process.GetProcessById(child.Id);var handle=retained.Handle;
   long creation=retained.StartTime.ToUniversalTime().ToFileTimeUtc();
   Need(creation==child.StartTime.ToUniversalTime().ToFileTimeUtc(),"tracking-host-child-identity");
   result["pid"]=retained.Id;result["creation_filetime"]=creation;result["state_before"]=host.SourceState;
   if(args[4]=="unproven"){
    var waits=new ConcurrentBag<int>();int kills=0;
    RuntimeTrackingHost.ChildWait=(p,ms)=>{Need(object.ReferenceEquals(p,child),"tracking-wait-owner-changed");waits.Add(ms);return false;};
    RuntimeTrackingHost.ChildKill=p=>{Need(object.ReferenceEquals(p,child),"tracking-kill-owner-changed");Interlocked.Increment(ref kills);};
    bool blocked=false;
    try{host.Dispose();}catch(TimeoutException error){blocked=error.Message=="TRACKER_CHILD_EXIT_UNPROVEN";}
    Need(blocked&&object.ReferenceEquals(Child(host),child),"tracking-unproven-exit-lost-owner");
    Need(Array.IndexOf(waits.ToArray(),3500)>=0&&Array.IndexOf(waits.ToArray(),1500)>=0&&kills>0,"tracking-unproven-exit-skipped-second-wait");
    Need(!host.SourceConnected&&!host.SourceState.StartsWith("streaming / ",StringComparison.Ordinal),"tracking-retired-source-still-connected");
    result["unproven_exit_blocked"]=true;result["wait_budgets_ms"]=new JArray(waits.ToArray());result["controlled_kill_requests"]=kills;
    RuntimeTrackingHost.ChildWait=null;RuntimeTrackingHost.ChildKill=null;
   }
   host.Dispose();
   bool exited=retained.WaitForExit(5000);result["exited"]=exited;result["exit_code"]=exited?(JToken)retained.ExitCode:JValue.CreateNull();result["state_after"]=host.SourceState;
   Need(exited&&retained.ExitCode==0,"tracking-host-shutdown-not-clean");
   Need(Child(host)==null&&!host.SourceConnected&&host.SourceState=="disconnected","tracking-host-retained-stale-source-or-owner");
   result["status"]="passed-development-original-tracker-clean-shutdown";code=0;
  }catch(Exception failure){result["error"]=failure.ToString();Console.Error.WriteLine(failure);}
  finally{
   RuntimeTrackingHost.ChildWait=null;RuntimeTrackingHost.ChildKill=null;
   try{host?.Dispose();}catch(Exception failure){result["cleanup_error"]=failure.ToString();Console.Error.WriteLine(failure);code=1;}
   retained?.Dispose();result["scope"]="actual original TCP synthetic Tracker; controlled wait-proof failure is not an OS kill failure; no physical camera";File.WriteAllText(args[3],result.ToString());
  }
  return code;
 }
}
