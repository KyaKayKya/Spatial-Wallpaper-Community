using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Threading;
using System.Reflection;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using System.Drawing;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Sdk.Capabilities;
using SpatialWallpaper.Supervision;
using SpatialWallpaper.Supervision.Windows;
using SpatialWallpaper.Supervision.Interactive;

static class NativeRenderInputChecks {
 [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
 [StructLayout(LayoutKind.Sequential)]struct CursorPoint {public int X,Y;}
 [DllImport("user32.dll")]static extern bool GetCursorPos(out CursorPoint point);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [StructLayout(LayoutKind.Sequential)]struct MouseInput {public int X,Y;public uint Data,Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Sequential)]struct KeyInput {public ushort Key,Scan;public uint Flags,Time;public UIntPtr Extra;}
 [StructLayout(LayoutKind.Explicit)]struct InputPayload {[FieldOffset(0)]public MouseInput Mouse;[FieldOffset(0)]public KeyInput Key;}
 [StructLayout(LayoutKind.Sequential)]struct NativeInput {public uint Type;public InputPayload Payload;}
 [DllImport("user32.dll")]static extern uint SendInput(uint count,NativeInput[] inputs,int size);
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool AllowSetForegroundWindow(uint process);
 [StructLayout(LayoutKind.Sequential)]struct WindowRect{public int Left,Top,Right,Bottom;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string title);
 [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out WindowRect rect);
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(CursorPoint point);
 [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
 [DllImport("user32.dll")]static extern IntPtr GetShellWindow();
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 static uint injectionFocusProcess;static int injectionFocusGrants;
 static bool primaryOwed;
 static readonly HashSet<ushort> keysOwed=new HashSet<ushort>();static bool secondaryOwed;
 static int checks;static readonly List<Process> pins=new List<Process>();
 static void Need(bool value,string why){if(!value)throw new Exception(why);checks++;}
 static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
 static void Until(Func<bool> done,string why,int ms=12000){var c=Stopwatch.StartNew();while(!done()){if(c.ElapsedMilliseconds>ms)throw new TimeoutException(why);Thread.Sleep(5);}}
 static JObject Wait(NativeDesktopTarget target,Func<JObject,bool> condition,string why){JObject state=null;Until(()=>{state=target.State;return condition(state);},why);return state;}
 static void Pin(int pid,long birth=0){var p=Process.GetProcessById(pid);try{long actual=PinnedProcessOwner.ReadCreationFileTime(p.Handle);Need(birth==0||actual==birth,"owned-child-kernel-birth");pins.Add(p);}catch{p.Dispose();throw;}}
 static void PinCollectors(JObject state){var ids=((JArray)state["input_helpers"]).ToObject<int[]>();Need(ids.Length==2,"renderer-input-collector-pair");foreach(int pid in ids)Pin(pid);}
 sealed class NoLaunch:IWorkerLauncher {public IManagedWorker Start(LaunchRequest r,AttemptReceipt a,Action<int> p){throw new Exception("no-legacy-renderer");}}
 // Test injector's last-input eligibility belongs to this process. Match the
 // original native input fixture's explicit, target-only foreground grant.
 // This never changes the collector's gate or a machine-wide policy.
 static void GrantInjectionFocus(){if(injectionFocusProcess!=0){Need(AllowSetForegroundWindow(injectionFocusProcess),"injector-target-foreground-grant-denied");injectionFocusGrants++;}}
 static void InjectMouse(uint flags,uint data=0){if(flags==2)primaryOwed=true;if(flags==8)secondaryOwed=true;Need(SendInput(1,new[]{new NativeInput{Payload=new InputPayload{Mouse=new MouseInput{Flags=flags,Data=data}}}},Marshal.SizeOf(typeof(NativeInput)))==1,"OS-mouse-edge-delivered");if(flags==4)primaryOwed=false;if(flags==16)secondaryOwed=false;GrantInjectionFocus();}
 static void InjectKey(ushort key,bool up){if(!up)keysOwed.Add(key);Need(SendInput(1,new[]{new NativeInput{Type=1,Payload=new InputPayload{Key=new KeyInput{Key=key,Flags=up?2u:0u}}}},Marshal.SizeOf(typeof(NativeInput)))==1,"OS-key-edge-delivered");if(up)keysOwed.Remove(key);GrantInjectionFocus();}
 static void Click(int x,int y){InjectMouse(2);Thread.Sleep(25);InjectMouse(4);Thread.Sleep(25);}
 static void ReleasePrimary(JObject report){if(primaryOwed){report["emergency_primary_release"]=SendInput(1,new[]{new NativeInput{Payload=new InputPayload{Mouse=new MouseInput{Flags=4}}}},Marshal.SizeOf(typeof(NativeInput)))==1;primaryOwed=false;}if(secondaryOwed){report["emergency_secondary_release"]=SendInput(1,new[]{new NativeInput{Payload=new InputPayload{Mouse=new MouseInput{Flags=16}}}},Marshal.SizeOf(typeof(NativeInput)))==1;secondaryOwed=false;}foreach(var key in keysOwed.ToArray()){report["emergency_key_release_"+key]=SendInput(1,new[]{new NativeInput{Type=1,Payload=new InputPayload{Key=new KeyInput{Key=key,Flags=2}}}},Marshal.SizeOf(typeof(NativeInput)))==1;keysOwed.Remove(key);}}
 static void Capture(string root,string name,DesktopDisplay display,JObject state){File.WriteAllText(Path.Combine(root,name+".json"),new JObject{{"display",new JObject{{"x",display.X},{"y",display.Y},{"width",display.Width},{"height",display.Height}}},{"state",state}}.ToString());Until(()=>File.Exists(Path.Combine(root,name+".captured")),"actual-OS-native-desktop-capture:"+name,10000);}
 static void OsClicks(NativeDesktopTarget target,DesktopDisplay display,string root,JObject report){
  int x=display.X+(int)(display.Width*.7),y=display.Y+(int)(display.Height*.55);
  Need(SetCursorPos(x,y),"OS-cursor-position");Thread.Sleep(100);
  // Focus the exposed empty desktop before counting. This warmup click is
  // deliberately outside the measured ten-edge window.
  Click(x,y);Thread.Sleep(350);var before=target.State;Need(before["script_error"]?.Type==JTokenType.Null,"native-input-warmup-script-error");
  report["OS_before"]=before.DeepClone();long revision=(long)before["script_scene_revision"];
  Capture(root,"os-clicks-before",display,before);
  for(int i=0;i<10;i++)Click(x,y);
  JObject positive;try{positive=Wait(target,s=>(long?)s["script_scene_revision"]>=revision+10,"OS-clicks-not-applied-to-native-scene");}finally{report["OS_after_clicks"]=target.State.DeepClone();}Thread.Sleep(200);positive=target.State;
  Need((long)positive["script_scene_revision"]==revision+10,"OS-clicks-lost-or-replayed");
  Need(Math.Abs((float)positive["script_node_values"][0]["state"]["position"][0]-3f)<.001f,"actual-OS-input-native-object-position");
  Need((bool?)positive["script_running"]==true&&positive["script_error"]?.Type==JTokenType.Null&&(bool?)positive["input_ready"]==true,"OS-positive-script-not-healthy");
  long positiveBatches=(long)positive["script_batches"];positive=Wait(target,s=>(long?)s["script_batches"]>positiveBatches+5,"OS-positive-script-did-not-continue");
  Need((bool?)positive["script_running"]==true&&positive["script_error"]?.Type==JTokenType.Null&&(long)positive["script_scene_revision"]==revision+10,"OS-positive-late-fault-or-replayed-command");
  Capture(root,"os-clicks-positive",display,positive);
  using(var blocker=new Form{Text="Spatial Wallpaper owned input exclusion",StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(x-100,y-60,200,120),TopMost=true}){
   blocker.Show();blocker.Activate();SetForegroundWindow(blocker.Handle);PumpWindow(150);Click(x,y);PumpWindow(150);Need(GetForegroundWindow()==blocker.Handle,"negative-OS-window-not-foreground");PumpWindow(400);var negative=target.State;
   Need((long)negative["script_scene_revision"]==(long)positive["script_scene_revision"],"foreground-other-window-input-reached-native-script");
   long frame=(long)negative["frame"];var advanced=Wait(target,s=>(long?)s["frame"]>frame+5,"focus-revoke-stopped-native-frames");
   Capture(root,"os-clicks-negative",display,advanced);
   report["actual_OS_input"]=new JObject{{"measured_primary_clicks",10},{"scene_revision_before",revision},{"scene_revision_after",positive["script_scene_revision"]},{"negative_scene_revision",negative["script_scene_revision"]},{"negative",negative},{"native_frame_after_revoke",advanced["frame"]},{"scope","Actual primary OS edges through original collector/Broker/LPAC command bridge; other owned foreground window zero native commands. No keyboard/icon/taskbar/crop/full input or performance claim."}};
  }
 }
 static void PumpWindow(int milliseconds){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<milliseconds){Application.DoEvents();Thread.Sleep(1);}}
 static JObject BrokerState(InputBroker broker){
  // Read the actual original Broker target, never synthesize author input.
  var active=(IEnumerable<InputTarget>)typeof(InputBroker).GetField("active",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(broker);
  var packet=active.Single().Snapshot();return new JObject{{"focused",BitConverter.ToUInt32(packet,4)==1},{"epoch",BitConverter.ToUInt64(packet,8)},{"time",BitConverter.ToUInt64(packet,16)}};
 }
 static void OsExclusion(NativeDesktopTarget target,DesktopDisplay display,string root,JObject report,InputBroker broker,string helper,string kind,bool preRevoke,bool excludedClick){
  Need(kind=="icon"||kind=="taskbar","known-OS-exclusion-fixture");int bx=display.X+(int)(display.Width*.7),by=display.Y+(int)(display.Height*.55);
  Need(SetCursorPos(bx,by),"exclusion-blank-cursor");Thread.Sleep(100);Click(bx,by);
  Until(()=>(bool)BrokerState(broker)["focused"],"exclusion-positive-never-acquired");Thread.Sleep(150);var before=target.State;
  Need((bool?)before["script_running"]==true&&before["script_error"]?.Type==JTokenType.Null,"exclusion-positive-script-unhealthy");
  long revision=(long)before["script_scene_revision"];
  if(excludedClick){
   // Prove both button edges still reach this exact healthy scene before the
   // excluded click; an earlier wheel positive cannot establish that alone.
   foreach(uint edge in new uint[]{2,4}){
    InjectMouse(edge);var accepted=Wait(target,s=>(long?)s["script_scene_revision"]>revision,"exclusion-positive-button-edge-missing");
    Need((long)accepted["script_scene_revision"]==revision+1&&(bool?)accepted["script_running"]==true&&accepted["script_error"]?.Type==JTokenType.Null,"exclusion-positive-button-unhealthy-or-extra");
    Need(Math.Abs((float)accepted["script_node_values"][0]["state"]["position"][0]-(edge==2?1:101))<.001f,"exclusion-positive-button-wrong-edge");revision++;
   }
   report["same_session_primary_edges_verified"]=true;
  }
  InjectMouse(0x800,120);
  var positive=Wait(target,s=>(long?)s["script_scene_revision"]>revision,"exclusion-positive-wheel-missing");
  Need((long)positive["script_scene_revision"]==revision+1&&Math.Abs((float)positive["script_node_values"][0]["state"]["position"][0]-201)<.001f&&Math.Abs((float)positive["script_node_values"][0]["state"]["position"][1]-1)<.001f,"exclusion-positive-wheel-not-native");
  long batches=(long)positive["script_batches"];positive=Wait(target,s=>(long?)s["script_batches"]>batches+5,"exclusion-positive-not-continuing");
  Need(positive["script_error"]?.Type==JTokenType.Null&&(bool)BrokerState(broker)["focused"],"exclusion-positive-late-fault");
  var authority=BrokerState(broker);report["exclusion_positive"]=positive.DeepClone();report["broker_positive"]=authority.DeepClone();Capture(root,"os-clicks-positive",display,positive);
  int x=0,y=0;var probe=new DesktopHitClient(helper);
  try{
   Pin(probe.ProcessId);GetWindowThreadProcessId(GetShellWindow(),out uint shellPid);DesktopHitResult hit=null;
   if(kind=="icon"){
    bool found=false;for(int yy=display.Y+20;yy<display.Y+display.Height-60&&!found;yy+=40)for(int xx=display.X+20;xx<display.X+display.Width/2&&!found;xx+=40){var candidate=probe.Query(xx,yy);Need(candidate!=null,"exclusion-real-icon-query");if(candidate.Classification==2&&candidate.ShellProcess==shellPid){x=xx;y=yy;hit=candidate;found=true;}}
    Need(found&&hit.ShellCreated>0,"exclusion-real-Explorer-icon-not-found");
   }else{
    var bar=FindWindow("Shell_TrayWnd",null);Need(bar!=IntPtr.Zero&&GetWindowRect(bar,out WindowRect unused),"exclusion-real-taskbar-not-found");WindowRect bounds;GetWindowRect(bar,out bounds);x=(bounds.Left+bounds.Right)/2;y=Math.Min(display.Y+display.Height-2,(bounds.Top+bounds.Bottom)/2);
    var window=WindowFromPoint(new CursorPoint{X=x,Y=y});Need(window==bar||GetAncestor(window,2)==bar,"exclusion-point-not-on-real-taskbar");hit=probe.Query(x,y);Need(hit!=null&&hit.Classification!=1&&hit.Classification!=2,"exclusion-taskbar-not-classified-as-excluded");
    report["actual_taskbar_window"]=bar.ToInt64();
   }
   report["actual_exclusion_hit"]=new JObject{{"kind",kind},{"x",x},{"y",y},{"classification",hit.Classification},{"window",hit.Window.ToInt64()},{"shell_pid",hit.ShellProcess},{"shell_creation",hit.ShellCreated}};
   if(preRevoke){var active=(IEnumerable<InputTarget>)typeof(InputBroker).GetField("active",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(broker);active.Single().Revoke();report["intentional_QA_pre_revoke"]=true;}
   var immediate=Wait(target,s=>(long?)s["script_batches"]>(long)positive["script_batches"],"exclusion-script-stopped-before-pointer-move");
   Need((bool?)immediate["script_running"]==true&&immediate["script_error"]?.Type==JTokenType.Null,"exclusion-script-unhealthy-before-pointer-move");
   var baseline=BrokerState(broker);Need((bool)baseline["focused"]&&(ulong)baseline["epoch"]==(ulong)authority["epoch"],"exclusion-lost-focus-before-pointer-move");
   Need(WindowsMachineClock.Milliseconds()-(long)baseline["time"]<150,"exclusion-evidence-too-old-before-pointer-move");authority=baseline;report["broker_immediate_before"]=baseline.DeepClone();report["exclusion_immediate_before"]=immediate.DeepClone();
   Need(SetCursorPos(x,y),"exclusion-position-pointer");
   if(excludedClick){InjectMouse(2);Thread.Sleep(40);InjectMouse(4);}else InjectMouse(0x800,120);
   Until(()=>!(bool)BrokerState(broker)["focused"],"actual-exclusion-did-not-revoke-original-Broker");var revoked=BrokerState(broker);Need((ulong)revoked["epoch"]>(ulong)authority["epoch"],"actual-exclusion-did-not-rotate-epoch");report["broker_revoked"]=revoked;
   Thread.Sleep(300);var negative=target.State;
   Need((long)negative["script_scene_revision"]==(long)positive["script_scene_revision"],"excluded-input-reached-native-author");
   var error=(string)negative["script_error"];Need(error==null||error.Contains("revoked-or-expired")||error.Contains("input-stale"),"exclusion-unrelated-script-fault:"+error);
   long frame=(long)negative["frame"];negative=Wait(target,s=>(long?)s["frame"]>frame+5,"exclusion-stopped-native-frames");report["exclusion_negative"]=negative.DeepClone();Capture(root,"os-clicks-negative",display,negative);
   Need(SetCursorPos(bx,by),"exclusion-return-blank");Thread.Sleep(150);Need(!(bool)BrokerState(broker)["focused"],"blank-hover-reacquired-without-click");InjectMouse(0x800,120);Thread.Sleep(150);Need((long)target.State["script_scene_revision"]==(long)positive["script_scene_revision"],"unfocused-blank-wheel-reached-native-author");
   report["exclusion_scope"]="Actual "+kind+" pointer entry followed immediately by OS "+(excludedClick?"single primary click":"wheel")+" rotates original Broker epoch and emits zero native commands; blank-hover wheel also excluded after local healthy positive wheel."+(excludedClick?" Same-session primary down/up positives verified.":" No icon/taskbar click.")+" No physical device, preview crop or whole input claim.";
   report["excluded_event"]=excludedClick?"single-primary-click":"wheel";
  }finally{probe.Dispose();Need(probe.Completion.Wait(3000),"exclusion-probe-cleanup-deadline");probe.Completion.GetAwaiter().GetResult();}
 }
 static void OsAllInput(NativeDesktopTarget target,DesktopDisplay display,string root,JObject report){
  int x=display.X+(int)(display.Width*.7),y=display.Y+(int)(display.Height*.55);Need(SetCursorPos(x,y),"all-input-cursor-position");Thread.Sleep(100);Click(x,y);Thread.Sleep(350);
  var before=target.State;Need((bool?)before["script_running"]==true&&before["script_error"]?.Type==JTokenType.Null,"all-input-warmup-not-healthy");long revision=(long)before["script_scene_revision"];
  var observed=new JArray();report["actual_OS_all_input"]=observed;report["all_input_before"]=before.DeepClone();
  Action<int,float?> expect=(code,wheel)=>{
   JObject state=null;try{state=Wait(target,s=>{if(s["script_error"]?.Type==JTokenType.String)throw new Exception("all-input-script-fault:"+s["script_error"]);return (long?)s["script_scene_revision"]>revision;},"all-input-edge-missing:"+code);}finally{report["all_input_last_state"]=target.State.DeepClone();}
   Need((long)state["script_scene_revision"]==revision+1,"all-input-extra-command:"+code);revision++;
   var position=(JArray)state["script_node_values"][0]["state"]["position"];Need(Math.Abs((float)position[0]-code)<.001f,"all-input-order-or-held-state:"+code);
   if(wheel.HasValue)Need(Math.Abs((float)position[1]-wheel.Value)<.001f,"all-input-wheel-direction");else Need(Math.Abs((float)position[1]-.7f)<.002f&&Math.Abs((float)position[2]-.55f)<.002f,"all-input-pointer-coordinates");
   Need((bool?)state["script_running"]==true&&state["script_error"]?.Type==JTokenType.Null,"all-input-edge-not-healthy");observed.Add(new JObject{{"code",code},{"revision",revision},{"position",position.DeepClone()},{"script_batches",state["script_batches"]}});
  };
  ushort[] keys={0,0,32,37,39,38,40};
  for(int i=0;i<7;i++){
   if(i<2)InjectMouse(i==0?2u:8u);else InjectKey(keys[i],false);expect(i+1,null);
   if(i<2)InjectMouse(i==0?4u:16u);else InjectKey(keys[i],true);expect(101+i,null);
  }
  InjectMouse(0x800,120);expect(201,1);InjectMouse(0x800,unchecked((uint)-120));expect(201,-1);
  // Interleave held buttons, keyboard and opposite wheel directions. Each
  // accepted source edge must reach a distinct ordered native transaction.
  InjectMouse(2);expect(1,null);InjectKey(32,false);expect(3,null);InjectMouse(0x800,120);expect(201,1);InjectMouse(8);expect(2,null);
  InjectKey(32,true);expect(103,null);InjectMouse(4);expect(101,null);InjectMouse(0x800,unchecked((uint)-120));expect(201,-1);InjectMouse(16);expect(102,null);
  var final=target.State;long batches=(long)final["script_batches"];final=Wait(target,s=>(long?)s["script_batches"]>batches+5,"all-input-final-script-stopped");Need((long)final["script_scene_revision"]==revision&&final["script_error"]?.Type==JTokenType.Null,"all-input-replayed-or-late-fault");
  report["all_input_final"]=final.DeepClone();
  ulong epoch=(ulong)final["input_epoch"];InjectKey(16,false);
  try{var revoked=Wait(target,s=>(ulong?)s["input_epoch"]>epoch,"modifier-did-not-revoke-native-input");Click(x,y);Thread.Sleep(300);var excluded=target.State;Need((long)excluded["script_scene_revision"]==revision,"modifier-click-reached-native-script");long frame=(long)excluded["frame"];excluded=Wait(target,s=>(long?)s["frame"]>frame+5,"modifier-revoke-stopped-native-render");report["modifier_exclusion"]=excluded.DeepClone();}finally{InjectKey(16,true);}
  report["all_input_scope"]="Seven existing buttons down/up, held and repeated same-Tick queries, normalized pointer, both wheel directions and interleaved cross-button/key/wheel order; Shift revocation and click zero commands. Actual OS injection with explicit test foreground grant. No long input/physical-device/formal Launch/full exclusion claim.";
 }
 [STAThread]static int Main(string[] args){
  if(args.Length==1&&args[0]=="--owned-QA-leftover"){Thread.Sleep(30000);return 0;}
  if(args.Contains("--await-parent-job")&&Console.ReadLine()!="RUN")return 2;
  string root=Path.GetFullPath(args[1]);Directory.CreateDirectory(root);NativeDesktopTarget target=null;SupervisorRuntime runtime=null;JournalActor journal=null;
  int exclusionAt=Array.IndexOf(args,"--os-exclusion");string exclusion=exclusionAt<0?null:args[exclusionAt+1];bool osInput=args.Contains("--os-clicks")||args.Contains("--os-all-input")||exclusion!=null;CursorPoint previousCursor=new CursorPoint();bool restoreCursor=osInput&&GetCursorPos(out previousCursor);
  var report=new JObject{{"scope",osInput?"Actual OS injected input through original native collector/Broker/LPAC to Unity object; bounded development with explicit test foreground grant when selected, not physical device/formal Launch/full input/performance/RC":"real renderer-owned input role and LPAC script with original authenticated Supervisor; no OS input injection, displayed-image, formal Launch or performance claim"}};
  try{if(osInput)foreach(int key in new[]{1,2,16,17,18,32,37,38,39,40,91,92})Need((GetAsyncKeyState(key)&0x8000)==0,"OS-preexisting-held-input:"+key);using(var broker=new InputBroker()){
   journal=new JournalActor(Path.Combine(root,"journal"),RuntimeLimits.JournalPending);journal.Initialized.GetAwaiter().GetResult();var clock=Stopwatch.StartNew();var policy=new WatchdogPolicy(RuntimeLimits.StartupMs,RuntimeLimits.FrameMs,RuntimeLimits.StopMs,RuntimeLimits.ProcessBytes,RuntimeLimits.CpuWindowMs,RuntimeLimits.CpuBudgetMs,RuntimeLimits.ActualGpuBytes,RuntimeLimits.GpuStaleMs);runtime=new SupervisorRuntime(journal,new NoLaunch(),policy,2,RuntimeLimits.GlobalProcessBytes,()=>clock.ElapsedMilliseconds);
   var type=typeof(NativeInputRegistry).Assembly.GetType("SpatialWallpaper.Supervision.Host+Service",true);string descriptor=Path.Combine(root,"endpoint.json");
   using(var service=(IDisposable)type.GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single().Invoke(new object[]{runtime,journal,descriptor,null,null,null,broker})){
    try{
    var display=WindowsDesktopWindow.Displays()[0];var pose=new JObject{{"display_id",display.Id},{"width",.53},{"height",.53*display.Height/display.Width},{"keep_height",true},{"eye",new JArray(0,0,-.67)},{"fresh",false},{"paused",false}};
    string helper=Path.GetFullPath(args[3]),worker=Path.GetFullPath(args[4]),program=Path.GetFullPath(args[5]);var definition=JObject.Parse(File.ReadAllText(program));var inventory=JObject.Parse(File.ReadAllText(Path.Combine(worker,"build.json")));
    bool showcase=args.Contains("--showcase");
    var config=new JObject{{"artwork","showcase"},{"session",Guid.NewGuid().ToString("N")},{"generation",1},{"desktop",true},{"pose",pose},
     {"input",new JObject{{"helper",Path.GetFullPath(args[2])},{"cache",Path.Combine(root,"input")},{"descriptor",descriptor},{"region",new JArray(display.X,display.Y,display.Width,display.Height)}}},
     {"script",new JObject{{"node_paths",showcase?new JArray("InteriorContent_A/ReplaceableSlot","@scene/RealtimeSculptureKey"):new JArray("InteriorContent_A/ReplaceableSlot")},{"helper",helper},{"helper_sha256",Hash(helper)},{"bootstrap_cache",Path.Combine(root,"script-bootstrap")},
      {"launch",new JObject{{"worker_image",worker},{"worker_files",inventory["runtime_files"]},{"attempts",Path.Combine(root,"script-attempts")},{"attempt",Guid.NewGuid().ToString("N")},{"program",program},{"program_sha256",Hash(program)},{"build",definition["build_id"]}}}}}};
    target=new NativeDesktopTarget(args[0],Path.Combine(root,"render"),display.Id,config);Until(()=>target.Ready.IsCompleted,"native-render-input-ready",35000);target.AttachReady();
    var state=Wait(target,s=>(bool?)s["input_ready"]==true&&(long?)s["script_batches"]>=5,"renderer-owned-input-not-connected");Need(state["input_error"]?.Type==JTokenType.Null&&(int)state["render_errors"]==0,"renderer-input-or-render-error");
    if(showcase){
     var values=(JArray)state["script_node_values"];Need(values!=null&&values.Count==2&&(string)values[1]["kind"]=="light","original-showcase-light-binding");Need(Math.Abs((float)values[1]["state"]["intensity"]-2.5f)<.001f,"actual-original-light-native-intensity");
     var rotation=values[0]["state"]["rotation"].DeepClone();long batches=(long)state["script_batches"];
     state=Wait(target,s=>(long?)s["script_batches"]>batches+5,"showcase-continuous-script-motion");Need(!JToken.DeepEquals(rotation,state["script_node_values"][0]["state"]["rotation"]),"actual-original-object-rotation-continues");report["showcase_actual_values"]=state["script_node_values"].DeepClone();
    }
    Pin((int)state["input_pid"],(long)state["input_creation"]);Pin((int)state["script_pid"],(long)state["script_creation"]);PinCollectors(state);report["initial"]=state.DeepClone();
   bool crashed=args.Contains("--renderer-crash");
    if(args.Contains("--os-focus-grant")){Need(osInput,"foreground-grant-requires-OS-fixture");injectionFocusProcess=(uint)(int)state["input_pid"];report["test_only_injector_foreground_grant_pid"]=injectionFocusProcess;}
    if(osInput){
     if(exclusion!=null)OsExclusion(target,display,root,report,broker,Path.Combine(Path.GetDirectoryName(args[2]),"DesktopHitTester.exe"),exclusion,args.Contains("--qa-pre-revoke"),args.Contains("--os-exclusion-click"));else if(args.Contains("--os-all-input"))OsAllInput(target,display,root,report);else OsClicks(target,display,root,report);var close=target.CloseAsync();Need(close.Wait(20000)&&target.ResourcesReleased,"OS-input-renderer-cleanup-unproven");target=null;
    }else if(crashed){
     var surface=target.Ready.GetAwaiter().GetResult();using(var renderer=Process.GetProcessById(surface.ProcessId)){Need(PinnedProcessOwner.ReadCreationFileTime(renderer.Handle)==surface.CreationFileTime,"kill-only-original-renderer");renderer.Kill();Need(renderer.WaitForExit(5000),"renderer-kernel-exit");}
     var close=target.CloseAsync();try{close.Wait(25000);}catch(AggregateException){}Need(close.IsCompleted&&target.Error!=null&&target.ResourcesReleased,"crashed-renderer-input-cleanup-unproven:"+target.CleanupError);report["expected_renderer_error"]=target.Error;target=null;
    }else{
    ulong epoch=(ulong)state["input_epoch"];pose["paused"]=true;target.PublishPose(pose);state=Wait(target,s=>(bool?)s["input_ready"]==false&&(ulong?)s["input_epoch"]>epoch,"renderer-pause-did-not-revoke");
    // A pre-pause commit may still deliver its acknowledgement asynchronously.
    Thread.Sleep(150);state=target.State;Need((bool?)state["paused"]==true&&(float?)state["time_scale"]==0&&(bool?)state["audio_paused"]==true,"actual-owner-animation-audio-pause");
    long frame=(long)state["frame"];long pausedBatches=(long)state["script_batches"];var pausedValues=state["script_node_values"]?.DeepClone();
    // Stay paused beyond the ordinary script deadline. The actual renderer
    // must keep presenting while script state and original animation freeze.
    Thread.Sleep(1400);state=target.State;Need((long)state["script_batches"]==pausedBatches,"paused-script-still-advances");Need(JToken.DeepEquals(pausedValues,state["script_node_values"]),"paused-native-script-objects-change");Need((long)state["frame"]>frame+5&&state["script_error"]?.Type==JTokenType.Null,"paused-render-or-script-deadline");
    pose["paused"]=false;target.PublishPose(pose);state=Wait(target,s=>(bool?)s["input_ready"]==true&&(long?)s["script_batches"]>pausedBatches+5,"renderer-input-resume");PinCollectors(state);
    // Force both main-thread publications to coalesce at the metadata handoff.
    // The pause transition itself must still cross that handoff exactly once.
    epoch=(ulong)state["input_epoch"];var owner=typeof(NativeDesktopTarget).GetField("process",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);var gate=typeof(NativeRenderProcess).GetField("gate",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(owner);
    lock(gate){pose["paused"]=true;target.PublishPose(pose);pose["paused"]=false;target.PublishPose(pose);}
    state=Wait(target,s=>(bool?)s["input_ready"]==true&&(ulong?)s["input_epoch"]>epoch,"renderer-pose-handoff-lost-pause-transition");PinCollectors(state);
    using(var script=Process.GetProcessById((int)state["script_pid"])){
     Need(PinnedProcessOwner.ReadCreationFileTime(script.Handle)==(long)state["script_creation"],"kill-only-original-script");script.Kill();Need(script.WaitForExit(5000),"script-kernel-exit");
    }
    var fault=Wait(target,s=>s["script_error"]?.Type==JTokenType.String&&(bool?)s["input_ready"]==false,"script-fault-did-not-revoke-input");frame=(long)fault["frame"];state=Wait(target,s=>(long?)s["frame"]>frame+5,"script-fault-stopped-native-frames");Need((int)state["render_errors"]==0,"native-render-errors-after-script-fault");report["fault"]=state.DeepClone();
    var close=target.CloseAsync();Need(close.Wait(20000)&&target.ResourcesReleased,"renderer-owned-input-close-unproven");target=null;
    }
    foreach(var pin in pins)Need(pin.WaitForExit(5000),"owned-input-or-script-child-still-live");report["owned_children_exited"]=pins.Count;
    }finally{
     ReleasePrimary(report);
     // The authenticated Host must outlive native input unregistration, even
     // when an assertion fails. Disposing it first deadlocks cleanup retries.
     if(target!=null){var close=target.CloseAsync();try{if(!close.Wait(20000)||!target.ResourcesReleased)throw new Exception("owned-native-cleanup-before-host-release-unproven");target=null;}catch(Exception cleanup){report["cleanup_error"]=cleanup.ToString();throw;}}
    }
   }
  }if(args.Contains("--qa-leftover")){using(var leftover=Process.Start(new ProcessStartInfo(Assembly.GetExecutingAssembly().Location,"--owned-QA-leftover"){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden}))report["intentional_QA_leftover_pid"]=leftover.Id;}report["status"]=osInput?(exclusion!=null?"passed-native-OS-"+exclusion+"-"+(args.Contains("--os-exclusion-click")?"single-click":"hover-wheel")+"-exclusion-development":args.Contains("--os-all-input")?"passed-native-OS-existing-input-members-development":"passed-native-OS-primary-development-combination"):"passed-renderer-owned-input-lifecycle-only";report["checks"]=checks;return 0;
  }catch(Exception e){report["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}
  finally{
   ReleasePrimary(report);
   try{if(target!=null){var close=target.CloseAsync();if(!close.Wait(20000)||!target.ResourcesReleased)report["cleanup_error"]="renderer-owned-input-close-unproven";}}catch(Exception e){report["cleanup_error"]=e.ToString();}
   if(restoreCursor)report["cursor_restored"]=SetCursorPos(previousCursor.X,previousCursor.Y);report["test_only_injector_foreground_grants"]=injectionFocusGrants;foreach(var pin in pins)pin.Dispose();pins.Clear();if(runtime!=null){runtime.Close();runtime.Completion.GetAwaiter().GetResult();}if(journal!=null){journal.Close();journal.Completion.GetAwaiter().GetResult();}File.WriteAllText(Path.Combine(root,"report.json"),report.ToString());
  }
 }
}
