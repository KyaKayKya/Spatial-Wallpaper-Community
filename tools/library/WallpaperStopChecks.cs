using System;
using System.IO;
using System.Threading.Tasks;
using SpatialWallpaper.Library;
class WallpaperStopChecks {
 sealed class Host:IWallpaperHost {
  public bool Available=>true;public string ActiveArtworkId{get;private set;}="pool";
  public int Calls;public TaskCompletionSource<WallpaperResult> Pending=new TaskCompletionSource<WallpaperResult>();
  public Task<WallpaperResult> ApplyAsync(WallpaperRequest request)=>throw new NotSupportedException();
  public async Task<WallpaperResult> StopAsync(){Calls++;var result=await Pending.Task;if(result.Succeeded)ActiveArtworkId=null;return result;}
 }
 static void Check(bool value,string name){if(!value)throw new Exception(name);}
 static void Main(){Run().GetAwaiter().GetResult();}
 static async Task Run(){
  var unavailable=new WallpaperStopAction(new UnavailableWallpaperHost());await unavailable.RestoreAsync();Check(!unavailable.Succeeded&&!unavailable.Busy&&!unavailable.CanRestore,"Unavailable host must not claim restore");
  var host=new Host();var action=new WallpaperStopAction(host);var pending=action.RestoreAsync();Check(action.Busy&&!action.CanRestore&&!action.Succeeded&&host.ActiveArtworkId=="pool","Pending stop must keep active state");
  await action.RestoreAsync();Check(host.Calls==1,"Duplicate stop");host.Pending.SetResult(new WallpaperResult(true,"ack"));await pending;
  Check(action.Succeeded&&!action.Busy&&host.ActiveArtworkId==null,"Acknowledged restore");await action.RestoreAsync();Check(host.Calls==1,"Repeated stop while inactive");
  var failedHost=new Host();var failed=new WallpaperStopAction(failedHost);var failure=failed.RestoreAsync();failedHost.Pending.SetResult(new WallpaperResult(false,"恢复失败，请重试"));await failure;Check(!failed.Succeeded&&!failed.Busy&&failedHost.ActiveArtworkId=="pool"&&failed.CanRestore&&failed.Message=="恢复失败，请重试","Failed restore must preserve wallpaper and retry");
  var brokenHost=new Host();var broken=new WallpaperStopAction(brokenHost);var error=broken.RestoreAsync();brokenHost.Pending.SetException(new IOException("连接已断开"));await error;Check(!broken.Succeeded&&!broken.Busy&&brokenHost.ActiveArtworkId=="pool"&&broken.CanRestore&&broken.Message.Contains("连接已断开"),"Transport error must remain visible");
  Console.WriteLine("PASS no-wallpaper pending/ack/failure/retry/unavailable/duplicate-stop checks");
 }
}
