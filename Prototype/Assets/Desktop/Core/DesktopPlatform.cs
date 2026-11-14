using System;
using System.Collections;
using System.IO;
using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Desktop {
 // Business capabilities only; native window identities stay in adapters.
 public interface IDesktopSurface:IDisposable {
  JObject Diagnostics{get;}
  bool Initialize();
  void Attach(string displayId);
  bool Verify();
  bool Covered();
  void Hide();
  void Detach();
 }
 public static class DesktopPlatform {
  public static string LaunchExecutable(string playerExecutable){
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
   return playerExecutable;
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
   return Path.Combine(Path.GetDirectoryName(playerExecutable),"Launch.exe");
#else
   throw new PlatformNotSupportedException("Desktop launch requires a platform Player");
#endif
  }
  public static IEnumerator CompleteStartup(bool visible){
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
   return MacDesktopSurface.CompleteStartup(visible);
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
   return WindowsStartupFlow.Complete(visible);
#else
   throw new PlatformNotSupportedException("Desktop startup requires a platform Player");
#endif
  }
  public static IDesktopSurface CreateSurface(){
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
   return new MacDesktopSurface();
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
   return new WindowsDesktopSurface();
#else
   throw new PlatformNotSupportedException("桌面挂载只支持 Windows/macOS Player；编辑器使用普通预览");
#endif
  }
  public static IDisposable CreateShell(Action<string> dispatch){
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
   return new MacDesktopShell(dispatch);
#elif UNITY_STANDALONE_WIN && !UNITY_EDITOR
   return new WindowsDesktopShell(dispatch);
#else
   throw new PlatformNotSupportedException("桌面系统事件需要正式 Player");
#endif
  }
  public static DesktopDisplay[] Displays(){
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
   return MacDesktopSurface.Displays();
#else
   return WindowsDesktopWindow.Displays();
#endif
  }
  public static bool ActivateManager(int owner){
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
   return MacDesktopSurface.ActivateManager(owner);
#else
   return WindowsDesktopWindow.ActivateManager(owner);
#endif
  }
 }
}
