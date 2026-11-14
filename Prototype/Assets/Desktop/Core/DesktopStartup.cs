using UnityEngine;
using SpatialWallpaper.Desktop;
public static class DesktopStartup {
 static bool Mac=>Application.platform==RuntimePlatform.OSXPlayer;
 public static bool Available=>Mac?!Application.isEditor&&string.IsNullOrEmpty(CalibrationRuntime.Argument("--calibration-smoke"))&&string.IsNullOrEmpty(CalibrationRuntime.Argument("--library-smoke")):WindowsDesktopStartup.Available;
 public static bool Enabled=>Mac?MacStartup.Enabled:WindowsDesktopStartup.Enabled;
 public static void Set(bool enabled){if(Mac)MacStartup.Set(enabled);else WindowsDesktopStartup.Set(enabled);}
}
