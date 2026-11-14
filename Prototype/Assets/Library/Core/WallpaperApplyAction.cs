using System;
using System.Threading.Tasks;
namespace SpatialWallpaper.Library {
 public sealed class WallpaperApplyAction {
  readonly IWallpaperHost host;
  public bool Busy {get;private set;}
  public bool Succeeded {get;private set;}
  public string Message {get;private set;}
  public WallpaperRequest Request {get;private set;}
  public event Action Changed;
  public WallpaperApplyAction(IWallpaperHost host){this.host=host;}
  public async Task ApplyAsync(WallpaperRequest request){
   if(Busy)return;
   if(!host.Available){Succeeded=false;Message="桌面壁纸功能尚未接入";Changed?.Invoke();return;}
   Request=request;Busy=true;Succeeded=false;Message="正在应用…";Changed?.Invoke();
   try{var result=await host.ApplyAsync(request);Succeeded=result.Succeeded;Message=result.Succeeded?"已应用到桌面":result.Message;}
   catch(Exception error){Message="应用失败："+error.Message;}
   finally{Busy=false;Changed?.Invoke();}
  }
 }
}
