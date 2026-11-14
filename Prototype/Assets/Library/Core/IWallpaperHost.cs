using System.Threading.Tasks;
namespace SpatialWallpaper.Library {
 // Implement with the future Runtime IPC client. Acknowledgement, not selection,
 // is the authority for ActiveArtworkId. Preview code never calls this boundary.
 public sealed class WallpaperRequest {
  public string ArtworkId {get;}
  public string DisplayId {get;}
  public string CalibrationPresetId {get;}
  public long CalibrationPresetRevision {get;}
  public WallpaperRequest(string artworkId,string displayId,string calibrationPresetId=null,long calibrationPresetRevision=0){ArtworkId=artworkId;DisplayId=displayId;CalibrationPresetId=calibrationPresetId;CalibrationPresetRevision=calibrationPresetRevision;}
 }
 public sealed class WallpaperResult {
  public bool Succeeded {get;}
  public string Message {get;}
  public WallpaperResult(bool succeeded,string message){Succeeded=succeeded;Message=message;}
 }
 public interface IWallpaperHost {
  bool Available {get;}
  string ActiveArtworkId {get;}
  Task<WallpaperResult> ApplyAsync(WallpaperRequest request);
  // Stop only this app's spatial wallpaper and reveal the user's previous system
  // wallpaper. Never select a bundled/system image or change its slideshow setup.
  // A successful response confirms restoration and clears ActiveArtworkId.
  Task<WallpaperResult> StopAsync();
 }
 public sealed class UnavailableWallpaperHost:IWallpaperHost {
  public bool Available=>false;
  public string ActiveArtworkId=>null;
  public Task<WallpaperResult> ApplyAsync(WallpaperRequest request)=>Task.FromResult(new WallpaperResult(false,"桌面壁纸功能尚未接入"));
  public Task<WallpaperResult> StopAsync()=>Task.FromResult(new WallpaperResult(false,"桌面壁纸功能尚未接入"));
 }
}
