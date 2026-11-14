using System;using System.Diagnostics;using System.IO;using System.Threading;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;
static class AudioSelectionChecks {
 static void Need(bool yes,string reason){if(!yes)throw new Exception(reason);}
 static void Refused(Action action,string reason){try{action();}catch(InvalidDataException){return;}catch(ArgumentException){return;}catch(InvalidOperationException){return;}throw new Exception(reason);}
 static int Main(string[] args){if(args.Length>0&&args[0]=="--wait"){Thread.Sleep(30000);return 0;}try{
  var own=TrustedAudioSource.Capture(Process.GetCurrentProcess().Id);Need(JToken.DeepEquals(own,TrustedAudioSource.RequireLive(own)),"own-source-identity-drift");Need(TrustedAudioSource.RequireLive(null)==null,"no-source-is-not-silent");
  var stale=(JObject)own.DeepClone();stale["created"]=((long)ulong.Parse((string)own["created"])+1).ToString();Refused(()=>TrustedAudioSource.RequireLive(stale),"reused-process-identity-accepted");
  var extra=(JObject)own.DeepClone();extra["system_mix"]=true;Refused(()=>TrustedAudioSource.RequireLive(extra),"unknown-source-mode-accepted");
  var zero=(JObject)own.DeepClone();zero["process"]=0;Refused(()=>TrustedAudioSource.RequireLive(zero),"zero-source-accepted");
  using(var child=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--wait"){UseShellExecute=false,CreateNoWindow=true})){try{var selected=TrustedAudioSource.Capture(child.Id);child.Kill();Need(child.WaitForExit(5000),"selected-child-stop-unproven");Refused(()=>TrustedAudioSource.RequireLive(selected),"ended-source-accepted");}finally{if(!child.HasExited){child.Kill();child.WaitForExit();}}}
  File.WriteAllText(args[0],new JObject{{"own_identity",true},{"no_source",true},{"stale_identity_refused",true},{"unknown_mode_refused",true},{"zero_pid_refused",true},{"ended_source_refused",true}}.ToString());return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
