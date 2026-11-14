using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Reflection;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Sdk.Native.Windows;
using SpatialWallpaper.Sdk.Capabilities;
using SpatialWallpaper.Supervision;
using SpatialWallpaper.Supervision.Windows;

static class NativeInputProcessChecks {
 static int checks;
 static void Need(bool value,string why){if(!value)throw new Exception(why);checks++;}
 static void Until(Func<bool> done,string why,int ms=12000){var c=Stopwatch.StartNew();while(!done()){if(c.ElapsedMilliseconds>ms)throw new TimeoutException(why);Thread.Sleep(5);}}
 sealed class NoLaunch:IWorkerLauncher {public IManagedWorker Start(LaunchRequest r,AttemptReceipt a,Action<int> p){throw new Exception("no-legacy-renderer");}}
 static int OwnedHost(string root){
  using(var broker=new InputBroker()){
   var journal=new JournalActor(Path.Combine(root,"child-journal"),RuntimeLimits.JournalPending);journal.Initialized.GetAwaiter().GetResult();var clock=Stopwatch.StartNew();var policy=new WatchdogPolicy(RuntimeLimits.StartupMs,RuntimeLimits.FrameMs,RuntimeLimits.StopMs,RuntimeLimits.ProcessBytes,RuntimeLimits.CpuWindowMs,RuntimeLimits.CpuBudgetMs,RuntimeLimits.ActualGpuBytes,RuntimeLimits.GpuStaleMs);var runtime=new SupervisorRuntime(journal,new NoLaunch(),policy,2,RuntimeLimits.GlobalProcessBytes,()=>clock.ElapsedMilliseconds);
   try{var type=typeof(NativeInputRegistry).Assembly.GetType("SpatialWallpaper.Supervision.Host+Service",true);using(var service=(IDisposable)type.GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single().Invoke(new object[]{runtime,journal,Path.Combine(root,"child-endpoint.json"),null,null,null,broker})){Until(()=>File.Exists(Path.Combine(root,"child-stop")),"owned-host-stop",60000);}}
   finally{runtime.Close();runtime.Completion.GetAwaiter().GetResult();journal.Close();journal.Completion.GetAwaiter().GetResult();}
  }return 0;
 }
 static int Main(string[] args){
  if(args.Length==2&&args[0]=="--owned-host")return OwnedHost(args[1]);
  string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);var report=new JObject{{"scope","actual native target, trusted input role and authenticated original Supervisor; no injected OS focus or script delivery claim"}};
  NativeDesktopTarget desktop=null;NativeInputProcess input=null;Process inputRole=null,childHost=null;var helpers=new List<Process>();SupervisorRuntime runtime=null;JournalActor journal=null;
  try{using(var broker=new InputBroker())using(var self=PinnedProcessOwner.PinCurrent()){
   journal=new JournalActor(Path.Combine(root,"journal"),RuntimeLimits.JournalPending);journal.Initialized.GetAwaiter().GetResult();var clock=Stopwatch.StartNew();var policy=new WatchdogPolicy(RuntimeLimits.StartupMs,RuntimeLimits.FrameMs,RuntimeLimits.StopMs,RuntimeLimits.ProcessBytes,RuntimeLimits.CpuWindowMs,RuntimeLimits.CpuBudgetMs,RuntimeLimits.ActualGpuBytes,RuntimeLimits.GpuStaleMs);
   runtime=new SupervisorRuntime(journal,new NoLaunch(),policy,2,RuntimeLimits.GlobalProcessBytes,()=>clock.ElapsedMilliseconds);
   var serviceType=typeof(NativeInputRegistry).Assembly.GetType("SpatialWallpaper.Supervision.Host+Service",true);string descriptor=Path.Combine(root,"endpoint.json");
   using(var service=(IDisposable)serviceType.GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single().Invoke(new object[]{runtime,journal,descriptor,null,null,null,broker})){
    bool hostExit=args.Length>3&&args[3]=="--host-exit-on-unregister";
    if(hostExit){childHost=Process.Start(new ProcessStartInfo(typeof(NativeInputProcessChecks).Assembly.Location,"--owned-host \""+root+"\""){UseShellExecute=false,CreateNoWindow=true});Need(childHost.Handle!=IntPtr.Zero,"original-host-kernel-handle");descriptor=Path.Combine(root,"child-endpoint.json");Until(()=>File.Exists(descriptor),"owned-host-descriptor");}
    var display=WindowsDesktopWindow.Displays()[0];var pose=new JObject{{"display_id",display.Id},{"width",.53},{"height",.53*display.Height/display.Width},{"keep_height",true},{"eye",new JArray(0,0,-.67)},{"fresh",false}};
    var config=new JObject{{"artwork","showcase"},{"session",Guid.NewGuid().ToString("N")},{"generation",1},{"desktop",true},{"pose",pose}};
    desktop=new NativeDesktopTarget(args[0],Path.Combine(root,"render"),display.Id,config);Until(()=>desktop.Ready.IsCompleted,"renderer-ready",35000);desktop.AttachReady();var render=desktop.Ready.GetAwaiter().GetResult();Until(()=>(int?)desktop.State["screen_width"]==display.Width,"render-resize");
    using(var pin=render.Pin()){
     if(!hostExit&&!(args.Length>3&&args[3]=="--registration-reply-loss")){
      var connection=new SupervisorManagerConnection(descriptor);var prior=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);connection.RegisterNativeOrderedInput(prior,false,pin).GetAwaiter().GetResult();
      try{using(var duplicate=new NativeInputProcess(args[2],Path.Combine(root,"duplicate"),descriptor,prior.Instance,prior.Generation,pin,render.Window,render.RenderThread,new[]{display.X,display.Y,display.Width,display.Height})){
       Need(duplicate.Completion.Wait(10000)&&duplicate.Error!=null&&duplicate.ResourcesReleased,"duplicate-input-owner-not-rejected-cleanly");Need(duplicate.Identity.Attempt!=prior.Attempt,"input-owner-reused-external-attempt");
       var registry=(NativeInputRegistry)serviceType.GetField("nativeInputs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(service);Need(registry.Find(prior,false,pin.Id,pin.CreationFileTime)!=null,"compensation-removed-previous-healthy-owner");
      }}finally{connection.UnregisterNativeInput(prior).GetAwaiter().GetResult();}
     }
     input=new NativeInputProcess(args[2],Path.Combine(root,"input"),descriptor,Guid.NewGuid(),1,pin,render.Window,render.RenderThread,new[]{display.X,display.Y,display.Width,display.Height});
     if(args.Length>3&&args[3]=="--registration-reply-loss"){
      Need(input.Completion.Wait(10000)&&input.Error!=null&&input.ResourcesReleased,"uncertain-registration-cleanup");
      var registry=(NativeInputRegistry)serviceType.GetField("nativeInputs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(service);
      Need(registry.Find(input.Identity,false,pin.Id,pin.CreationFileTime)==null,"lost-reply-retained-registration");
      using(var a=broker.Register(new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),true))using(var b=broker.Register(new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),true))Need(true,"lost-reply-broker-capacity-not-returned");
      Need(input.ProcessId==0,"lost-registration-launched-input-role");report["server_registered_then_dropped_reply_compensated"]=true;
     }else{
     Until(()=>input.Ready||input.Error!=null,"native-input-role-ready");Need(input.Error==null&&input.Ready,"trusted-input-role-error:"+input.Error);
     Need(input.ProcessId>0&&input.ProcessId!=self.Id&&input.ProcessId!=render.ProcessId,"input-role-process-isolation");
     inputRole=Process.GetProcessById(input.ProcessId);Need(PinnedProcessOwner.ReadCreationFileTime(inputRole.Handle)==input.CreationFileTime,"input-role-original-kernel-pin");
     var first=input.Latest;Need(first!=null,"ordered-snapshot-not-delivered");ulong epoch=BitConverter.ToUInt64(first.Snapshot,8);input.Accept(first.Cursor);
     foreach(int pid in input.HelperProcessIds){var p=Process.GetProcessById(pid);Need(p.Handle!=IntPtr.Zero,"helper-kernel-handle");helpers.Add(p);}Need(helpers.Count==2,"input-role-helper-count");
     input.SetEnabled(false);Until(()=>!input.Ready&&input.Latest!=null&&BitConverter.ToUInt64(input.Latest.Snapshot,8)>epoch,"pause-epoch-revoke");Need(BitConverter.ToUInt32(input.Latest.Snapshot,4)==0,"pause-kept-focus");
     foreach(var p in helpers)Need(p.WaitForExit(5000),"paused-input-helper-still-live");
     input.SetEnabled(true);Until(()=>input.Ready||input.Error!=null,"input-role-resume");Need(input.Error==null&&input.Ready,"resumed-role-error:"+input.Error);
     foreach(int pid in input.HelperProcessIds){var p=Process.GetProcessById(pid);Need(p.Handle!=IntPtr.Zero,"resumed-helper-kernel-handle");helpers.Add(p);}
     ulong rapidEpoch=BitConverter.ToUInt64(input.Latest.Snapshot,8);var handoff=typeof(NativeInputProcess).GetField("gate",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(input);
     lock(handoff){input.SetEnabled(false);input.SetEnabled(true);}
     Until(()=>input.Ready&&input.Latest!=null&&BitConverter.ToUInt64(input.Latest.Snapshot,8)>rapidEpoch,"rapid-pause-resume-lost-revocation");Need(input.Ready,"rapid-resume-not-ready");
     foreach(int pid in input.HelperProcessIds){var p=Process.GetProcessById(pid);Need(p.Handle!=IntPtr.Zero,"rapid-resume-helper-kernel-handle");helpers.Add(p);}
     bool late=args.Length>3&&args[3]=="--late-cleanup-fault",fault=late||args.Length>3&&args[3]=="--cleanup-fault";
     input.Stop();if(late){Until(()=>input.CleanupError!=null||input.Completion.IsCompleted,"late-cleanup-warning",18000);Need(!input.Completion.IsCompleted&&!input.ResourcesReleased&&input.FirstCleanupError!=null,"late-cleanup-owner-retired-before-kernel-exit");long before=(long)desktop.State["frame"];Thread.Sleep(100);Need((long)desktop.State["frame"]>before,"native-frames-blocked-by-input-cleanup");}
     Need(input.Completion.Wait(25000)&&input.ResourcesReleased,"input-role-cleanup-unproven:"+input.CleanupError);Need(fault?input.Error!=null:input.Error==null,"input-role-stop-error:"+input.Error);
     if(hostExit){Need(childHost.WaitForExit(5000)&&input.FirstCleanupError!=null&&input.FirstCleanupError.StartsWith("native-input-unregister:",StringComparison.Ordinal)&&input.CleanupError==null,"original-host-exit-did-not-complete-unregister-cleanup");report["unregister_reply_loss_then_original_host_exit"]=true;}
     if(fault){var evidence=JObject.Parse(File.ReadAllText(Path.Combine(root,"input","cleanup-fault.json")));report["controlled_cleanup_fault_and_eof_retry"]=evidence;Need((bool?)evidence["first_failed"]==true&&(bool?)evidence["released_after_eof_retry"]==true&&(bool?)evidence["rescue_used"]==false,"helper-eof-cleanup-retry-not-observed-without-test-rescue");}
     foreach(var p in helpers)Need(p.WaitForExit(5000),"input-helper-exit-unproven");report["owned_helpers_exited"]=helpers.Count;
     if(late){var registry=(NativeInputRegistry)serviceType.GetField("nativeInputs",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(service);Need(input.FirstCleanupError!=null&&input.CleanupError==null&&registry.Find(input.Identity,false,pin.Id,pin.CreationFileTime)==null,"late-cleanup-did-not-finalize-registration");report["late_cleanup_first_error_retained"]=input.FirstCleanupError;}
     }
    }
    var close=desktop.CloseAsync();Need(close.Wait(17000)&&desktop.ResourcesReleased,"native-target-cleanup");desktop=null;
   }
  }report["status"]="passed-lifecycle-only";report["checks"]=checks;return 0;
  }catch(Exception e){report["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}
  finally{
   try{if(input!=null){input.Stop();if(!input.Completion.Wait(15000)||!input.ResourcesReleased)throw new Exception("input-cleanup:"+input.CleanupError);}}catch(Exception e){report["cleanup_error"]=e.ToString();}
   try{if(desktop!=null){var close=desktop.CloseAsync();if(!close.Wait(17000)||!desktop.ResourcesReleased)throw new Exception("renderer-cleanup");}}catch(Exception e){report["renderer_cleanup_error"]=e.ToString();}
   if(inputRole!=null){if(!inputRole.WaitForExit(25000))report["input_role_kernel_exit_unproven"]=true;inputRole.Dispose();}if(childHost!=null){File.WriteAllText(Path.Combine(root,"child-stop"),"stop");if(!childHost.WaitForExit(5000))report["owned_host_exit_unproven"]=true;childHost.Dispose();}foreach(var p in helpers)p.Dispose();if(runtime!=null){runtime.Close();runtime.Completion.GetAwaiter().GetResult();}if(journal!=null){journal.Close();journal.Completion.GetAwaiter().GetResult();}
   File.WriteAllText(Path.Combine(root,"report.json"),report.ToString());
  }
 }
}
