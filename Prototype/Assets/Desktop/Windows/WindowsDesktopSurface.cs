using System;
using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Desktop {
 public sealed class WindowsDesktopSurface:IDesktopSurface {
  readonly WindowsDesktopWindow native=new WindowsDesktopWindow();IntPtr window;
  public JObject Diagnostics=>new JObject{["window"]=window.ToInt64(),["parent"]=native.Parent.ToInt64()};
  public bool Initialize(){window=WindowsDesktopWindow.FindOwnWindow(true);if(window==IntPtr.Zero)return false;DisplayViewport.RegisterRenderWindow(window);WindowsDesktopWindow.PrepareHiddenSurface(window);return true;}
  public void Attach(string displayId){native.Attach(window,displayId);DisplayViewport.RegisterRenderWindow(window);}
  public bool Verify()=>native.Verify();
  public bool Covered()=>native.Covered();
  public void Hide(){WindowsDesktopWindow.HideWindow(window);}
  public void Detach()=>native.Detach();
  public void Dispose()=>native.Dispose();
 }
}
