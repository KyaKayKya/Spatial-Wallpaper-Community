namespace SpatialWallpaper.Desktop {
 // Optional capability: the Windows tray uses live state to label/enable items.
 // The supplied Mac bridge exposes a fixed pause/resume action instead.
 public interface IDesktopShellState {
  void SetWallpaperState(bool active,bool paused);
 }
}
