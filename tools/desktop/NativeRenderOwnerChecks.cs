using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Security.Cryptography;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Sdk.Hosting;

static class NativeRenderOwnerChecks {
 static int checks;
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
 static JObject Pose(float x)=>new JObject{{"width",.53},{"height",.298125},{"keep_height",true},{"eye",new JArray(x,0,-.67)},{"fresh",false}};
 static JObject Config(string artwork)=>new JObject{{"artwork",artwork},{"session",Guid.NewGuid().ToString("N")},{"generation",1},{"pose",Pose(0)}};
 static string Hash(string path){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-","").ToLowerInvariant();}
 static void ScriptCases(string[] args,string output,JObject report){
  var records=new JArray();foreach(string mode in new[]{"budget-fault","healthy"}){
   string worker=Path.GetFullPath(args[2]),helper=Path.GetFullPath(args[3]),program=Path.Combine(Path.GetFullPath(args[4]),mode+".native-script.json");
   var config=Config("showcase");var inventory=JObject.Parse(File.ReadAllText(Path.Combine(worker,"build.json")));var definition=JObject.Parse(File.ReadAllText(program));
   config["script"]=new JObject{{"node_paths",new JArray("InteriorContent_A/ReplaceableSlot")},{"helper",helper},{"helper_sha256",Hash(helper)},{"bootstrap_cache",Path.Combine(output,"script-bootstrap")},
    {"launch",new JObject{{"worker_image",worker},{"worker_files",inventory["runtime_files"]},{"attempts",Path.Combine(output,"script-attempts")},{"attempt",Guid.NewGuid().ToString("N")},{"program",program},{"program_sha256",Hash(program)},{"build",definition["build_id"]}}}};
   using(var owner=new NativeRenderProcess(args[0],Path.Combine(output,"owned"),config))try{
    Need(owner.Ready.Wait(35000),"script-renderer-ready:"+mode);
    if(mode=="healthy"){
     var live=WaitState(owner,s=>(long?)s["script_batches"]>=5&&(bool?)s["script_running"]==true);
     using(var script=PinnedProcessOwner.Open((int)live["script_pid"],(long)live["script_creation"]))using(var process=Process.GetProcessById(script.Id)){Need(PinnedProcessOwner.ReadCreationFileTime(process.Handle)==script.CreationFileTime,"kill-only-owned-script");process.Kill();Need(process.WaitForExit(5000),"owned-script-kernel-exit");}
    }
    var stopped=WaitState(owner,s=>s["script_error"]?.Type==JTokenType.String);long before=(long)stopped["frame"];
    var continuing=WaitState(owner,s=>(long?)s["frame"]>before+5);Need(!owner.Completion.IsCompleted&&(int)continuing["render_errors"]==0,"script-fault-stopped-native-renderer:"+mode);
    records.Add(new JObject{{"case",mode},{"script_error",continuing["script_error"]},{"continued_frames",(long)continuing["frame"]-before},{"render_errors",continuing["render_errors"]}});
   }finally{Close(owner);}
  }
  report["script_cases"]=records;
 }
 static JObject WaitState(NativeRenderProcess owner,Func<JObject,bool> matches){var wait=Stopwatch.StartNew();while(wait.ElapsedMilliseconds<10000){var state=owner.State;if(matches(state))return state;if(owner.Completion.IsCompleted)throw new Exception("owner-ended:"+owner.Error+":"+owner.CleanupError);Thread.Sleep(10);}throw new TimeoutException("native-owner-state");}
 static void Close(NativeRenderProcess owner){if(owner==null)return;owner.Stop();Need(owner.Completion.Wait(17000),"native-owner-close-deadline");Need(owner.ResourcesReleased,"native-owner-close-unproven:"+owner.CleanupError);}
 static int Main(string[] args){NativeRenderProcess a=null,b=null;JObject report=new JObject{{"status","failed"},{"scope","production metadata owner and real render-only Players; no DesktopRuntime attachment/input/visible-image/performance claim"}};string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
  try{
   var clock=Stopwatch.StartNew();a=new NativeRenderProcess(args[0],Path.Combine(output,"owned"),Config("showcase"));b=new NativeRenderProcess(args[0],Path.Combine(output,"owned"),Config("mita"));Need(clock.ElapsedMilliseconds<500,"native-owner-construction-blocked-caller");
   Need(a.Ready.Wait(35000)&&b.Ready.Wait(35000),"real-renderer-ready-deadline");var first=a.Ready.Result;var second=b.Ready.Result;
   using(var pin=first.Pin())Need(WindowsDesktopWindow.MatchesPinnedChildWindow(first.Window,pin,first.RenderThread),"published-surface-kernel-identity");
   var initialA=WaitState(a,s=>(bool?)s["ready"]==true);var initialB=WaitState(b,s=>(bool?)s["ready"]==true);Need((string)initialA["pipeline"]=="ShowcaseURP"&&(string)initialB["pipeline"]=="Built-in","native-owner-pipeline-isolation");
   report["showcase"]=initialA;report["mita"]=initialB;a.PublishPose(Pose(.08f));
   var moved=WaitState(a,s=>s["eye"] is JArray eye&&Math.Abs((float)eye[0]-.08f)<.0001f);Need((int)moved["render_errors"]==0,"native-owner-pose-render-errors");report["moved_eye"]=moved["eye"].DeepClone();
   long before=(long)b.State["frame"];using(var pin=first.Pin())using(var child=Process.GetProcessById(first.ProcessId)){Need(PinnedProcessOwner.ReadCreationFileTime(child.Handle)==pin.CreationFileTime,"kill-only-original-owned-renderer");child.Kill();Need(child.WaitForExit(5000),"owned-renderer-kernel-exit");}
   var survivor=WaitState(b,s=>(long?)s["frame"]>before+5);Need((int)survivor["render_errors"]==0,"other-renderer-failed-after-peer-exit");report["survivor"]=survivor;
   try{a.Completion.Wait(17000);}catch(AggregateException){}Need(a.Completion.IsCompleted&&a.Error!=null&&a.ResourcesReleased,"crashed-renderer-cleanup-must-be-observed");report["owned_crash_error"]=a.Error;
   Close(b);Need(!File.Exists(b.DiagnosticLog),"healthy-renderer-cache-not-retired");
   using(var cancelled=new NativeRenderProcess(args[0],Path.Combine(output,"owned"),Config("showcase"))){cancelled.Stop();Close(cancelled);Need(cancelled.Ready.IsCanceled,"stopped-before-ready-was-published");}
   if(args.Length==5)ScriptCases(args,output,report);
   report["status"]="passed-native-owner-process-control-not-DesktopRuntime";report["checks"]=checks;return 0;
  }catch(Exception error){report["error"]=error.ToString();Console.Error.WriteLine(error);return 1;}
  finally{
   foreach(var owner in new[]{b,a})if(owner!=null){owner.Stop();try{owner.Completion.Wait(17000);}catch{}if(!owner.ResourcesReleased)report["cleanup_error"]=owner.CleanupError??"owner-not-completed";}
   File.WriteAllText(Path.Combine(output,"report.json"),report.ToString());
  }
 }
}
