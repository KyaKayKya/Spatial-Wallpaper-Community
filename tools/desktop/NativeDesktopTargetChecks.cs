using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Desktop;

static class NativeDesktopTargetChecks {
 static int checks;
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
 static int Main(string[] args){
  string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);NativeDesktopTarget target=null;var report=new JObject{{"status","failed"},{"scope","real native child desktop attachment; standalone trusted owner, not formal DesktopRuntime/Launch or visible image/performance"}};
  try{
   var display=WindowsDesktopWindow.Displays()[0];var config=new JObject{{"artwork","showcase"},{"session",Guid.NewGuid().ToString("N")},{"generation",1},{"desktop",true},{"pose",new JObject{{"display_id",display.Id},{"width",.53},{"height",.53*display.Height/display.Width},{"keep_height",true},{"eye",new JArray(0,0,-.67)},{"fresh",false}}}};
   target=new NativeDesktopTarget(args[0],Path.Combine(output,"owned"),display.Id,config);
   Need(target.Ready.Wait(35000),"target-ready-deadline");
   target.AttachReady();Need(target.Verify(),"target-not-attached");Need(target.DisplayId==display.Id,"target-display-identity");
   bool wrongThread=false;System.Threading.Tasks.Task.Run(()=>{try{target.Verify();}catch(InvalidOperationException e){wrongThread=e.Message=="native-target-window-thread";}}).GetAwaiter().GetResult();Need(wrongThread,"window-operation-allowed-on-background-thread");
   var wait=Stopwatch.StartNew();JObject state;
   do{state=target.State;if((int?)state["screen_width"]==display.Width&&(int?)state["screen_height"]==display.Height)break;Thread.Sleep(25);}while(wait.ElapsedMilliseconds<10000);
   Need((int?)state["screen_width"]==display.Width&&(int?)state["screen_height"]==display.Height,"actual-player-client-not-resized");report["attached_state"]=state;report["display"]=display.Id;
   target.Reconcile();Need(target.Verify(),"reconcile-lost-target");
   var close=target.CloseAsync();Need(!target.Verify(),"stop-did-not-detach-before-await");Need(close.Wait(17000),"target-close-deadline");Need(target.ResourcesReleased,"target-cleanup-unproven");
   target.CloseAsync().GetAwaiter().GetResult();Need(target.ResourcesReleased,"repeat-close-failed");
   bool reattach=false;try{target.AttachReady();}catch(InvalidOperationException e){reattach=e.Message=="native-target-closing";}Need(reattach,"closed-target-reattached");
   var cancelled=new NativeDesktopTarget(args[0],Path.Combine(output,"owned"),display.Id,config);try{Need(cancelled.CloseAsync().Wait(17000),"pre-ready-close-deadline");Need(cancelled.ResourcesReleased&&!cancelled.Verify(),"pre-ready-close-unproven");}finally{cancelled.CloseAsync().GetAwaiter().GetResult();}
   report["checks"]=checks;report["status"]="passed-native-target-attachment-not-formal-Launch";return 0;
  }catch(Exception error){report["error"]=error.ToString();Console.Error.WriteLine(error);return 1;}
  finally{if(target!=null)try{target.CloseAsync().GetAwaiter().GetResult();}catch(Exception e){report["cleanup_error"]=e.ToString();}File.WriteAllText(Path.Combine(output,"report.json"),report.ToString());}
 }
}
