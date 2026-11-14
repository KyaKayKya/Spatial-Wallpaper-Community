using System;
using System.IO;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Threading;
using Newtonsoft.Json.Linq;

// QA-only elevated coordinator. Each official read-only capture is restricted
// to one original renderer kernel identity, with a fixed twenty-second limit.
static class PresentMonPhaseCaptureHost {
 static void Need(bool value,string why){if(!value)throw new Exception(why);}
 static JObject Read(string path){try{return JObject.Parse(File.ReadAllText(path));}catch(FileNotFoundException){return null;}catch(DirectoryNotFoundException){return null;}catch(Newtonsoft.Json.JsonReaderException){return null;}}
 static int Main(string[] args){
  string root=Path.GetFullPath(args[1]),log=Path.Combine(root,"presentmon-host.log");
  try{
   Need(new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator),"capture-not-elevated");
   using(var sha=SHA256.Create())using(var image=File.OpenRead(args[0])){
    Need(BitConverter.ToString(sha.ComputeHash(image)).Replace("-","").ToLowerInvariant()=="b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af","official-presentmon-image-drift");
    var total=Stopwatch.StartNew();
    foreach(string movement in new[]{"fixed","moving"})foreach(string mode in new[]{"no-bridge","bridge","script-fault"}){
     string folder=Path.Combine(root,movement+"-"+mode);JObject ready;
     while((ready=Read(Path.Combine(folder,"ready.json")))==null){Need(total.ElapsedMilliseconds<600000&&!File.Exists(Path.Combine(root,"abort")),"capture-phase-ready-deadline-or-abort");Thread.Sleep(25);}
     int seconds=(int?)ready["measurement_seconds"]??10;Need(seconds==10||seconds==60,"capture-fixed-development-duration");int captureSeconds=seconds+10;
     using(var renderer=Process.GetProcessById((int)ready["pid"])){
      var retained=renderer.Handle;
      Need(renderer.StartTime.ToFileTimeUtc()==(long)ready["state"]["creation_filetime"]&&!renderer.HasExited,"capture-original-renderer-identity");
      Need(string.Equals(Path.GetFullPath(renderer.MainModule.FileName),Path.GetFullPath(args[2]),StringComparison.OrdinalIgnoreCase),"capture-renderer-image-path");
      string output=Path.Combine(folder,"presents.csv"),phaseLog=Path.Combine(folder,"presentmon.log");
      File.WriteAllText(phaseLog,"elevated=True; renderer="+renderer.Id+"; creation="+renderer.StartTime.ToFileTimeUtc()+Environment.NewLine);
      var info=new ProcessStartInfo(Path.GetFullPath(args[0]),"--process_id "+renderer.Id+" --output_file \""+output+"\" --qpc_time_ms --session_name SpatialNativeBridge-"+Guid.NewGuid().ToString("N")+" --timed "+captureSeconds+" --terminate_after_timed --no_console_stats"){
       UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(Path.GetFullPath(args[0]))};
      using(var child=Process.Start(info)){
       object gate=new object();DataReceivedEventHandler receive=(sender,e)=>{if(e.Data!=null)lock(gate)File.AppendAllText(phaseLog,e.Data+Environment.NewLine);};
       child.OutputDataReceived+=receive;child.ErrorDataReceived+=receive;child.BeginOutputReadLine();child.BeginErrorReadLine();
       try{
        Thread.Sleep(2000);Need(!child.HasExited,"official-presentmon-start-failed");
        File.WriteAllText(Path.Combine(folder,"capture-armed"),"original renderer PID capture started; final CSV coverage still required");
        Need(child.WaitForExit((captureSeconds+10)*1000),"official-presentmon-phase-deadline");child.WaitForExit();
        File.AppendAllText(phaseLog,"official_exit="+child.ExitCode+Environment.NewLine);Need(child.ExitCode==0,"official-presentmon-nonzero-exit");
       }finally{if(!child.HasExited){child.Kill();Need(child.WaitForExit(5000),"official-presentmon-kernel-exit-unproven");}}
      }
     }
     File.AppendAllText(log,"completed "+movement+"-"+mode+Environment.NewLine);
    }
   }
   File.AppendAllText(log,"all-six-official-captures-exited"+Environment.NewLine);return 0;
  }catch(Exception error){File.AppendAllText(log,error.ToString());return 1;}
 }
}
