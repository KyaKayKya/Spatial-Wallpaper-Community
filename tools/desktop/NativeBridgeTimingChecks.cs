using System;using System.IO;using System.Linq;using System.Diagnostics;using System.Threading;using System.Collections.Generic;using System.Security.Cryptography;using Newtonsoft.Json.Linq;using SpatialWallpaper.Desktop;using SpatialWallpaper.Sdk.Hosting;
// A bounded trusted parent for controlled same-asset development timings.
// No Tracker, tray, Library, raw camera, author execution or pixels live here.
static class NativeBridgeTimingChecks {
 static string Hash(string p){using(var h=SHA256.Create())return BitConverter.ToString(h.ComputeHash(File.ReadAllBytes(p))).Replace("-","").ToLowerInvariant();}
 static double Qpc()=>Stopwatch.GetTimestamp()*1000.0/Stopwatch.Frequency;
 static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
 static int Main(string[] a){if(Console.ReadLine()!="RUN")return 2;string root=Path.GetFullPath(a[1]);Directory.CreateDirectory(root);var report=new JObject{{"status","failed"},{"scope","Controlled original Showcase no bridge/bridge/script-fault, fixed or synthetic derived moving eye; real native Unity child, not formal Launch/physical tracker/RC"}};var cases=new JArray();report["cases"]=cases;NativeDesktopTarget target=null;
  try{int seconds=a.Length>5?int.Parse(a[5]):10;int vsync=a.Length>6?int.Parse(a[6]):-1;int queued=a.Length>7?int.Parse(a[7]):-1;Need(seconds==10||seconds==60,"timing-fixed-development-duration");Need(vsync==-1||vsync==0||vsync==1,"timing-vsync-policy");Need(queued==-1||queued==1||queued==2,"timing-queue-policy");var display=WindowsDesktopWindow.Displays(true)[0];Need(WindowsDesktopWindow.Displays().Length==1,"single-target-timing-fixture");var inventory=JObject.Parse(File.ReadAllText(Path.Combine(a[3],"build.json")));
   foreach(bool moving in new[]{false,true})foreach(string mode in new[]{"no-bridge","bridge","script-fault"}){
    string name=(moving?"moving-":"fixed-")+mode,folder=Path.Combine(root,name);Directory.CreateDirectory(folder);var row=new JObject{{"name",name},{"mode",mode},{"moving_eye",moving}};cases.Add(row);
    var pose=new JObject{{"display_id",display.Id},{"width",.53},{"height",.53*display.Height/display.Width},{"keep_height",true},{"eye",new JArray(0,0,-.67)},{"fresh",false},{"paused",false},{"presentation",new JObject{{"numerator",display.RefreshNumerator},{"denominator",display.RefreshDenominator}}}};
    var config=new JObject{{"artwork","showcase"},{"session",Guid.NewGuid().ToString("N")},{"generation",1},{"desktop",true},{"diagnostics",true},{"pose",pose}};
    if(vsync>=0)config["diagnostic_vsync"]=vsync;
    if(queued>0)config["diagnostic_queued_frames"]=queued;
    if(mode!="no-bridge")config["script"]=new JObject{{"node_paths",new JArray("InteriorContent_A/ReplaceableSlot","@scene/RealtimeSculptureKey")},{"helper",Path.GetFullPath(a[2])},{"helper_sha256",Hash(a[2])},{"bootstrap_cache",Path.Combine(folder,"bootstrap")},{"launch",new JObject{{"worker_image",Path.GetFullPath(a[3])},{"worker_files",inventory["runtime_files"]},{"attempts",Path.Combine(folder,"attempts")},{"attempt",Guid.NewGuid().ToString("N")},{"program",Path.GetFullPath(a[4])},{"program_sha256",Hash(a[4])},{"build",JObject.Parse(File.ReadAllText(a[4]))["build_id"]}}}};
    var pins=new List<Process>();
    try{
     target=new NativeDesktopTarget(a[0],Path.Combine(folder,"renderer"),display.Id,config);var timeout=Stopwatch.StartNew();while(!target.Ready.IsCompleted){Need(timeout.ElapsedMilliseconds<35000,"timing-native-ready");Thread.Sleep(5);}target.AttachReady();
     var surface=target.Ready.GetAwaiter().GetResult();var renderer=Process.GetProcessById(surface.ProcessId);Need(PinnedProcessOwner.ReadCreationFileTime(renderer.Handle)==surface.CreationFileTime,"timing-original-renderer-pin");pins.Add(renderer);JObject state=null;
     Action drive=()=>{Need(!File.Exists(Path.Combine(root,"abort")),"timing-parent-aborted");state=target.State;row["last_state"]=state.DeepClone();Need((int?)state["render_errors"]==0,"timing-render-error");if(mode=="bridge")Need(state["script_error"]?.Type!=JTokenType.String,"timing-healthy-script-faulted: "+state["script_error"]);};
     timeout.Restart();do{Thread.Sleep(10);drive();Need(timeout.ElapsedMilliseconds<15000,"timing-scene-warmup");}while((long?)state["frame"]<90||mode!="no-bridge"&&(long?)state["script_batches"]<8);
     if(mode!="no-bridge"){
      var script=Process.GetProcessById((int)state["script_pid"]);Need(PinnedProcessOwner.ReadCreationFileTime(script.Handle)==(long)state["script_creation"],"timing-original-script-pin");pins.Add(script);
      if(mode=="script-fault"){script.Kill();Need(script.WaitForExit(5000),"timing-fault-script-kernel-exit");long frame=(long)state["frame"];timeout.Restart();do{Thread.Sleep(10);drive();Need(timeout.ElapsedMilliseconds<5000,"timing-fault-render-did-not-continue");}while(state["script_error"]?.Type!=JTokenType.String||(long?)state["frame"]<=frame+5);}
     }
     int expectedSync=vsync<0?display.NativeVSyncCount:vsync;Need((int?)state["v_sync_count"]==expectedSync,"timing-vsync-policy-not-applied");if(queued>0||vsync<0&&expectedSync>0)Need((int?)state["max_queued_frames"]==(queued>0?queued:1),"timing-queue-policy-not-applied");row["before"]=state.DeepClone();row["renderer_pid"]=surface.ProcessId;row["renderer_creation_filetime"]=surface.CreationFileTime;
     File.WriteAllText(Path.Combine(folder,"ready.json"),new JObject{{"name",name},{"pid",surface.ProcessId},{"state",state},{"measurement_seconds",seconds},{"display",new JObject{{"x",display.X},{"y",display.Y},{"width",display.Width},{"height",display.Height}}}}.ToString());
     timeout.Restart();while(!File.Exists(Path.Combine(folder,"go"))){drive();Need(timeout.ElapsedMilliseconds<60000,"timing-parent-go-deadline");Thread.Sleep(10);}
     drive();row["measurement_before"]=state.DeepClone();row["start_qpc_ms"]=Qpc();var sample=Stopwatch.StartNew();double min=1,max=-1;var history=new JArray();long lastFrame=0;
     while(sample.ElapsedMilliseconds<seconds*1000){
      double x=moving?.08*Math.Sin(sample.Elapsed.TotalSeconds*2*Math.PI*.15):0;pose["eye"]=new JArray(x,0,-.67);target.PublishPose(pose);drive();double actual=(double)state["eye"][0];min=Math.Min(min,actual);max=Math.Max(max,actual);
      if((long)state["frame"]>lastFrame+5){lastFrame=(long)state["frame"];history.Add(new JObject{{"qpc_ms",Qpc()},{"frame",state["frame"]},{"eye_x",actual},{"script_batches",state["script_batches"]},{"gc0",state["gc0"]},{"gc1",state["gc1"]},{"gc2",state["gc2"]},{"last_gc_time",state["last_gc_time"]}});}Thread.Sleep(10);
     }
     row["end_qpc_ms"]=Qpc();row["after"]=state.DeepClone();row["actual_eye_min"]=min;row["actual_eye_max"]=max;row["history"]=history;
     Need((long)state["frame"]>(long)row["measurement_before"]["frame"]+seconds*30,"timing-insufficient-native-progress");if(moving)Need(min<-.07&&max>.07,"timing-moving-eye-not-presented");
     if(mode=="bridge")Need((long)state["script_batches"]>(long)row["measurement_before"]["script_batches"]+100,"timing-bridge-not-advancing");if(mode=="script-fault")Need(state["script_error"]?.Type==JTokenType.String,"timing-fault-not-observed");
     Need(JToken.DeepEquals(row["before"]["script_node_values"],state["script_node_values"]),"timing-same-value-script-mutated-original-scene");
     // Hold the moving-eye endpoint for its image, outside the measured window.
     pose["eye"]=new JArray(moving?.08:0,0,-.67);target.PublishPose(pose);Thread.Sleep(150);drive();row["image_state"]=state.DeepClone();File.WriteAllText(Path.Combine(folder,"sample.json"),row.ToString());
     timeout.Restart();while(!File.Exists(Path.Combine(folder,"captured"))){drive();Need(timeout.ElapsedMilliseconds<15000,"timing-parent-capture-deadline");Thread.Sleep(10);}
    }finally{
     if(target!=null){var close=target.CloseAsync();Need(close.Wait(25000)&&target.ResourcesReleased,"timing-native-close-unproven");target=null;}
     try{foreach(var pin in pins)Need(pin.WaitForExit(5000),"timing-owned-child-leftover");row["original_pins_exited"]=pins.Count;}finally{foreach(var pin in pins)pin.Dispose();}
    }
    File.WriteAllText(Path.Combine(root,"progress.json"),report.ToString());
   }
   report["status"]="controlled-native-bridge-timing-phases-complete-not-RC";return 0;
  }catch(Exception e){report["error"]=e.ToString();Console.Error.WriteLine(e);return 1;}
  finally{if(target!=null)try{target.CloseAsync().Wait(25000);}catch(Exception e){report["cleanup_error"]=e.ToString();}File.WriteAllText(Path.Combine(root,"report.json"),report.ToString());}
 }
}
