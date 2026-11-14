using System;
using System.Threading.Tasks;
namespace SpatialWallpaper.Library {
 // Desktop state belongs to the host; merely selecting the card never stops it.
 public sealed class WallpaperStopAction {
  readonly IWallpaperHost host;
  public bool Busy {get;private set;}
  public bool Succeeded {get;private set;}
  public string Message {get;private set;}="";
  public bool CanRestore=>!Busy&&host.Available&&host.ActiveArtworkId!=null;
  public event Action Changed;
  public WallpaperStopAction(IWallpaperHost host){this.host=host;}
  public async Task RestoreAsync(){
   if(Busy)return;
   if(!CanRestore){Succeeded=false;Message="当前未启用空间壁纸，系统壁纸保持原样。";Changed?.Invoke();return;}
   Busy=true;Succeeded=false;Message="正在停止空间壁纸并恢复 系统壁纸…";Changed?.Invoke();
   try{var result=await host.StopAsync();Succeeded=result.Succeeded;Message=result.Succeeded?"已停止空间壁纸，恢复原来的 系统壁纸。":result.Message;}
   catch(Exception error){Message="恢复失败："+error.Message;}
   finally{Busy=false;Changed?.Invoke();}
  }
 }
}
