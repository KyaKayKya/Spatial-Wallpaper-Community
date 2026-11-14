using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Sdk.Capabilities;
using SpatialWallpaper.Supervision.Broker;
using SpatialWallpaper.Supervision.Interactive;

static class NativeInputSourceChecks {
 static int checks;
 static readonly System.Collections.Generic.List<Process> helpers=new System.Collections.Generic.List<Process>();
 static void Need(bool ok,string why){if(!ok)throw new Exception(why);checks++;}
 static void PumpUntil(Func<bool> done,string why,int milliseconds=10000){var clock=Stopwatch.StartNew();while(!done()){if(clock.ElapsedMilliseconds>milliseconds)throw new TimeoutException(why);Application.DoEvents();Thread.Sleep(1);}}
 static void PinHelpers(WindowsNativeDesktopInput source){var ids=source.HelperProcessIds;Need(ids.Length==2&&ids[0]>0&&ids[1]>0&&ids[0]!=ids[1],"independent-input-helpers");foreach(int id in ids){var process=Process.GetProcessById(id);try{Need(process.Handle!=IntPtr.Zero,"input-helper-original-kernel-pin");helpers.Add(process);}catch{process.Dispose();throw;}}}
 [STAThread]static int Main(string[] args){
  string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);var report=new JObject{{"status","failed"},{"scope","real native-window input-source lifecycle using original collector and broker; no injected clicks, focus acceptance, ordered events or formal Launch claim"}};
  NativeDesktopTarget desktop=null;WindowsNativeDesktopInput source=null;InputTarget token=null;PinnedProcessOwner pin=null;object faultSource=null;System.Reflection.FieldInfo faultField=null;var serving=new System.Collections.Generic.List<Task>();var servers=new System.Collections.Generic.List<NamedPipeServerStream>();
  using(var broker=new InputBroker())try{
   var display=WindowsDesktopWindow.Displays()[0];var config=new JObject{{"artwork","showcase"},{"session",Guid.NewGuid().ToString("N")},{"generation",1},{"desktop",true},{"pose",new JObject{{"display_id",display.Id},{"width",.53},{"height",.53*display.Height/display.Width},{"keep_height",true},{"eye",new JArray(0,0,-.67)},{"fresh",false}}}};
   desktop=new NativeDesktopTarget(args[0],Path.Combine(output,"owned"),display.Id,config);PumpUntil(()=>desktop.Ready.IsCompleted,"renderer-ready",35000);desktop.AttachReady();var render=desktop.Ready.GetAwaiter().GetResult();pin=render.Pin();
   PumpUntil(()=>(int?)desktop.State["screen_width"]==display.Width,"renderer-resize");
   token=broker.Register(new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),true);
   Func<CancellationToken,DesktopInputChannel> connect=cancel=>{
    string name="SpatialWallpaper.NativeInputCheck."+Guid.NewGuid().ToString("N");var server=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous);lock(servers)servers.Add(server);
    var task=Task.Run(()=>{using(server){server.WaitForConnection();DesktopInputChannel.Serve(server,token);}});lock(serving)serving.Add(task);
    var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous);try{cancel.ThrowIfCancellationRequested();client.Connect(1000);cancel.ThrowIfCancellationRequested();return new DesktopInputChannel(client);}catch{client.Dispose();server.Dispose();throw;}
   };
   var region=new Rectangle(display.X,display.Y,display.Width,display.Height);
   bool wrong=false;try{new WindowsNativeDesktopInput(pin,render.Window,render.RenderThread+1,region,connect,args[2]);}catch(InvalidOperationException){wrong=true;}Need(wrong,"wrong-render-thread-accepted");
   var mismatched=new WindowsNativeDesktopInput(pin,render.Window,render.RenderThread,new Rectangle(region.X+1,region.Y,region.Width,region.Height),connect,args[2]);try{var watch=Stopwatch.StartNew();while(watch.ElapsedMilliseconds<200){Application.DoEvents();Thread.Sleep(1);}Need(mismatched.Idle&&!mismatched.Ready,"wrong-native-content-region-collected-input");var stop=mismatched.StopAsync();PumpUntil(()=>stop.IsCompleted,"mismatched-source-close");stop.GetAwaiter().GetResult();Need(mismatched.ResourcesReleased,"mismatched-source-leaked");}finally{var stop=mismatched.StopAsync();PumpUntil(()=>stop.IsCompleted,"mismatched-finally-close");stop.GetAwaiter().GetResult();}
   source=new WindowsNativeDesktopInput(pin,render.Window,render.RenderThread,region,connect,args[2]);PumpUntil(()=>source.Ready,"native-input-source-ready");Need(source.Ready,"original-collector-not-ready");
   PinHelpers(source);
   ulong before=BitConverter.ToUInt64(token.Snapshot(),8);source.SetEnabled(false);PumpUntil(()=>source.Idle,"paused-source-not-retired");Need(!source.Ready&&!source.Focused,"paused-source-retained-authority");Need(BitConverter.ToUInt64(token.Snapshot(),8)>before,"pause-did-not-revoke-broker-epoch");
   source.SetEnabled(true);PumpUntil(()=>source.Ready,"native-input-source-resume");Need(source.Ready,"resumed-source-not-ready");
   PinHelpers(source);
   if(args.Length>3&&args[3]=="--cleanup-fault"){
    var flags=System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic;var session=typeof(WindowsNativeDesktopInput).GetField("input",flags).GetValue(source);faultSource=session.GetType().GetProperty("CurrentSource",flags).GetValue(session);faultField=faultSource.GetType().GetField("removeHook",flags);faultField.SetValue(faultSource,new Func<IntPtr,int>(ignored=>5));
    var failing=source.StopAsync();PumpUntil(()=>failing.IsCompleted,"input-cleanup-fault-deadline");Need(failing.IsFaulted&&!source.ResourcesReleased,"failed-hook-cleanup-reported-success");faultField.SetValue(faultSource,null);
    var retry=source.RetryCleanupAsync();PumpUntil(()=>retry.IsCompleted,"input-cleanup-retry-deadline");retry.GetAwaiter().GetResult();Need(source.ResourcesReleased,"input-cleanup-retry-not-released");report["controlled_hook_cleanup_failure_and_retry"]=true;
   }
   var close=source.StopAsync();PumpUntil(()=>close.IsCompleted,"native-input-source-close");close.GetAwaiter().GetResult();Need(source.ResourcesReleased,"native-source-cleanup-unproven");Need(!token.GetFocusConfirmation().Focused,"closed-source-left-focus");
   foreach(var helper in helpers)Need(helper.WaitForExit(5000),"input-helper-kernel-exit-unproven");report["owned_helpers_exited"]=helpers.Count;
   var end=desktop.CloseAsync();PumpUntil(()=>end.IsCompleted,"renderer-close",17000);end.GetAwaiter().GetResult();Need(desktop.ResourcesReleased,"renderer-cleanup-unproven");
   report["status"]="passed-native-input-source-lifecycle-only";report["checks"]=checks;return 0;
  }catch(Exception e){report["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}
  finally{
   try{if(source!=null){faultField?.SetValue(faultSource,null);var close=source.StopAsync();if(close.IsFaulted)close=source.RetryCleanupAsync();PumpUntil(()=>close.IsCompleted,"source-finally-close");close.GetAwaiter().GetResult();}}catch(Exception e){report["cleanup_error"]=e.ToString();}
   token?.Dispose();lock(servers)foreach(var server in servers)server.Dispose();lock(serving)foreach(var task in serving)try{task.Wait(2000);}catch(AggregateException){}
   pin?.Dispose();try{if(desktop!=null){var end=desktop.CloseAsync();PumpUntil(()=>end.IsCompleted,"renderer-finally-close",17000);end.GetAwaiter().GetResult();}}catch(Exception e){report["renderer_cleanup_error"]=e.ToString();}
   foreach(var helper in helpers)helper.Dispose();helpers.Clear();
   File.WriteAllText(Path.Combine(output,"report.json"),report.ToString());
  }
 }
}
