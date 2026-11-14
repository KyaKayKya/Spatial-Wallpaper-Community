using System;
using System.Diagnostics;
using System.Collections.Generic;
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
using SpatialWallpaper.Supervision.Interactive;

static class NativePreviewWindowChecks {
 [StructLayout(LayoutKind.Sequential)]struct Point {public int X,Y;}
 [StructLayout(LayoutKind.Sequential)]struct Bounds {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]struct GuiState {public uint Size,Flags;public IntPtr Active,Focus,Capture,MenuOwner,MoveSize,Caret;public Bounds CaretRect;}
 [StructLayout(LayoutKind.Sequential)]struct Mouse {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Sequential)]struct Key {public ushort Code,Scan;public uint Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Explicit,Size=32)]struct Payload {[FieldOffset(0)]public Mouse Mouse;[FieldOffset(0)]public Key Key;}
 [StructLayout(LayoutKind.Sequential)]struct Input {public uint Type;public Payload Data;}
 [DllImport("user32.dll")]static extern IntPtr SetParent(IntPtr child,IntPtr parent);
 [DllImport("user32.dll")]static extern int GetWindowLong(IntPtr window,int index);
 [DllImport("user32.dll")]static extern int SetWindowLong(IntPtr window,int index,int value);
 [DllImport("user32.dll")]static extern bool MoveWindow(IntPtr window,int x,int y,int width,int height,bool repaint);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool GetGUIThreadInfo(uint thread,ref GuiState state);
 [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("user32.dll")]static extern IntPtr GetShellWindow();
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Bounds bounds);
 [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
 [DllImport("user32.dll")]static extern uint SendInput(uint count,Input[] values,int size);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenThread(uint access,bool inherit,uint id);
 [DllImport("kernel32.dll")]static extern uint GetProcessIdOfThread(IntPtr thread);
 [DllImport("kernel32.dll")]static extern uint SuspendThread(IntPtr thread);
 [DllImport("kernel32.dll")]static extern uint ResumeThread(IntPtr thread);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll")]static extern bool GetExitCodeThread(IntPtr thread,out uint code);
 static readonly HashSet<ushort> keysOwed=new HashSet<ushort>(),injected=new HashSet<ushort>();static uint buttonsOwed;static bool partialClick,hangAfterDown;static string inputLedger;static long injectorCreation;
 static void SaveOwed(){string temporary=inputLedger+".tmp";File.WriteAllText(temporary,new JObject{{"pid",Process.GetCurrentProcess().Id},{"creation_filetime",injectorCreation},{"buttons",buttonsOwed},{"keys",new JArray(keysOwed.OrderBy(code=>code))},{"injected",new JArray(injected.OrderBy(code=>code))}}.ToString());if(File.Exists(inputLedger))File.Replace(temporary,inputLedger,null);else File.Move(temporary,inputLedger);}
 static int checks;
 static JObject NativeFocus(IntPtr window){IntPtr root=GetAncestor(window,2),foreground=GetForegroundWindow();uint ownThread=GetWindowThreadProcessId(window,out uint ownPid),rootThread=GetWindowThreadProcessId(root,out uint rootPid),foregroundThread=GetWindowThreadProcessId(foreground,out uint foregroundPid);var own=new GuiState{Size=(uint)Marshal.SizeOf(typeof(GuiState))};var parent=own;bool ownOk=GetGUIThreadInfo(ownThread,ref own),rootOk=GetGUIThreadInfo(rootThread,ref parent);return new JObject{{"window",window.ToInt64()},{"window_pid",ownPid},{"root",root.ToInt64()},{"root_pid",rootPid},{"foreground",foreground.ToInt64()},{"foreground_pid",foregroundPid},{"foreground_thread",foregroundThread},{"own_gui_ok",ownOk},{"own_active",own.Active.ToInt64()},{"own_focus",own.Focus.ToInt64()},{"own_flags",own.Flags},{"root_gui_ok",rootOk},{"root_active",parent.Active.ToInt64()},{"root_focus",parent.Focus.ToInt64()},{"root_flags",parent.Flags},{"window_style",GetWindowLong(window,-16)}};}
 sealed class ScriptSuspension:IDisposable {
  readonly List<IntPtr> threads=new List<IntPtr>();
  public ScriptSuspension(Process script,long creation){
   Need(PinnedProcessOwner.ReadCreationFileTime(script.Handle)==creation,"backpressure-original-script-pin");
   try{foreach(ProcessThread thread in script.Threads){IntPtr handle=OpenThread(0x802,false,(uint)thread.Id);if(handle==IntPtr.Zero)throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());if(GetProcessIdOfThread(handle)!=(uint)script.Id||SuspendThread(handle)==uint.MaxValue){CloseHandle(handle);throw new Exception("owned-script-thread-suspend-failed");}threads.Add(handle);}Need(threads.Count>0,"backpressure-script-threads-retained");}catch{Dispose();throw;}
  }
  public void Dispose(){bool resumed=true;foreach(IntPtr thread in threads){bool observed=GetExitCodeThread(thread,out uint code);resumed&=observed;if(observed&&code==259)resumed&=ResumeThread(thread)!=uint.MaxValue;resumed&=CloseHandle(thread);}threads.Clear();Need(resumed,"backpressure-original-threads-resumed-or-observed-exited-and-closed");}
 }
 sealed class NoLaunch:IWorkerLauncher {public IManagedWorker Start(LaunchRequest request,AttemptReceipt attempt,Action<int> process)=>throw new InvalidOperationException("native-preview-never-starts-legacy-worker");}
 sealed class BrokerHold:IDisposable {
  readonly ManualResetEventSlim acquired=new ManualResetEventSlim(),release=new ManualResetEventSlim();readonly Thread thread;public volatile bool Held;public long ElapsedMs;
  public BrokerHold(InputBroker broker,int maximumMs){var ownedGate=typeof(InputBroker).GetField("sync",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(broker);thread=new Thread(()=>{lock(ownedGate){var timer=Stopwatch.StartNew();Held=true;acquired.Set();try{release.Wait(maximumMs);}finally{ElapsedMs=timer.ElapsedMilliseconds;Held=false;}}}){IsBackground=true};thread.Start();if(!acquired.Wait(1000))throw new TimeoutException("owned-broker-lock-acquisition");}
  public void Dispose(){release.Set();if(!thread.Join(5000))throw new TimeoutException("owned-broker-lock-release");acquired.Dispose();release.Dispose();}
 }
 static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
 static void Need(bool value,string why){if(!value)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
 static void Pump(int milliseconds){var timer=Stopwatch.StartNew();do{Application.DoEvents();Thread.Sleep(1);}while(timer.ElapsedMilliseconds<milliseconds);}
 static void Until(Func<bool> condition,string why,int milliseconds=10000){var timer=Stopwatch.StartNew();while(!condition()){if(timer.ElapsedMilliseconds>=milliseconds)throw new Exception(why);Pump(5);}Need(true,why);}
 static void Click(int x,int y){Need(SetCursorPos(x,y),"position-owned-preview-pointer");Pump(30);buttonsOwed|=4;injected.Add(1);SaveOwed();uint sent=SendInput(partialClick||hangAfterDown?1u:2u,new[]{new Input{Data=new Payload{Mouse=new Mouse{Flags=2}}},new Input{Data=new Payload{Mouse=new Mouse{Flags=4}}}},Marshal.SizeOf(typeof(Input)));if(hangAfterDown){Need(sent==1,"QA-owned-primary-down");MouseEdge(8);foreach(ushort code in new ushort[]{16,32,37,38,39,40})KeyEdge(code,false);Thread.Sleep(Timeout.Infinite);}Need(sent==2,"OS-primary-pair-injected");buttonsOwed&=~4u;SaveOwed();Pump(150);}
 static void MouseEdge(uint flags,uint data=0){if(flags==2){buttonsOwed|=4;injected.Add(1);}if(flags==8){buttonsOwed|=16;injected.Add(2);}SaveOwed();Need(SendInput(1,new[]{new Input{Data=new Payload{Mouse=new Mouse{Flags=flags,Data=data}}}},Marshal.SizeOf(typeof(Input)))==1,"OS-preview-mouse-edge");if(flags==4||flags==16)buttonsOwed&=~flags;SaveOwed();}
 static void KeyEdge(ushort code,bool up){if(!up){keysOwed.Add(code);injected.Add(code);}SaveOwed();Need(SendInput(1,new[]{new Input{Type=1,Data=new Payload{Key=new Key{Code=code,Flags=up?2u:0u}}}},Marshal.SizeOf(typeof(Input)))==1,"OS-preview-key-edge");if(up)keysOwed.Remove(code);SaveOwed();}
 static void KeyBurst(int count){keysOwed.Add(32);injected.Add(32);SaveOwed();var edges=Enumerable.Range(0,count).Select(index=>new Input{Type=1,Data=new Payload{Key=new Key{Code=32,Flags=(index&1)==0?0u:2u}}}).ToArray();Need(SendInput((uint)edges.Length,edges,Marshal.SizeOf(typeof(Input)))==(uint)edges.Length,"actual-owned-OS-key-burst:"+count);keysOwed.Remove(32);SaveOwed();}
 static long AllInput(NativeRenderProcess render,JObject report,long revision){
  var observed=new JArray();report["ordered_events"]=observed;
  Action<int,float?> expect=(code,wheel)=>{
   Until(()=>{var current=render.State;if(current["script_error"]?.Type!=JTokenType.Null)throw new Exception("preview-all-input-script-fault:"+current["script_error"]);return (long?)current["script_scene_revision"]>revision;},"preview-event-committed:"+code);
   var state=render.State;Need((long)state["script_scene_revision"]==revision+1,"preview-one-command-per-edge");revision++;
   var position=(JArray)state["script_node_values"][0]["state"]["position"];Need(Math.Abs((float)position[0]-code)<.001f,"preview-event-order-held-and-repeated-query:"+code);
   if(wheel.HasValue)Need(Math.Abs((float)position[1]-wheel.Value)<.001f,"preview-wheel-sign");else Need(Math.Abs((float)position[1]-.5f)<.002f&&Math.Abs((float)position[2]-.5f)<.002f,"preview-cropped-normalized-pointer");
   Need((bool?)state["script_running"]==true,"preview-event-script-running");observed.Add(new JObject{{"code",code},{"revision",revision},{"position",position.DeepClone()}});
  };
  ushort[] keys={0,0,32,37,39,38,40};
  for(int index=0;index<keys.Length;index++){if(index<2)MouseEdge(index==0?2u:8u);else KeyEdge(keys[index],false);expect(index+1,null);if(index<2)MouseEdge(index==0?4u:16u);else KeyEdge(keys[index],true);expect(101+index,null);}
  MouseEdge(0x800,120);expect(201,1);MouseEdge(0x800,unchecked((uint)-120));expect(201,-1);
  MouseEdge(2);expect(1,null);KeyEdge(32,false);expect(3,null);MouseEdge(0x800,120);expect(201,1);MouseEdge(8);expect(2,null);
  KeyEdge(32,true);expect(103,null);MouseEdge(4);expect(101,null);MouseEdge(0x800,unchecked((uint)-120));expect(201,-1);MouseEdge(16);expect(102,null);
  long batches=(long)render.State["script_batches"];Until(()=>(long?)render.State["script_batches"]>batches+5,"preview-all-input-continues-after-sequence");Need((long)render.State["script_scene_revision"]==revision,"preview-no-replayed-events");report["all_input_positive"]=render.State.DeepClone();return revision;
 }
 static void Capture(string root,string name,System.Drawing.Point origin,JObject state){File.WriteAllText(Path.Combine(root,name+".json"),new JObject{{"x",origin.X},{"y",origin.Y},{"width",640},{"height",360},{"state",state}}.ToString());Until(()=>File.Exists(Path.Combine(root,name+".captured")),"actual-preview-image-captured:"+name);}
 static void ShellExclusion(NativeRenderProcess render,InputBroker broker,JObject report,string helper,string kind){
  var probe=new DesktopHitClient(helper);
  try{
   GetWindowThreadProcessId(GetShellWindow(),out uint shell);int x=0,y=0;DesktopHitResult hit=null;
   if(kind=="icon"){
    var kinds=new JObject();report["Shell_search_classifications"]=kinds;
    for(int row=20;row<Screen.PrimaryScreen.Bounds.Height-60&&hit==null;row+=40)for(int column=20;column<Screen.PrimaryScreen.Bounds.Width/2&&hit==null;column+=40){var candidate=probe.Query(column,row);string key=candidate==null?"null":candidate.Classification.ToString();kinds[key]=((int?)kinds[key]??0)+1;if(probe.Failure!=null)throw new InvalidOperationException("preview-icon-query-failed:"+probe.Failure+":"+probe.FailurePhase);if(candidate!=null&&candidate.Classification==2&&candidate.ShellProcess==shell){x=column;y=row;hit=candidate;}}
    Need(hit!=null&&hit.ShellCreated>0,"preview-exclusion-real-Explorer-icon-found");
   }else{
    var taskbar=FindWindow("Shell_TrayWnd",null);Need(taskbar!=IntPtr.Zero&&GetWindowRect(taskbar,out var bounds),"preview-exclusion-real-taskbar-found");GetWindowRect(taskbar,out bounds);x=(bounds.Left+bounds.Right)/2;y=(bounds.Top+bounds.Bottom)/2;
    var window=WindowFromPoint(new Point{X=x,Y=y});Need(window==taskbar||GetAncestor(window,2)==taskbar,"preview-exclusion-point-on-real-taskbar");hit=probe.Query(x,y);Need(hit!=null&&hit.Classification!=1&&hit.Classification!=2,"preview-taskbar-not-blank-or-icon");
   }
   var immediate=render.State;Need((bool?)immediate["script_running"]==true&&immediate["script_error"]?.Type==JTokenType.Null,"preview-Shell-exclusion-immediate-script-healthy");
   var targets=(System.Collections.Generic.IEnumerable<InputTarget>)typeof(InputBroker).GetField("active",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(broker);var packet=targets.Single().Snapshot();
   Need(BitConverter.ToUInt32(packet,4)==1&&BitConverter.ToUInt64(packet,8)==(ulong)immediate["input_epoch"]&&WindowsMachineClock.Milliseconds()-(long)BitConverter.ToUInt64(packet,16)<150,"preview-Shell-immediate-focused-fresh-authority");
   report["Shell_immediate_before"]=immediate.DeepClone();long sequence=(long)immediate["input_source_revoke_sequence"],moved=WindowsMachineClock.Milliseconds();Click(x,y);
   Until(()=>(long?)render.State["input_source_revoke_sequence"]>sequence&&(long?)render.State["input_source_revoke_time"]>=moved,"preview-Shell-native-source-revoked");
   report["actual_Shell_hit"]=new JObject{{"kind",kind},{"x",x},{"y",y},{"classification",hit.Classification},{"window",hit.Window.ToInt64()},{"shell_pid",hit.ShellProcess},{"shell_creation",hit.ShellCreated}};
  }finally{report["Shell_probe"]=new JObject{{"failure",probe.Failure},{"phase",probe.FailurePhase},{"trace",probe.FailureTrace},{"sequence",probe.LastQuerySequence},{"maximum_query_ms",probe.MaximumQueryMilliseconds}};probe.Dispose();Need(probe.Completion.Wait(3000),"preview-Shell-probe-released");probe.Completion.GetAwaiter().GetResult();}
 }
 static int OwnedForeground(){using(var form=new Form{Text="Independent excluded foreground process",StartPosition=FormStartPosition.Manual,Location=new System.Drawing.Point(900,100),ClientSize=new Size(280,160),TopMost=true})using(var timer=new System.Windows.Forms.Timer{Interval=30000}){timer.Tick+=(sender,eventArgs)=>form.Close();form.Shown+=(sender,eventArgs)=>{form.Activate();SetForegroundWindow(form.Handle);timer.Start();};Application.Run(form);}return 0;}
 [STAThread]static int Main(string[] args){
  if(args.Length>0&&args[0]=="--owned-foreground")return OwnedForeground();
  if(Console.ReadLine()!="RUN")return 2;
  string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);string exclusion=args.Length>5?args[5]:"crop";partialClick=args.Contains("--qa-partial-click");var report=new JObject{{"status","failed"},{"case",exclusion},{"qa_partial_click",partialClick},{"scope","Actual Unity native preview window, crop and OS injection through async source/Host/LPAC; development only, not original Library/Launch or physical input"}};
  inputLedger=Path.Combine(root,"injection-owed.json");using(var injector=PinnedProcessOwner.PinCurrent())injectorCreation=injector.CreationFileTime;hangAfterDown=args.Contains("--qa-hang-after-down");
  NativeRenderProcess render=null;IntPtr child=IntPtr.Zero;var prior=GetForegroundWindow();GetCursorPos(out var cursor);
  try{foreach(int code in new[]{1,2,16,17,18,27,32,37,38,39,40,91,92})Need((GetAsyncKeyState(code)&0x8000)==0,"preview-no-preexisting-held-input:"+code);using(var broker=new InputBroker())using(var form=new Form{Text="Native SDK cropped preview check",ClientSize=new Size(640,360),StartPosition=FormStartPosition.Manual,Location=new System.Drawing.Point(80,80),TopMost=true}){
   var journal=new JournalActor(Path.Combine(root,"journal"),RuntimeLimits.JournalPending);journal.Initialized.GetAwaiter().GetResult();var clock=Stopwatch.StartNew();
   var policy=new WatchdogPolicy(RuntimeLimits.StartupMs,RuntimeLimits.FrameMs,RuntimeLimits.StopMs,RuntimeLimits.ProcessBytes,RuntimeLimits.CpuWindowMs,RuntimeLimits.CpuBudgetMs,RuntimeLimits.ActualGpuBytes,RuntimeLimits.GpuStaleMs);
   var runtime=new SupervisorRuntime(journal,new NoLaunch(),policy,2,RuntimeLimits.GlobalProcessBytes,()=>clock.ElapsedMilliseconds);
   try{
    var serviceType=typeof(NativeInputRegistry).Assembly.GetType("SpatialWallpaper.Supervision.Host+Service",true);string descriptor=Path.Combine(root,"endpoint.json");
    using(var service=(IDisposable)serviceType.GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single().Invoke(new object[]{runtime,journal,descriptor,null,null,null,broker})){
     form.Show();form.Activate();SetForegroundWindow(form.Handle);Pump(100);var origin=form.PointToScreen(System.Drawing.Point.Empty);
     var crop=new JArray(origin.X+80,origin.Y+60,400,220);var pose=new JObject{{"display_id","preview-fixture"},{"width",.53},{"height",.53*9/16},{"keep_height",true},{"eye",new JArray(0,0,-.67)},{"fresh",false},{"paused",false}};
     string worker=Path.GetFullPath(args[3]),program=Path.GetFullPath(args[4]),helper=Path.GetFullPath(args[2]);var inventory=JObject.Parse(File.ReadAllText(Path.Combine(worker,"build.json")));var definition=JObject.Parse(File.ReadAllText(program));
     var config=new JObject{{"artwork","showcase"},{"session",Guid.NewGuid().ToString("N")},{"generation",1},{"desktop",false},{"pose",pose},{"diagnostics",true},
      {"input",new JObject{{"descriptor",descriptor},{"region",crop}}},
      {"script",new JObject{{"node_paths",new JArray("InteriorContent_A/ReplaceableSlot")},{"helper",helper},{"helper_sha256",Hash(helper)},{"bootstrap_cache",Path.Combine(root,"bootstrap")},
       {"launch",new JObject{{"worker_image",worker},{"worker_files",inventory["runtime_files"]},{"attempts",Path.Combine(root,"attempts")},{"attempt",Guid.NewGuid().ToString("N")},{"program",program},{"program_sha256",Hash(program)},{"build",definition["build_id"]}}}}}};
     if(args.Contains("--trusted-ui")){config["preview_ui"]=true;pose["input_region"]=crop;report["trusted_ui"]=true;}
     try{
      render=new NativeRenderProcess(args[0],Path.Combine(root,"renderer"),config);
      Until(()=>render.Ready.IsCompleted,"native-preview-ready",40000);var surface=render.Ready.GetAwaiter().GetResult();child=surface.Window;
      SetWindowLong(child,-16,(GetWindowLong(child,-16)|0x40000000)&unchecked((int)~0x80000000u));SetParent(child,form.Handle);Need(MoveWindow(child,0,0,640,360,true),"native-preview-child-sized");
      form.Activate();SetForegroundWindow(form.Handle);Pump(150);
      Until(()=>(bool?)render.State["input_ready"]==true&&(long?)render.State["script_batches"]>5,"native-preview-input-connected");
      if(GetForegroundWindow()!=form.Handle){Need(WindowFromPoint(new Point{X=origin.X+20,Y=origin.Y+20})==child,"foreground-bootstrap-hit-is-owned-native-child");Click(origin.X+20,origin.Y+20);form.Activate();SetForegroundWindow(form.Handle);Pump(50);}
      Need(GetForegroundWindow()==form.Handle,"preview-parent-has-real-foreground");
      var before=render.State;report["before"]=before.DeepClone();report["focus_before"]=NativeFocus(child);long revision=(long)before["script_scene_revision"];
      Need(SetCursorPos(origin.X+280,origin.Y+170),"positive-position-before-fresh-hit-proof");Pump(30);report["focus_immediate_positive"]=NativeFocus(child);Need(WindowFromPoint(new Point{X=origin.X+280,Y=origin.Y+170})==child&&GetForegroundWindow()==form.Handle,"positive-immediate-hit-and-root-foreground-are-original-preview");Click(origin.X+280,origin.Y+170);
      Until(()=>(long?)render.State["script_scene_revision"]==revision+2,"native-preview-complete-positive-down-up");
      var positive=render.State;Need((bool?)positive["script_running"]==true&&positive["script_error"]?.Type==JTokenType.Null,"native-preview-positive-script-healthy");
      report["positive"]=positive.DeepClone();report["focus_positive"]=NativeFocus(child);Capture(root,"positive",origin,positive);report["focus_after_capture"]=NativeFocus(child);long batches=(long)positive["script_batches"];
      Until(()=>(long?)render.State["script_batches"]>batches+4,"native-preview-positive-continues");
      long expectedRevision=revision+2;
      if(exclusion=="event-saturation"){
       var immediate=render.State;long cursorBefore=(long)immediate["input_cursor"];var overflowClock=Stopwatch.StartNew();
       using(var originalScript=Process.GetProcessById((int)immediate["script_pid"]))using(var heldScript=new ScriptSuspension(originalScript,(long)immediate["script_creation"])){
        for(int burst=0;burst<12&&render.State["script_error"]?.Type==JTokenType.Null;burst++){KeyBurst(32);long expectedCursor=cursorBefore+32;Until(()=>render.State["script_error"]?.Type==JTokenType.String||(long?)render.State["input_cursor"]>=expectedCursor,"actual-OS-edges-reach-native-input-queue-or-explicit-fault",1500);cursorBefore=expectedCursor;}
        var overflow=render.State;Need((string)overflow["script_error"]=="native-input:native-input-event-capacity-or-gap"&&(int?)overflow["input_script_queue"]?["rejected"]>256,"actual-native-ordered-event-capacity-not-deadline-or-gap");Need((int?)overflow["input_transport"]?["peak_queued"]<64&&overflow["input_error"]?.Type==JTokenType.Null,"event-budget-not-transport-mailbox-overflow");Need((long)overflow["script_scene_revision"]==expectedRevision,"pending-author-tick-cannot-partially-commit-saturated-events");report["event_saturated"]=overflow;report["event_saturation_elapsed_ms"]=overflowClock.ElapsedMilliseconds;
       }
       long resumedFrame=(long)render.State["frame"];report["event_owner_resumed_or_exited_frame"]=resumedFrame;Until(()=>(long?)render.State["frame"]>resumedFrame+10,"actual-event-overflow-native-frames-continue-after-owned-VM-resume-or-exit");Need((long)render.State["script_scene_revision"]==expectedRevision,"no-deferred-commit-after-original-script-resume-or-exit");long escapes=(long)render.State["preview_ui"]["escapes"];KeyEdge(27,false);try{Until(()=>(long?)render.State["preview_ui"]?["escapes"]==escapes+1,"trusted-Escape-survives-real-ordered-event-overflow");}finally{KeyEdge(27,true);}report["event_saturated_ui_live"]=render.State;
      }else if(exclusion=="pipe-block"||exclusion=="pipe-saturation"){
       bool saturation=exclusion=="pipe-saturation";long pipeFrame=(long)render.State["frame"];var held=new BrokerHold(broker,saturation?750:150);
       try{using(held){
        Until(()=>held.Held&&(bool?)render.State["input_transport"]?["pending"]==true&&(int?)render.State["input_transport"]?["queued"]>0,"real-native-preview-pipe-request-actually-blocked",500);report["pipe_blocked"]=render.State;
        if(saturation){
         KeyBurst(160);
         Until(()=>render.State["input_error"]?.Type==JTokenType.String,"blocked-pipe-explicit-source-fault",1000);Need(((string)render.State["input_error"]).Contains("async-preview-mailbox-full")&&(int?)render.State["input_transport"]?["peak_queued"]==64,"real-sixty-four-slot-saturation-exact-fault-not-channel-timeout");Until(()=>(long?)render.State["frame"]>pipeFrame+2,"real-pipe-saturation-does-not-block-native-render-frames");report["pipe_saturated"]=render.State;
         long escapes=(long)render.State["preview_ui"]["escapes"];KeyEdge(27,false);try{Until(()=>(long?)render.State["preview_ui"]?["escapes"]==escapes+1,"trusted-Escape-survives-real-author-mailbox-exception");}finally{KeyEdge(27,true);}report["pipe_saturated_ui_live"]=render.State;
         var expired=Stopwatch.StartNew();while(expired.ElapsedMilliseconds<300)Pump(5);
        }else{
         for(int index=0;index<3;index++){KeyEdge(32,false);KeyEdge(32,true);}Until(()=>held.Held&&(int?)render.State["input_transport"]?["queued"]>=6&&(long?)render.State["frame"]>pipeFrame+2,"six-real-edges-retained-while-blocked-pipe-native-frames-continue",500);report["pipe_queued"]=render.State;
        }
       }
       }finally{report["broker_actual_hold_ms"]=held.ElapsedMs;}
       if(saturation){Until(()=>(bool?)render.State["input_completed"]==true&&(bool?)render.State["input_resources_released"]==true,"saturated-source-pipe-owner-and-authority-retired");Need((long)render.State["script_scene_revision"]==expectedRevision,"expired-inflight-and-queued-old-input-zero-commit-after-pipe-release");}
       else{Need(held.ElapsedMs<200,"real-pipe-block-released-within-original-channel-deadline");expectedRevision+=6;Until(()=>(long?)render.State["script_scene_revision"]==expectedRevision,"six-blocked-pipe-edges-delivered-once-in-order");Need(render.State["input_error"]?.Type==JTokenType.Null,"short-real-pipe-block-does-not-break-healthy-source");}
       report["pipe_after_release"]=render.State;
      }else if(exclusion=="script-crash"||exclusion=="script-deadline"||exclusion=="script-budget"){
       var immediate=render.State;long faultFrame=(long)immediate["frame"];var faultClock=Stopwatch.StartNew();
       Need((bool?)immediate["script_running"]==true&&immediate["script_error"]?.Type==JTokenType.Null,"fault-immediate-original-script-healthy");
       if(exclusion=="script-budget"){
        MouseEdge(8);try{Until(()=>{var fault=render.State["script_error"];if(fault?.Type!=JTokenType.String)return false;Need((string)fault=="InvalidOperationException:script-fault:ExecutionBudgetException:execution-budget","looping-script-exact-execution-budget-not-other-fault");return true;},"looping-script-budget-fault-reported");}finally{MouseEdge(16);}
       }else using(var ownedScript=Process.GetProcessById((int)immediate["script_pid"])){
        Need(PinnedProcessOwner.ReadCreationFileTime(ownedScript.Handle)==(long)immediate["script_creation"],"fault-only-original-script-kernel");
        if(exclusion=="script-crash"){ownedScript.Kill();Need(ownedScript.WaitForExit(5000),"fault-original-script-kernel-exited");Until(()=>render.State["script_error"]?.Type==JTokenType.String,"crashed-script-fault-reported");}
        else using(var suspended=new ScriptSuspension(ownedScript,(long)immediate["script_creation"]))Until(()=>(string)render.State["script_error"]=="native-script-deadline","blocked-script-original-deadline-enforced");
       }
       var failed=render.State;Need((long)failed["script_scene_revision"]==expectedRevision,"script-fault-no-partial-command");Until(()=>(long?)render.State["frame"]>faultFrame+5,"script-fault-native-frames-advance");long after=(long)render.State["frame"];Until(()=>(long?)render.State["frame"]>after+10,"script-fault-native-continues-after-fault");
       report["script_fault"]=render.State.DeepClone();report["script_fault_elapsed_ms"]=faultClock.ElapsedMilliseconds;
       if(args.Contains("--trusted-ui")){long escapes=(long)render.State["preview_ui"]["escapes"];KeyEdge(27,false);try{Until(()=>(long?)render.State["preview_ui"]?["escapes"]==escapes+1,"trusted-preview-Escape-live-after-author-fault");}finally{KeyEdge(27,true);}Need((long)render.State["script_scene_revision"]==expectedRevision,"trusted-Escape-does-not-dispatch-to-faulted-author");report["trusted_ui_after_fault"]=render.State;}
      }else if(exclusion=="switch"){
       var registrations=(IEnumerable<InputTarget>)typeof(InputBroker).GetField("active",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(broker);var oldTarget=registrations.Single();ulong oldEpoch=oldTarget.CaptureEpoch;
       KeyEdge(32,false);Until(()=>(long?)render.State["script_scene_revision"]==expectedRevision+1,"switch-old-generation-receives-owned-held-key");report["switch_old"]=render.State.DeepClone();
       SetParent(child,IntPtr.Zero);child=IntPtr.Zero;render.Stop();Until(()=>render.Completion.IsCompleted,"switch-original-renderer-and-script-complete",25000);render.Completion.GetAwaiter().GetResult();Need(render.ResourcesReleased&&!registrations.Any(),"switch-old-preview-source-unregistered-before-replacement");render=null;
       config["generation"]=2;config["script"]["launch"]["attempt"]=Guid.NewGuid().ToString("N");render=new NativeRenderProcess(args[0],Path.Combine(root,"renderer-next"),config);
       Until(()=>render.Ready.IsCompleted,"switch-next-generation-ready",40000);surface=render.Ready.GetAwaiter().GetResult();child=surface.Window;
       SetWindowLong(child,-16,(GetWindowLong(child,-16)|0x40000000)&unchecked((int)~0x80000000u));SetParent(child,form.Handle);Need(MoveWindow(child,0,0,640,360,true),"switch-next-child-sized");form.Activate();SetForegroundWindow(form.Handle);
       Until(()=>(bool?)render.State["input_ready"]==true&&(long?)render.State["script_batches"]>5,"switch-next-input-connected");Need(registrations.Single().Identity.Generation==2&&registrations.Single().Identity.Instance==oldTarget.Identity.Instance,"switch-same-instance-next-generation-registration");
       long nextRevision=(long)render.State["script_scene_revision"];Need(nextRevision==1,"switch-no-held-input-replayed-into-next-scene");
       oldTarget.Observe(new InputObservation(long.MaxValue,WindowsMachineClock.Milliseconds(),true,true,true,true,true,false,true,1,.5f,.5f,0,oldEpoch));
       KeyEdge(32,true);Pump(150);Need((long)render.State["script_scene_revision"]==nextRevision,"switch-old-registration-and-preheld-release-zero-dispatch");
       Click(origin.X+280,origin.Y+170);Until(()=>(long?)render.State["script_scene_revision"]==nextRevision+2,"switch-next-complete-positive-down-up");expectedRevision=nextRevision+2;
       Need((bool?)render.State["script_running"]==true&&render.State["script_error"]?.Type==JTokenType.Null,"switch-next-script-healthy");long continued=(long)render.State["script_batches"];Until(()=>(long?)render.State["script_batches"]>continued+4,"switch-next-script-continues");report["switch_next"]=render.State.DeepClone();
       pose["paused"]=true;render.PublishPose(pose);Until(()=>(bool?)render.State["paused"]==true,"switch-final-pause");
      }else if(exclusion=="backpressure"||exclusion=="backpressure-revoke"){
       var immediate=render.State;Need((bool?)immediate["script_running"]==true&&immediate["script_error"]?.Type==JTokenType.Null,"backpressure-immediate-script-healthy");
       using(var scriptProcess=Process.GetProcessById((int)immediate["script_pid"])){
        var clocked=Stopwatch.StartNew();using(var suspended=new ScriptSuspension(scriptProcess,(long)immediate["script_creation"])){
         Pump(30);var blocked=render.State;report["backpressure_blocked"]=blocked.DeepClone();long blockedBatches=(long)blocked["script_batches"],blockedFrame=(long)blocked["frame"];
         for(int click=0;click<3;click++){MouseEdge(2);MouseEdge(4);}
         if(exclusion=="backpressure-revoke"){pose["paused"]=true;render.PublishPose(pose);Until(()=>(bool?)render.State["paused"]==true&&!((bool?)render.State["input_ready"]??true),"backpressure-pause-authority-revoked");Pump(300);}else Pump(80);
         var waiting=render.State;report["backpressure_waiting"]=waiting.DeepClone();report["script_suspended_ms"]=clocked.ElapsedMilliseconds;report["observed_ack_delta_during_suspension"]=(long)waiting["script_batches"]-blockedBatches;Need((long)waiting["script_scene_revision"]==expectedRevision,"suspended-script-no-new-input-commit");Need((long)waiting["frame"]>blockedFrame+2,"native-render-advances-while-script-blocked");
        }
       }
       if(exclusion=="backpressure"){
        Until(()=>(long?)render.State["script_scene_revision"]==expectedRevision+6,"queued-preview-three-complete-clicks-delivered");expectedRevision+=6;Need(render.State["script_error"]?.Type==JTokenType.Null,"backpressure-release-script-healthy");
        long continued=(long)render.State["script_batches"];Until(()=>(long?)render.State["script_batches"]>continued+4,"backpressure-script-continues-after-drain");
        pose["paused"]=true;render.PublishPose(pose);Until(()=>(bool?)render.State["paused"]==true,"backpressure-test-final-pause");
       }else{pose["paused"]=false;render.PublishPose(pose);Until(()=>(bool?)render.State["paused"]==false,"revoked-backpressure-explicit-resume");Until(()=>render.State["script_error"]?.Type==JTokenType.String,"revoked-blocked-script-stops-without-old-input-commit");}
       report["backpressure_after"]=render.State.DeepClone();
      }else if(exclusion=="icon"||exclusion=="taskbar"){
       int helperIndex=Array.IndexOf(args,"--hit-helper");Need(helperIndex>=0&&helperIndex+1<args.Length,"preview-Shell-helper-explicit");ShellExclusion(render,broker,report,args[helperIndex+1],exclusion);
      }else if(exclusion=="all-input"){
       expectedRevision=AllInput(render,report,expectedRevision);ulong epoch=(ulong)render.State["input_epoch"];KeyEdge(16,false);
       try{Until(()=>(ulong?)render.State["input_epoch"]>epoch,"preview-Shift-revokes");Click(origin.X+280,origin.Y+170);}finally{KeyEdge(16,true);}
      }else if(exclusion=="pause"){
       pose["paused"]=true;render.PublishPose(pose);Until(()=>(bool?)render.State["paused"]==true&&(bool?)render.State["input_ready"]==false,"native-preview-pause-revokes");
       Click(origin.X+280,origin.Y+170);
      }else if(exclusion=="menu"){
       using(var menu=new ContextMenuStrip()){
        var immediate=render.State;Need((bool?)immediate["script_running"]==true&&immediate["script_error"]?.Type==JTokenType.Null,"menu-immediate-script-healthy");
        var active=(System.Collections.Generic.IEnumerable<InputTarget>)typeof(InputBroker).GetField("active",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(broker);var input=active.Single().Snapshot();
        Need(BitConverter.ToUInt32(input,4)==1&&BitConverter.ToUInt64(input,8)==(ulong)immediate["input_epoch"]&&WindowsMachineClock.Milliseconds()-(long)BitConverter.ToUInt64(input,16)<150,"menu-immediate-authority-fresh-focused");
        report["menu_immediate_before"]=immediate.DeepClone();long sourceSequence=(long?)immediate["input_source_revoke_sequence"]??0,opened=WindowsMachineClock.Milliseconds();
        menu.Items.Add("Excluded parent-process preview menu");menu.Show(form,new System.Drawing.Point(500,280));Pump(100);
        ulong epoch=(ulong)immediate["input_epoch"];Until(()=>(ulong?)render.State["input_epoch"]>epoch,"parent-process-menu-revokes-native-preview");
        var menuState=render.State;Need((long?)menuState["input_source_revoke_sequence"]>sourceSequence&&(long?)menuState["input_source_revoke_time"]>=opened,"menu-native-source-physical-proof-revoked");
        report["menu"]=render.State.DeepClone();menu.Close();
       }
      }else if(exclusion=="region"){
       pose["input_region"]=new JArray(origin.X+400,origin.Y+230,180,100);render.PublishPose(pose);Pump(150);Click(origin.X+280,origin.Y+170);
      }else if(exclusion=="disconnect"){
       var active=(System.Collections.Generic.IEnumerable<InputTarget>)typeof(InputBroker).GetField("active",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(broker);active.Single().Dispose();
       Until(()=>(bool?)render.State["input_completed"]==true&&(bool?)render.State["input_resources_released"]==true,"failed-preview-input-retires-author-source-before-renderer-exit");
       Need(render.State["input_error"]?.Type==JTokenType.String,"preview-input-disconnect-explicit-error");
       if(args.Contains("--trusted-ui")){string pipeError=(string)render.State["input_error"];Need(pipeError.Contains("EndOfStreamException")||pipeError.Contains("IOException"),"actual-source-pipe-EOF-or-broken-pipe-observed");long escapes=(long)render.State["preview_ui"]["escapes"];KeyEdge(27,false);try{Until(()=>(long?)render.State["preview_ui"]?["escapes"]==escapes+1,"trusted-Escape-live-after-real-source-pipe-disconnect");}finally{KeyEdge(27,true);}report["pipe_disconnected_ui_live"]=render.State;}
      }else if(exclusion=="other-app"){
       var immediate=render.State;Need((bool?)immediate["script_running"]==true&&immediate["script_error"]?.Type==JTokenType.Null,"other-app-immediate-script-healthy");
       using(var other=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--owned-foreground"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden})){
        try{
         IntPtr foreground=IntPtr.Zero;Until(()=>{other.Refresh();foreground=GetForegroundWindow();GetWindowThreadProcessId(foreground,out uint pid);return pid==other.Id&&!other.HasExited;},"independent-process-owns-real-foreground");
         Need(PinnedProcessOwner.ReadCreationFileTime(other.Handle)>0,"other-app-original-process-pin");Need(WindowFromPoint(new Point{X=1040,Y=180})==foreground,"other-app-owned-injection-point");
         Click(1040,180);Until(()=>(bool?)render.State["input_ready"]==false||(ulong?)render.State["input_epoch"]>(ulong)immediate["input_epoch"],"other-app-revokes-native-preview");
         report["other_app"]=new JObject{{"pid",other.Id},{"creation_filetime",PinnedProcessOwner.ReadCreationFileTime(other.Handle)},{"foreground",foreground.ToInt64()},{"state",render.State}};
        }finally{if(!other.HasExited){Need(other.CloseMainWindow(),"other-app-close-requested");Until(()=>other.HasExited,"other-app-original-process-exited");}}
       }
      }else if(exclusion=="other-window"){
       using(var other=new Form{Text="Excluded foreground window",StartPosition=FormStartPosition.Manual,Location=new System.Drawing.Point(900,100),ClientSize=new Size(280,160),TopMost=true}){
        other.Show();other.Activate();SetForegroundWindow(other.Handle);Pump(50);var point=other.PointToScreen(new System.Drawing.Point(140,80));Need(WindowFromPoint(new Point{X=point.X,Y=point.Y})==other.Handle,"other-window-injection-is-owned");
        Click(point.X,point.Y);Need(GetForegroundWindow()==other.Handle,"different-preview-root-owns-real-foreground");
        Until(()=>(bool?)render.State["input_ready"]==false||(ulong?)render.State["input_epoch"]>(ulong)positive["input_epoch"],"other-window-revokes-native-preview");report["other_window"]=render.State.DeepClone();
       }
      }else Click(origin.X+20,origin.Y+20);
      Pump(300);var outside=render.State;
      Need((long)outside["script_scene_revision"]==expectedRevision,"excluded-preview-zero-native-dispatch:"+exclusion);long frame=(long)outside["frame"];
      Until(()=>(long?)render.State["frame"]>frame+5,"excluded-preview-rendering-continues");report["outside"]=render.State.DeepClone();
      Capture(root,"negative",origin,render.State);
      report["status"]="passed-native-preview-crop-development";
     }catch(Exception primary){report["primary_error"]=primary.ToString();report["failure_focus"]=NativeFocus(child);report["failure_state"]=render?.State.DeepClone();try{var targets=(System.Collections.Generic.IEnumerable<InputTarget>)typeof(InputBroker).GetField("active",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(broker);var batch=targets.Single().OrderedSnapshot(0);report["failure_input"]=new JObject{{"cursor",batch.Cursor},{"events",batch.Events.Length},{"snapshot",BitConverter.ToString(batch.Snapshot)}};}catch(Exception diagnostic){report["diagnostic_error"]=diagnostic.Message;}throw;}finally{
      if(child!=IntPtr.Zero){SetParent(child,IntPtr.Zero);child=IntPtr.Zero;}
      if(render!=null){render.Stop();Until(()=>render.Completion.IsCompleted,"native-preview-complete-cleanup",25000);try{render.Completion.GetAwaiter().GetResult();}catch(Exception cleanup){report["cleanup_error"]=cleanup.ToString();if(report["primary_error"]==null)throw;}Need(render.ResourcesReleased,"native-preview-resources-released");render=null;}
     }
    }
   }finally{runtime.Close();runtime.Completion.GetAwaiter().GetResult();journal.Close();journal.Completion.GetAwaiter().GetResult();}
  }return 0;}catch(Exception error){report["error"]=error.ToString();Console.Error.WriteLine(error);return 1;}
  finally{if(buttonsOwed!=0){bool released=SendInput(1,new[]{new Input{Data=new Payload{Mouse=new Mouse{Flags=buttonsOwed}}}},Marshal.SizeOf(typeof(Input)))==1;report["emergency_mouse_release"]=released;if(released)buttonsOwed=0;}foreach(ushort code in keysOwed.ToArray()){bool released=SendInput(1,new[]{new Input{Type=1,Data=new Payload{Key=new Key{Code=code,Flags=2}}}},Marshal.SizeOf(typeof(Input)))==1;report["emergency_key_release_"+code]=released;if(released)keysOwed.Remove(code);}SaveOwed();if(render!=null){render.Stop();try{render.Completion.Wait(20000);}catch(Exception failure){report["cleanup_error"]=failure.ToString();}}report["checks"]=checks;SetCursorPos(cursor.X,cursor.Y);if(prior!=IntPtr.Zero)SetForegroundWindow(prior);File.WriteAllText(Path.Combine(root,"report.json"),report.ToString());}
 }
}
