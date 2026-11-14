using System;using Newtonsoft.Json.Linq;using SpatialWallpaper.Desktop;
namespace SpatialWallpaper.Content {
 // The content client uses the original native desktop attachment and shell.
 // Scene loading, applied revisions and sandbox ownership stay in ContentHost.
 public sealed class ContentDesktopRuntime:IDisposable {
  readonly WindowsDesktopWindow surface=new WindowsDesktopWindow();WindowsDesktopShell shell;readonly string display;
  public ContentDesktopRuntime(IntPtr window,string display,Action<string> dispatch){
   this.display=display;try{surface.Attach(window,display);shell=new WindowsDesktopShell(dispatch);}catch{surface.Dispose();throw;}
  }
  public bool Hidden{get;private set;}
  public JObject Snapshot=>new JObject{{"enabled",true},{"attached",surface.Verify()},{"display_id",display},{"window",surface.Window.ToInt64()},{"parent",surface.Parent.ToInt64()}};
  public bool Attached=>surface.Verify();public bool Covered=>surface.Covered();
  public void Reattach(){surface.Attach(surface.Window,display);if(Hidden)surface.Hide();}
  public void SetWallpaperState(bool active,bool paused)=>shell.SetWallpaperState(active,paused);
  public void Hide(){surface.Hide();Hidden=true;}public void Show(){surface.Show();Hidden=false;}
  public void Dispose(){shell?.Dispose();shell=null;surface.Dispose();}
 }
}
