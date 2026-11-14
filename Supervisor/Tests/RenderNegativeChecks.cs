// Diagnostic driver uses the production LPAC launcher and lifecycle.
using System;
using System.IO;
using System.Threading;
using System.Diagnostics;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Supervision;
using SpatialWallpaper.Supervision.Windows;
using SpatialWallpaper.Sdk.Hosting;
static class RenderNegativeChecks {
 sealed class Launcher:IWorkerLauncher {
  readonly WindowsWorkerLauncher inner;internal WindowsManagedWorker Worker;
  internal Launcher(WindowsWorkerLauncher value){inner=value;}
  public IManagedWorker Start(LaunchRequest request,AttemptReceipt receipt,Action<int> created){return Worker=(WindowsManagedWorker)inner.Start(request,receipt,created);}
 }
 static void Require(bool condition,string reason){if(!condition)throw new Exception(reason);}
 static void Until(Func<bool> ready){var time=Stopwatch.StartNew();while(!ready()){if(time.ElapsedMilliseconds>30000)throw new TimeoutException("render-negative-timeout");Thread.Sleep(10);}}
 static Dictionary<string,string> Hashes(JObject value){var result=new Dictionary<string,string>();foreach(var p in value.Properties())result.Add(p.Name,(string)p.Value);return result;}
 static int Main(string[] args){
  string output=args[0];var result=new JObject{{"status","failed"}};Launcher launcher=null;SupervisedSession session=null;
  try{
   var inventory=JObject.Parse(File.ReadAllText(args[1]));long origin=checked((long)SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Qpc100ns());Func<long> now=()=>((checked((long)SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Qpc100ns())-origin)/10000);
   result["clock_origin_qpc"]=origin;result["clock_frequency"]=10000000L;
   var journal=new JournalActor(Path.Combine(output,"journal"),32);
   try{using(var station=new PrivateStation()){
    string vr=Path.Combine(output,"validator");var vi=JObject.Parse(File.ReadAllText(Path.Combine(vr,"build.json")));
    var validator=new ValidatorLauncher(station,new WorkerImage(vr,"Validator.exe",Hashes((JObject)vi["files"])),args[5]);
    var storage=new SpatialWallpaper.Supervision.Broker.StorageBroker(Path.Combine(output,"storage"));storage.Initialized.GetAwaiter().GetResult();
    var gpu=new WindowsGpuSampler(now);
    launcher=new Launcher(new WindowsWorkerLauncher(station,new WorkerImage(args[2],"Worker.exe",Hashes((JObject)inventory["runtime_files"])),args[5],now,640,360,gpu,validator,storage));
    var runtime=new SupervisorRuntime(journal,launcher,new WatchdogPolicy(RuntimeLimits.StartupMs,RuntimeLimits.FrameMs,RuntimeLimits.StopMs,RuntimeLimits.ProcessBytes,RuntimeLimits.CpuWindowMs,RuntimeLimits.CpuBudgetMs,RuntimeLimits.ActualGpuBytes,RuntimeLimits.GpuStaleMs),1,RuntimeLimits.ProcessBytes,now);
    try{
     var task=runtime.Start(new LaunchRequest(Guid.NewGuid(),args[4],Guid.NewGuid(),1,StartIntent.Manual,args[3],RuntimeLimits.ProcessBytes));Until(()=>task.IsCompleted);session=task.GetAwaiter().GetResult();
     Until(()=>session.Completion.IsCompleted);session.Completion.GetAwaiter().GetResult();
     string log=System.Text.Encoding.UTF8.GetString(launcher.Worker.ReadLog());File.WriteAllText(Path.Combine(output,"worker.log"),log);
     string mode=(string)inventory["mode"];int frames=(int)inventory["expected_frames"];
     Require(log.Contains("SDK_RENDER_NEGATIVE "),"render-mutation-not-reached");
     Require(log.Contains((string)inventory["expected_error"]),"wrong-render-negative-failure");
     Require(session.Fault==(mode=="stop-before-late-readback"?FaultReason.FrameTimeout:FaultReason.DeviceLost),"wrong-render-negative-fault:"+session.Fault);
     // Completion follows Worker.Dispose, which joins the frame reader. With
     // strictly increasing positive wire sequences, count N and last N prove
     // precisely 1..N, without duplicates, gaps or a later queued frame.
     Require(launcher.Worker.ReceivedFrames==frames&&launcher.Worker.Sequence==frames,"frame-published-after-render-mutation:"+launcher.Worker.ReceivedFrames);
     Require((bool?)session.StopEvidence["cleanup_completed"]==true&&session.StopEvidence["worker_kernel_exited_at_ms"]?.Type==JTokenType.Integer,"render-negative-kernel-cleanup-unproven");
     Require(!Directory.Exists(Path.Combine(args[5],session.Attempt.ToString("N"))),"render-negative-runtime-not-reclaimed");
     result["status"]="expected-render-mutation-rejected";result["received_frames"]=launcher.Worker.ReceivedFrames;result["last_sequence"]=launcher.Worker.Sequence;result["attempt"]=session.Attempt.ToString("N");result["fault"]=session.Fault.ToString();result["stop_evidence"]=session.StopEvidence;
    }finally{
     runtime.Close();Until(()=>runtime.Completion.IsCompleted);runtime.Completion.GetAwaiter().GetResult();gpu.Close();storage.Close();storage.Completion.GetAwaiter().GetResult();
     if(launcher.Worker!=null)File.WriteAllBytes(Path.Combine(output,"worker.log"),launcher.Worker.ReadLog());
    }
   }}finally{journal.Close();Until(()=>journal.Completion.IsCompleted);journal.Completion.GetAwaiter().GetResult();}
   return 0;
  }catch(Exception error){result["status"]="failed";result["error"]=error.ToString();Console.Error.WriteLine(error);return 1;}
  finally{File.WriteAllText(Path.Combine(output,"results.json"),result.ToString());}
 }
}
