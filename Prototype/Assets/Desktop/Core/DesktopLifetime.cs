using System.IO;

namespace SpatialWallpaper.Desktop {
 // Explicit user shutdown survives the control socket closing. A fresh manager
 // launch clears it only after acquiring the single-manager lease.
 public static class DesktopLifetime {
  static string Notice(string root)=>Path.Combine(root,"shutdown.requested");
  public static void RequestShutdown(string root){Directory.CreateDirectory(root);File.WriteAllText(Notice(root),"user-full-exit");}
  public static bool ShutdownRequested(string root)=>File.Exists(Notice(root));
  public static void BeginLaunch(string root){File.Delete(Notice(root));}
 }
}
