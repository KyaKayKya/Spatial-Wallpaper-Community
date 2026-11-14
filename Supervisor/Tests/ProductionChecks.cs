// Original MIT acceptance driver. All launch, monitoring and termination use the production runtime.
using System;using System.IO;using System.Threading;using System.Diagnostics;using System.Collections.Generic;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Windows;using SpatialWallpaper.Sdk.Hosting;
static class ProductionChecks {
 sealed class Measured:IManagedWorker,IWorkerDiagnostics {
  internal readonly WindowsManagedWorker Worker;internal long Peak,GpuPeak=-1,LastSequence,FirstAt=-1,LastAt,FirstCpu,LastCpu,MaxFrameGap,FirstKillAt=-1,KernelExitAt=-1;internal int Kills;readonly Stopwatch clock;internal readonly JArray Samples=new JArray();long sampled=-100,lastReceived=-1;
  internal Measured(WindowsManagedWorker worker,Stopwatch time){Worker=worker;clock=time;}
  public int ProcessId=>Worker.ProcessId;
  public WorkerSample Sample(){var sample=Worker.Sample();long at=clock.ElapsedMilliseconds;if(!sample.Alive&&KernelExitAt<0)KernelExitAt=at;Peak=Math.Max(Peak,sample.PrivateBytes);GpuPeak=Math.Max(GpuPeak,sample.GpuBytes);if(sample.Sequence>LastSequence){if(lastReceived>=0)MaxFrameGap=Math.Max(MaxFrameGap,sample.ReceivedAt-lastReceived);lastReceived=sample.ReceivedAt;}LastSequence=sample.Sequence;
   if(sample.Alive){if(FirstAt<0){FirstAt=at;FirstCpu=sample.CpuMilliseconds;}LastAt=at;LastCpu=sample.CpuMilliseconds;if(at-sampled>=100&&Samples.Count<2048){sampled=at;Samples.Add(new JObject{{"at_ms",at},{"private_bytes",sample.PrivateBytes},{"cpu_ms",sample.CpuMilliseconds},{"sequence",sample.Sequence}});}}return sample;}
  public void Kill(){if(FirstKillAt<0)FirstKillAt=clock.ElapsedMilliseconds;Interlocked.Increment(ref Kills);Worker.Kill();}
  public void RequestStop()=>Worker.RequestStop();
  public string ReadDiagnostic()=>Worker.ReadDiagnostic();
  public void Dispose()=>Worker.Dispose();
 }
 sealed class MeasuredLauncher:IWorkerLauncher {
  readonly WindowsWorkerLauncher platform;readonly Stopwatch clock;internal readonly Dictionary<Guid,Measured> Workers=new Dictionary<Guid,Measured>();
  internal MeasuredLauncher(WindowsWorkerLauncher value,Stopwatch time){platform=value;clock=time;}
  public IManagedWorker Start(LaunchRequest request,AttemptReceipt receipt,Action<int> created){var result=new Measured((WindowsManagedWorker)platform.Start(request,receipt,created),clock);lock(Workers)Workers.Add(receipt.Attempt,result);return result;}
  internal Measured Get(SupervisedSession session){lock(Workers)return Workers[session.Attempt];}
 }
 static void Require(bool value,string code){if(!value)throw new Exception(code);}
 static void Until(Func<bool> ready,int timeout=30000){var clock=Stopwatch.StartNew();while(!ready()){if(clock.ElapsedMilliseconds>timeout)throw new Exception("production-check-timeout");Thread.Sleep(10);}}
 static SupervisedSession Start(SupervisorRuntime runtime,LaunchRequest request){var task=runtime.Start(request);Until(()=>task.IsCompleted);return task.GetAwaiter().GetResult();}
 static int Main(string[] args){try{
  string output=args[0];var inventory=JObject.Parse(File.ReadAllText(args[1]));var hashes=new Dictionary<string,string>();foreach(var item in ((JObject)inventory["runtime_files"]).Properties())hashes.Add(item.Name,(string)item.Value);
  int width=args.Length>8?int.Parse(args[8]):640,height=args.Length>9?int.Parse(args[9]):360,steady=args.Length>10?int.Parse(args[10]):0;
  var distribution=new WorkerImage(args[2],"Worker.exe",hashes);var clock=Stopwatch.StartNew();Guid installation=Guid.NewGuid();var results=new JObject();
  var journal=new JournalActor(Path.Combine(output,"journal"),32);
  try{using(var station=new PrivateStation()){
   string validatorRoot=Path.Combine(output,"validator");var validatorFiles=new Dictionary<string,string>();foreach(var file in ((JObject)JObject.Parse(File.ReadAllText(Path.Combine(validatorRoot,"build.json")))["files"]).Properties())validatorFiles.Add(file.Name,(string)file.Value);
   var validator=new ValidatorLauncher(station,new WorkerImage(validatorRoot,"Validator.exe",validatorFiles),args[7]);var storage=new SpatialWallpaper.Supervision.Broker.StorageBroker(Path.Combine(output,"storage"));storage.Initialized.GetAwaiter().GetResult();
   var gpu=new WindowsGpuSampler(()=>clock.ElapsedMilliseconds);var platform=new WindowsWorkerLauncher(station,distribution,args[7],()=>clock.ElapsedMilliseconds,width,height,gpu,validator,storage);var launcher=new MeasuredLauncher(platform,clock);
   var runtime=new SupervisorRuntime(journal,launcher,new WatchdogPolicy(RuntimeLimits.StartupMs,RuntimeLimits.FrameMs,RuntimeLimits.StopMs,RuntimeLimits.ProcessBytes,RuntimeLimits.CpuWindowMs,RuntimeLimits.CpuBudgetMs,RuntimeLimits.ActualGpuBytes,RuntimeLimits.GpuStaleMs),RuntimeLimits.MaxInstances,RuntimeLimits.GlobalProcessBytes,()=>clock.ElapsedMilliseconds);
   try{
    long begin=clock.ElapsedMilliseconds;var healthy=Start(runtime,new LaunchRequest(installation,args[4],Guid.NewGuid(),1,StartIntent.Manual,args[3],RuntimeLimits.ProcessBytes));Until(()=>healthy.Ready.IsCompleted);healthy.Ready.GetAwaiter().GetResult();long first=clock.ElapsedMilliseconds-begin;var good=launcher.Get(healthy);
    var pixels=new byte[width*height*4];long sequence;Require(good.Worker.CopyFrame(pixels,out sequence),"production-frame-missing");int center=((height/2)*width+width/2)*4;
    Require(pixels[center]>150&&pixels[center+1]<100&&pixels[center+2]<100,"author-geometry-not-in-production-frame:"+pixels[center]+","+pixels[center+1]+","+pixels[center+2]);
    long steadyStart=clock.ElapsedMilliseconds,steadyCpu=good.LastCpu,steadyFrames=good.Worker.ReceivedFrames;if(steady>0)Thread.Sleep(steady);long steadyDuration=clock.ElapsedMilliseconds-steadyStart;steadyCpu=good.LastCpu-steadyCpu;steadyFrames=good.Worker.ReceivedFrames-steadyFrames;
    var bad=Start(runtime,new LaunchRequest(installation,args[6],Guid.NewGuid(),1,StartIntent.Manual,args[5],RuntimeLimits.ProcessBytes));var evil=launcher.Get(bad);Until(()=>bad.Completion.IsCompleted);bad.Completion.GetAwaiter().GetResult();
    Require(bad.Fault==FaultReason.Budget,"bad-Worker-budget-classification-lost");string badLog=System.Text.Encoding.UTF8.GetString(evil.Worker.ReadLog());Require(badLog.Contains("execution-budget"),"bad-Worker-missing-VM-budget-fault");
    long survived=good.LastSequence;Thread.Sleep(1500);Require(!healthy.Completion.IsCompleted&&good.LastSequence>survived+10,"production-healthy-did-not-survive");
    var gpuLimited=new SupervisorRuntime(journal,launcher,new WatchdogPolicy(RuntimeLimits.StartupMs,RuntimeLimits.FrameMs,RuntimeLimits.StopMs,RuntimeLimits.ProcessBytes,RuntimeLimits.CpuWindowMs,RuntimeLimits.CpuBudgetMs,1,RuntimeLimits.GpuStaleMs),1,RuntimeLimits.ProcessBytes,()=>clock.ElapsedMilliseconds);
    SupervisedSession gpuVictim=null;long gpuKillAt=clock.ElapsedMilliseconds;
    try{gpuVictim=Start(gpuLimited,new LaunchRequest(Guid.NewGuid(),args[4],Guid.NewGuid(),1,StartIntent.Manual,args[3],RuntimeLimits.ProcessBytes));Until(()=>gpuVictim.Completion.IsCompleted);gpuVictim.Completion.GetAwaiter().GetResult();Require(gpuVictim.Fault==FaultReason.Budget&&launcher.Get(gpuVictim).GpuPeak>1,"actual-OS-GPU-counter-did-not-enforce-budget");}finally{gpuLimited.Close();Until(()=>gpuLimited.Completion.IsCompleted);}
    long gpuKillDuration=clock.ElapsedMilliseconds-gpuKillAt;Require(!healthy.Completion.IsCompleted,"GPU-budget-stopped-healthy-peer");
    bool denied=false;try{Start(runtime,new LaunchRequest(installation,args[6],Guid.NewGuid(),2,StartIntent.Resume,args[5],RuntimeLimits.ProcessBytes));}catch(InvalidOperationException error){denied=error.Message=="build-quarantined";}Require(denied,"production-actually-resumed-quarantined-build");
    var unresponsive=Start(runtime,new LaunchRequest(Guid.NewGuid(),args[4],Guid.NewGuid(),1,StartIntent.Manual,args[3],RuntimeLimits.ProcessBytes));Until(()=>unresponsive.Ready.IsCompleted);unresponsive.Ready.GetAwaiter().GetResult();var pausedWorker=launcher.Get(unresponsive);JObject pausedEvidence;
    using(var retained=Process.GetProcessById(pausedWorker.ProcessId)){
     Require(RestrictedProcess.ReadCreationFileTime(retained.Handle)==pausedWorker.Worker.CreationFileTime,"paused-Worker-creation-mismatch");
     using(var paused=new OwnedHelperPause(retained)){
      unresponsive.Stop();Until(()=>unresponsive.Completion.IsCompleted);unresponsive.Completion.GetAwaiter().GetResult();pausedEvidence=unresponsive.StopEvidence;
      pausedEvidence["attempt"]=unresponsive.Attempt.ToString("N");pausedEvidence["worker_pid"]=pausedWorker.ProcessId;pausedEvidence["worker_creation_filetime"]=pausedWorker.Worker.CreationFileTime;pausedEvidence["paused_kernel_exit_before_fallback"]=paused.Exited;
      File.WriteAllText(Path.Combine(output,"unresponsive-stop-evidence.json"),pausedEvidence.ToString());
      Require(paused.Exited&&unresponsive.Fault==FaultReason.StopTimeout&&!unresponsive.GracefulStop&&pausedWorker.Kills>0,"paused-Worker-not-stopped-by-production-monitor");
      Require(unresponsive.ForcedAt-unresponsive.StopAcceptedAt>=RuntimeLimits.StopMs,"unresponsive-Worker-lost-full-stop-grace");
      Require(unresponsive.ExitedAt>=unresponsive.ForcedAt&&unresponsive.CleanedAt>=unresponsive.ExitedAt,"unresponsive-Worker-stop-order");
     }
    }
    long stopAt=clock.ElapsedMilliseconds;healthy.Stop();Until(()=>healthy.Completion.IsCompleted);healthy.Completion.GetAwaiter().GetResult();long stopped=clock.ElapsedMilliseconds-stopAt;Require(!healthy.Fault.HasValue,"requested-stop-marked-fault");
    File.WriteAllBytes(Path.Combine(output,"healthy.log"),good.Worker.ReadLog());File.WriteAllBytes(Path.Combine(output,"bad.log"),evil.Worker.ReadLog());
    Require(!Directory.Exists(Path.Combine(args[7],healthy.Attempt.ToString("N")))&&!Directory.Exists(Path.Combine(args[7],bad.Attempt.ToString("N"))),"exited-Worker-runtime-copy-not-reclaimed");Require(good.GpuPeak>0,"missing-actual-OS-GPU-memory");
    results=new JObject{{"width",width},{"height",height},{"first_durable_frame_ms",first},{"normal_stop_completion_ms",stopped},{"healthy_frames",good.LastSequence},{"received_frames",good.Worker.ReceivedFrames},{"max_frame_gap_ms",good.MaxFrameGap},{"steady_ms",steadyDuration},{"steady_cpu_ms",steadyCpu},{"steady_frames",steadyFrames},{"healthy_frames_after_other_fault",good.LastSequence-survived},{"bad_fault",bad.Fault.ToString()},{"healthy_peak_private_bytes",good.Peak},{"bad_peak_private_bytes",evil.Peak},{"healthy_cpu_ms",good.LastCpu-good.FirstCpu},{"healthy_observation_ms",good.LastAt-good.FirstAt},{"author_geometry_center_rgba",new JArray(pixels[center],pixels[center+1],pixels[center+2],pixels[center+3])},{"resume_denied",denied},{"runtime_copies_reclaimed",true},{"healthy_samples",good.Samples},{"bad_samples",evil.Samples}};
    results["stop_evidence"]=healthy.StopEvidence;Require(healthy.GracefulStop&&good.Kills==0,"normal-stop-was-not-graceful");
    results["render_expected_identity"]=new JObject{{"instance",healthy.Request.Instance.ToString("N")},{"attempt",healthy.Attempt.ToString("N")},{"generation",healthy.Request.Generation},{"build_id",healthy.Request.BuildId}};
    results["unresponsive_stop_evidence"]=pausedEvidence;
    results["actual_gpu_peak_bytes"]=good.GpuPeak;
    results["actual_gpu_budget_trip"]=true;results["gpu_budget_case_ms"]=gpuKillDuration;
    var gpuObservation=launcher.Get(gpuVictim);results["gpu_budget_kill_to_kernel_exit_ms"]=gpuObservation.KernelExitAt-gpuObservation.FirstKillAt;
   }finally{runtime.Close();Until(()=>runtime.Completion.IsCompleted);runtime.Completion.GetAwaiter().GetResult();gpu.Close();storage.Close();storage.Completion.GetAwaiter().GetResult();lock(launcher.Workers)foreach(var item in launcher.Workers)File.WriteAllBytes(Path.Combine(output,item.Key.ToString("N")+".log"),item.Value.Worker.ReadLog());}
  }}finally{journal.Close();Until(()=>journal.Completion.IsCompleted);journal.Completion.GetAwaiter().GetResult();}
  File.WriteAllText(Path.Combine(output,"results.json"),results.ToString());Console.WriteLine(results);return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
