using System;using System.Diagnostics;using System.IO;using System.Threading.Tasks;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Sdk.Editor {
 internal static class SandboxPreviewRecovery {
  // A missing/replaced PID proves the original process ended; access failures do
  // not. A final file still needs both original process identities to have ended.
  static Process Original(int id,long creation){Process process;try{process=Process.GetProcessById(id);}catch(ArgumentException){return null;}try{if(PinnedProcessOwner.ReadCreationFileTime(process.Handle)!=creation){process.Dispose();return null;}return process;}catch{process.Dispose();throw;}}
  internal static bool Ended(int id,long creation){using(var process=Original(id,creation))return process==null||process.HasExited;}
  internal static async Task ReleaseAsync(JObject record){
   int id=(int)record["viewer_pid"];long creation=(long)record["viewer_creation"];if(id<=0||creation<=0)throw new InvalidDataException("preview-recovery-owner");string workspace=Path.GetFullPath((string)record["workspace"]);Process host=null;using(var viewer=Original(id,creation))try{
    if(viewer!=null&&!viewer.HasExited)File.WriteAllText(Path.Combine(workspace,"stop-request"),"stop");var deadline=Stopwatch.StartNew();JObject status;
    while(true){status=PreviewStatusFile.Read(Path.Combine(workspace,"status.json"),id,creation);if(host==null&&status!=null&&(int)status["host_pid"]>0)host=Original((int)status["host_pid"],(long)status["host_creation_filetime"]);if(host!=null&&status!=null&&((int)status["host_pid"]!=host.Id||(long)status["host_creation_filetime"]!=PinnedProcessOwner.ReadCreationFileTime(host.Handle)))throw new InvalidDataException("preview-recovery-host-changed");
     if(viewer==null||viewer.HasExited)break;if(deadline.ElapsedMilliseconds>45000)throw new TimeoutException("preview-recovery-native-cleanup-deadline");await Task.Delay(25);
    }
    if(status==null||((string)status["state"]!="Stopped"&&(string)status["state"]!="Failed")||!(bool)status["window_resources_released"]||!(bool)status["host_resources_released"]||(bool)status["forced_host_termination"])throw new InvalidOperationException("preview-recovery-final-cleanup-unproven");
    while(host!=null&&!host.HasExited){if(deadline.ElapsedMilliseconds>45000)throw new TimeoutException("preview-recovery-host-cleanup-deadline");await Task.Delay(25);}
   }finally{host?.Dispose();}
  }
 }
}
