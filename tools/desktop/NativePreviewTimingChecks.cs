using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Threading;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Sdk.Capabilities;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Supervision;
using SpatialWallpaper.Supervision.Windows;

static class NativePreviewTimingChecks {
 [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
 [StructLayout(LayoutKind.Sequential)]struct Mouse{public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Explicit,Size=32)]struct Payload{[FieldOffset(0)]public Mouse Mouse;}
 [StructLayout(LayoutKind.Sequential)]struct Input{public uint Type;public Payload Data;}
 [DllImport("user32.dll")]static extern bool SetProcessDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint process);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateWindowEx(uint extended,string className,string title,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr parameter);
 [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr window);
 [DllImport("kernel32.dll",CharSet=CharSet.Unicode)]static extern IntPtr LoadLibraryEx(string path,IntPtr file,uint flags);
 [DllImport("kernel32.dll",CharSet=CharSet.Ansi)]static extern IntPtr GetProcAddress(IntPtr module,string name);
 [DllImport("kernel32.dll")]static extern bool FreeLibrary(IntPtr module);
 [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")]static extern uint SendInput(uint count,Input[] values,int size);
 sealed class NoLaunch:IWorkerLauncher{public IManagedWorker Start(LaunchRequest request,AttemptReceipt attempt,Action<int> process)=>throw new InvalidOperationException("native-preview-timing-never-starts-legacy-worker");}
 sealed class TimingWindow:IDisposable {
  internal IntPtr Handle{get;private set;}
  internal TimingWindow(DesktopDisplay display){Handle=CreateWindowEx(8,"STATIC","Native preview full-resolution timing",0x90000000u,display.X,display.Y,display.Width,display.Height,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);Need(Handle!=IntPtr.Zero,"timing-original-native-parent-created");}
  public void Dispose(){if(Handle!=IntPtr.Zero){Need(DestroyWindow(Handle),"timing-original-native-parent-destroyed");Handle=IntPtr.Zero;}}
 }
 sealed class FocusProbe:IDisposable {
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int Begin(uint thread,IntPtr window);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl,CharSet=CharSet.Unicode)]delegate int Read([Out]System.Text.StringBuilder text,int capacity);
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate int End();
  static readonly List<FocusProbe> retained=new List<FocusProbe>();
  IntPtr module;FileStream image;Read read;End end;
  FocusProbe(){}
  internal static void CheckFailedUnhookRetention(){string path=Path.GetTempFileName();var probe=new FocusProbe();int attempts=0;try{probe.image=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);probe.end=()=>{attempts++;return 0;};bool failed=false;try{probe.Dispose();}catch(InvalidOperationException failure){failed=failure.Message=="native-focus-probe-unhook-failed-module-retained";}Need(failed&&attempts==3&&retained.Contains(probe)&&probe.image!=null&&probe.image.CanRead&&probe.end!=null,"failed-unhook-must-retain-module-owner-and-image-after-bounded-retries");probe.end=()=>1;probe.Dispose();Need(probe.image==null&&probe.end==null&&!retained.Contains(probe),"successful-owner-thread-cleanup-retry-releases-retained-image");Console.WriteLine("PASS failed-unhook-retention-and-successful-retry");}finally{probe.end=()=>1;probe.Dispose();File.Delete(path);}}
  internal FocusProbe(string path,string expected,NativeRenderSurface surface){try{image=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read);Need(Hash(path)==expected,"owned-native-focus-probe-image-hash");Need(GetWindowThreadProcessId(surface.Window,out var process)==surface.RenderThread&&process==surface.ProcessId,"native-focus-probe-original-renderer-thread");module=LoadLibraryEx(path,IntPtr.Zero,0x1100);Need(module!=IntPtr.Zero,"owned-native-focus-probe-loaded-in-QA-process");var begin=Marshal.GetDelegateForFunctionPointer<Begin>(GetProcAddress(module,"BeginFocusTrace"));read=Marshal.GetDelegateForFunctionPointer<Read>(GetProcAddress(module,"ReadFocusTrace"));end=Marshal.GetDelegateForFunctionPointer<End>(GetProcAddress(module,"EndFocusTrace"));Need(begin(surface.RenderThread,surface.Window)==1,"native-focus-probe-bound-only-to-original-owned-render-thread");}catch{Dispose();throw;}}
  internal string Snapshot(){var text=new System.Text.StringBuilder(8192);Need(read(text,text.Capacity)>=0,"native-focus-probe-bounded-snapshot");return text.ToString();}
  public void Dispose(){if(end!=null){bool closed=false;for(int attempt=0;attempt<3&&!closed;attempt++){closed=end()==1;if(!closed)Pump(10);}if(!closed){if(!retained.Contains(this))retained.Add(this);throw new InvalidOperationException("native-focus-probe-unhook-failed-module-retained");}end=null;}if(module!=IntPtr.Zero){if(!FreeLibrary(module)){if(!retained.Contains(this))retained.Add(this);throw new InvalidOperationException("native-focus-probe-module-release-failed-retained");}module=IntPtr.Zero;}image?.Dispose();image=null;retained.Remove(this);}
 }
 static string ledger;static int injector;static long creation;static bool injected;
 static JObject currentRow;
 static void Need(bool condition,string why){if(!condition)throw new InvalidOperationException(why);}
 static void Pump(int milliseconds=5){var timer=Stopwatch.StartNew();do{Application.DoEvents();Thread.Sleep(1);}while(timer.ElapsedMilliseconds<milliseconds);}
 static void Until(Func<bool> condition,string why,int milliseconds=15000){var timer=Stopwatch.StartNew();while(!condition()){Need(timer.ElapsedMilliseconds<milliseconds,why);Pump();}}
 static string Hash(string path){using(var hash=SHA256.Create())return BitConverter.ToString(hash.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
 static double Qpc()=>Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency;
 static void PrepareForeground(TimingWindow parent,NativePreviewTarget target,NativeRenderSurface surface,JObject pose,Point point){
  pose["paused"]=true;target.PublishPose(pose);Until(()=>(bool?)target.State["paused"]==true,"timing-foreground-bootstrap-original-pause-applied");Need(SetCursorPos(point.X,point.Y),"timing-bootstrap-pointer-position");Pump(50);Need(GetCursorPos(out var actual)&&WindowFromPoint(actual)==surface.Window&&target.Verify(),"timing-bootstrap-only-original-owned-child-hit");currentRow["bootstrap_before"]=target.State;
  injected=true;Ledger(4);Need(SendInput(2,new[]{new Input{Data=new Payload{Mouse=new Mouse{Flags=2}}},new Input{Data=new Payload{Mouse=new Mouse{Flags=4}}}},Marshal.SizeOf(typeof(Input)))==2,"timing-paused-owned-window-foreground-bootstrap-pair");Ledger(0);Pump(150);SetForegroundWindow(parent.Handle);Need(GetForegroundWindow()==parent.Handle,"timing-real-parent-foreground-without-input-queue-attachment");var paused=target.State;Need((long?)paused["script_last_input_event"]==null||(long?)paused["script_last_input_event"]==0,"timing-paused-bootstrap-dispatched-zero-author-events");currentRow["bootstrap_after"]=paused;
  pose["paused"]=false;target.PublishPose(pose);Until(()=>{var state=target.State;return (bool?)state["paused"]==false&&((int?)state["script_pid"]==0||(bool?)state["input_ready"]==true);},"timing-original-preview-unpaused-after-owned-bootstrap");
 }
 static void Ledger(uint buttons){string temporary=ledger+".tmp";File.WriteAllText(temporary,new JObject{{"pid",injector},{"creation_filetime",creation},{"buttons",buttons},{"keys",new JArray()},{"injected",injected?new JArray(1):new JArray()}}.ToString());if(File.Exists(ledger))File.Replace(temporary,ledger,null);else File.Move(temporary,ledger);}
 static void Click(IntPtr window,IntPtr parent,Point point){
  bool pointer=GetCursorPos(out var current);currentRow["input_hit"]=new JObject{{"pointer_available",pointer},{"pointer",new JArray(current.X,current.Y)},{"expected_point",new JArray(point.X,point.Y)},{"hit_window",WindowFromPoint(current).ToInt64()},{"expected_window",window.ToInt64()},{"foreground",GetForegroundWindow().ToInt64()},{"expected_parent",parent.ToInt64()}};
  Need(pointer&&WindowFromPoint(current)==window&&GetForegroundWindow()==parent,"timing-real-owned-preview-hit-and-foreground");
  Need(new[]{1,16,17,18,91,92}.All(key=>(GetAsyncKeyState(key)&0x8000)==0),"timing-real-click-has-no-preheld-primary-or-modifiers");
  injected=true;Ledger(4);Need(SendInput(2,new[]{new Input{Data=new Payload{Mouse=new Mouse{Flags=2}}},new Input{Data=new Payload{Mouse=new Mouse{Flags=4}}}},Marshal.SizeOf(typeof(Input)))==2,"timing-real-primary-pair-injected");Ledger(0);
 }
 [STAThread]static int Main(string[] args){
  if(args.Length==1&&args[0]=="--focus-probe-cleanup-check"){FocusProbe.CheckFailedUnhookRetention();return 0;}
  if(Console.ReadLine()!="RUN")return 2;string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);ledger=Path.Combine(root,"injection-owed.json");using(var self=PinnedProcessOwner.PinCurrent()){injector=self.Id;creation=self.CreationFileTime;}
  var report=new JObject{{"status","failed"},{"scope","Full-resolution original Showcase native preview with real OS primary pairs at4Hz; fixed/moving no-bridge/bridge/script-crash; not formal Launch, physical input or RC"},{"cases",new JArray()}};
  NativePreviewTarget target=null;var pins=new List<Process>();GetCursorPos(out var originalCursor);
  try{
   Need((GetAsyncKeyState(1)&0x8000)==0,"timing-no-preheld-primary");SetProcessDpiAwarenessContext(new IntPtr(-4));SetThreadDpiAwarenessContext(new IntPtr(-4));
   int seconds=int.Parse(args[5]);Need(seconds==10||seconds==60,"timing-uniform-original-duration");Need(int.Parse(args[6])==-1&&int.Parse(args[7])==-1,"preview-timing-requires-automatic-product-presentation");
   var displays=WindowsDesktopWindow.Displays(true);Need(displays.Length==1,"timing-one-real-target");var display=displays[0];var crop=new[]{display.X,display.Y,display.Width,display.Height};
   var inventory=JObject.Parse(File.ReadAllText(Path.Combine(args[3],"build.json")));var definition=JObject.Parse(File.ReadAllText(args[4]));
   using(var broker=new InputBroker())using(var form=new TimingWindow(display)){
    var journal=new JournalActor(Path.Combine(root,"journal"),RuntimeLimits.JournalPending);journal.Initialized.GetAwaiter().GetResult();var clock=Stopwatch.StartNew();
    var policy=new WatchdogPolicy(RuntimeLimits.StartupMs,RuntimeLimits.FrameMs,RuntimeLimits.StopMs,RuntimeLimits.ProcessBytes,RuntimeLimits.CpuWindowMs,RuntimeLimits.CpuBudgetMs,RuntimeLimits.ActualGpuBytes,RuntimeLimits.GpuStaleMs);
    var runtime=new SupervisorRuntime(journal,new NoLaunch(),policy,2,RuntimeLimits.GlobalProcessBytes,()=>clock.ElapsedMilliseconds);
    try{string descriptor=Path.Combine(root,"endpoint.json");var serviceType=typeof(NativeInputRegistry).Assembly.GetType("SpatialWallpaper.Supervision.Host+Service",true);
     using(var service=(IDisposable)serviceType.GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single().Invoke(new object[]{runtime,journal,descriptor,null,null,null,broker})){
      SetForegroundWindow(form.Handle);Pump(100);
      bool inputDiagnostic=Array.IndexOf(args,"--input-diagnostic")>=0;
      foreach(bool moving in inputDiagnostic?new[]{false}:new[]{false,true})foreach(string mode in inputDiagnostic?new[]{"bridge"}:new[]{"no-bridge","bridge","script-fault"}){
       string name=(moving?"moving-":"fixed-")+mode,folder=Path.Combine(root,name);Directory.CreateDirectory(folder);var row=new JObject{{"name",name},{"mode",mode},{"moving_eye",moving},{"input_load_hz",4}};currentRow=row;((JArray)report["cases"]).Add(row);
       var pose=new JObject{{"display_id",display.Id},{"width",.53},{"height",.53*display.Height/display.Width},{"keep_height",true},{"eye",new JArray(0,0,-.67)},{"fresh",false},{"paused",false},{"input_region",new JArray(crop)},{"presentation",new JObject{{"numerator",display.RefreshNumerator},{"denominator",display.RefreshDenominator}}}};
       var config=new JObject{{"artwork","showcase"},{"session",Guid.NewGuid().ToString("N")},{"generation",1},{"desktop",false},{"preview_ui",true},{"diagnostics",true},{"input_trace",Array.IndexOf(args,"--trace-input")>=0},{"pose",pose}};
       if(mode!="no-bridge"){
        config["input"]=new JObject{{"descriptor",descriptor},{"region",new JArray(crop)}};
        config["script"]=new JObject{{"node_paths",new JArray("InteriorContent_A/ReplaceableSlot","@scene/RealtimeSculptureKey")},{"helper",Path.GetFullPath(args[2])},{"helper_sha256",Hash(args[2])},{"bootstrap_cache",Path.Combine(folder,"bootstrap")},{"launch",new JObject{{"worker_image",Path.GetFullPath(args[3])},{"worker_files",inventory["runtime_files"]},{"attempts",Path.Combine(folder,"attempts")},{"attempt",Guid.NewGuid().ToString("N")},{"program",Path.GetFullPath(args[4])},{"program_sha256",Hash(args[4])},{"build",definition["build_id"]}}}};
       }
       FocusProbe focusProbe=null;try{
        target=new NativePreviewTarget(args[0],Path.Combine(folder,"renderer"),form.Handle,config);Until(()=>target.Ready.IsCompleted,"timing-native-preview-ready",35000);var surface=target.Ready.GetAwaiter().GetResult();target.AttachReady(crop);Until(target.Verify,"timing-preview-physical-placement");
        var renderer=Process.GetProcessById(surface.ProcessId);Need(PinnedProcessOwner.ReadCreationFileTime(renderer.Handle)==surface.CreationFileTime,"timing-original-renderer-pin");pins.Add(renderer);JObject state=null;
        Action drive=()=>{if(focusProbe!=null)row["native_focus_trace"]=focusProbe.Snapshot();Need(!File.Exists(Path.Combine(root,"abort")),"timing-parent-aborted");state=target.State;row["last_state"]=state.DeepClone();GetCursorPos(out var cursor);var foregroundWindow=GetForegroundWindow();GetWindowThreadProcessId(foregroundWindow,out var foregroundProcess);row["last_focus"]=new JObject{{"foreground",foregroundWindow.ToInt64()},{"foreground_pid",foregroundProcess},{"parent",form.Handle.ToInt64()},{"pointer",new JArray(cursor.X,cursor.Y)},{"hit",WindowFromPoint(cursor).ToInt64()},{"child",surface.Window.ToInt64()}};Need(target.Error==null&&target.Verify(),"timing-native-preview-owner-failed:"+target.Error);Need((int?)state["render_errors"]==0,"timing-original-scene-render-error");if(mode=="bridge")Need(state["script_error"]?.Type!=JTokenType.String&&state["input_error"]?.Type!=JTokenType.String,"timing-healthy-input-or-script-faulted:"+state["script_error"]+":"+state["input_error"]);};
        Until(()=>{drive();return (long?)state["frame"]>=90&&(bool?)state["paused"]==false&&(int?)state["preview_ui"]?["rendered_width"]==display.Width&&(int?)state["preview_ui"]?["rendered_height"]==display.Height&&(mode=="no-bridge"||(long?)state["script_batches"]>=8&&(bool?)state["input_ready"]==true);},"timing-full-original-scene-input-warmup");
        Need((int?)state["v_sync_count"]==display.NativeVSyncCount,"timing-preview-automatic-per-target-presentation");if(display.NativeVSyncCount>0)Need((int?)state["max_queued_frames"]==1,"timing-preview-original-queue-one-policy");row["renderer_pid"]=surface.ProcessId;row["renderer_creation_filetime"]=surface.CreationFileTime;
        File.WriteAllText(Path.Combine(folder,"ready.json"),new JObject{{"name",name},{"pid",surface.ProcessId},{"state",state},{"measurement_seconds",seconds},{"display",new JObject{{"x",display.X},{"y",display.Y},{"width",display.Width},{"height",display.Height}}}}.ToString());
        Until(()=>{drive();return File.Exists(Path.Combine(folder,"go"));},"timing-parent-capture-go",60000);
        var point=new Point{X=display.X+display.Width/2,Y=display.Y+display.Height/2};PrepareForeground(form,target,surface,pose,point);Pump(50);Click(surface.Window,form.Handle,point);
        if(mode!="no-bridge")Until(()=>{drive();return (long?)state["script_last_input_event"]==2;},"timing-real-startup-click-confirmed-by-author");else{Pump(100);drive();}
        if(mode!="no-bridge"){
         var script=Process.GetProcessById((int)state["script_pid"]);Need(PinnedProcessOwner.ReadCreationFileTime(script.Handle)==(long)state["script_creation"],"timing-original-script-pin");pins.Add(script);
         if(mode=="script-fault"){Need(state["script_error"]?.Type==JTokenType.Null,"timing-immediate-script-healthy-before-owned-crash");row["crash_original_pid"]=script.Id;row["crash_original_creation"]=state["script_creation"];long faultFrame=(long)state["frame"];script.Kill();Need(script.WaitForExit(5000),"timing-injected-original-script-kernel-exit");Until(()=>{drive();return state["script_error"]?.Type==JTokenType.String&&(long?)state["frame"]>faultFrame+5;},"timing-owned-crash-native-render-continues");}
        }
        drive();int probeIndex=Array.IndexOf(args,"--native-focus-probe");if(probeIndex>=0){focusProbe=new FocusProbe(args[probeIndex+1],args[probeIndex+2],surface);row["native_focus_trace_phase"]="after-healthy-startup-ack-before-measurement";}target.ResetFocusTrace();row["before"]=state.DeepClone();row["measurement_before"]=state.DeepClone();row["start_qpc_ms"]=Qpc();var sample=Stopwatch.StartNew();double nextClick=0,min=1,max=-1;int inputEdges=0;long lastFrame=0;var history=new JArray();
        while(sample.ElapsedMilliseconds<seconds*1000){
         double eye=moving?.08*Math.Sin(sample.Elapsed.TotalSeconds*2*Math.PI*.15):0;pose["eye"]=new JArray(eye,0,-.67);target.PublishPose(pose);drive();double actual=(double)state["eye"][0];min=Math.Min(min,actual);max=Math.Max(max,actual);
         if(nextClick<seconds*1000&&sample.Elapsed.TotalMilliseconds>=nextClick){Click(surface.Window,form.Handle,point);inputEdges+=2;nextClick+=250;}
         if((long)state["frame"]>lastFrame+5){lastFrame=(long)state["frame"];history.Add(new JObject{{"qpc_ms",Qpc()},{"frame",state["frame"]},{"eye_x",actual},{"script_batches",state["script_batches"]},{"acknowledged_input_event",state["script_last_input_event"]},{"input_queue",state["input_script_queue"]}});}Pump(5);
        }
        row["end_qpc_ms"]=Qpc();row["after"]=state.DeepClone();row["actual_eye_min"]=min;row["actual_eye_max"]=max;row["history"]=history;row["injected_measurement_edges"]=inputEdges;
        Need(inputEdges==seconds*8,"timing-uniform-real-click-load-complete");Need((long)state["frame"]>(long)row["measurement_before"]["frame"]+seconds*30,"timing-native-render-progress-under-input-load");if(moving)Need(min<-.07&&max>.07,"timing-actual-moving-eye-not-presented");
        if(mode=="bridge"){Until(()=>{drive();return (long?)state["script_last_input_event"]==inputEdges+2;},"timing-all-real-measured-input-edges-acknowledged-once");Need((long)state["script_batches"]>(long)row["measurement_before"]["script_batches"]+100,"timing-healthy-bridge-progress-under-real-input");}if(mode=="script-fault")Need((long?)state["script_last_input_event"]==2,"timing-faulted-author-received-no-late-input");row["input_delivery_after_drain"]=state.DeepClone();
        Need(JToken.DeepEquals(row["before"]["script_node_values"],state["script_node_values"]),"timing-same-value-program-cannot-change-original-scene");pose["eye"]=new JArray(moving?.08:0,0,-.67);target.PublishPose(pose);Pump(150);drive();row["image_state"]=state.DeepClone();File.WriteAllText(Path.Combine(folder,"sample.json"),row.ToString());Until(()=>{drive();return File.Exists(Path.Combine(folder,"captured"));},"timing-parent-image-capture");
       }catch(Exception primary){if(focusProbe!=null){Pump(50);row["native_focus_trace"]=focusProbe.Snapshot();}row["parent_focus_stack"]=target?.FirstParentFocusStack;row["parent_focus_time"]=target?.FirstParentFocusTime;row["primary_error"]=primary.ToString();throw;}finally{
        try{focusProbe?.Dispose();}catch(Exception failure){row["focus_probe_cleanup_error"]=failure.ToString();}
        if(target!=null){var close=target.CloseAsync();Until(()=>close.IsCompleted,"timing-preview-close",25000);close.GetAwaiter().GetResult();Need(target.ResourcesReleased,"timing-preview-original-owner-resources-released");target=null;}
        try{foreach(var pin in pins)Need(pin.WaitForExit(5000),"timing-original-owned-kernel-leftover");row["original_pins_exited"]=pins.Count;}finally{foreach(var pin in pins)pin.Dispose();pins.Clear();}
       }
       Need(row["focus_probe_cleanup_error"]==null,"native-focus-probe-cleanup-proven");File.WriteAllText(Path.Combine(root,"progress.json"),report.ToString());
      }
     }
    }finally{runtime.Close();runtime.Completion.GetAwaiter().GetResult();journal.Close();journal.Completion.GetAwaiter().GetResult();}
   }
   report["status"]="controlled-native-bridge-timing-phases-complete-not-RC";return 0;
  }catch(Exception failure){report["error"]=failure.ToString();Console.Error.WriteLine(failure);return 1;}
  finally{if(target!=null)try{var close=target.CloseAsync();Until(()=>close.IsCompleted,"timing-failure-owned-preview-close",25000);close.GetAwaiter().GetResult();}catch(Exception failure){report["cleanup_error"]=failure.ToString();}foreach(var pin in pins)pin.Dispose();SetCursorPos(originalCursor.X,originalCursor.Y);File.WriteAllText(Path.Combine(root,"report.json"),report.ToString());}
 }
}
