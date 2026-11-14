using System;using System.ComponentModel;using System.Drawing;using System.IO;using System.Runtime.InteropServices;using System.Threading.Tasks;using System.Windows.Forms;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Supervision.Interactive;
namespace SpatialWallpaper.Supervision.Presentation {
 // Reuses the E collector and its focus/hit/cleanup rules. The transparent
 // receiver is separate from the wallpaper child window, which never activates.
 public sealed class WindowsPresentedDesktopInput {
  sealed class Receiver:Form {
   protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80000|0x20|0x80;return p;}}
   protected override bool ShowWithoutActivation=>true;
  }
  [DllImport("user32.dll",SetLastError=true)]static extern bool SetLayeredWindowAttributes(IntPtr window,uint key,byte alpha,uint flags);
  [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
  [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out Rect rect);
  [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref Point point);
  readonly Control target;readonly WindowsFramePresenter presenter;readonly RuntimeSession runtime;readonly SupervisorManagerConnection connection;readonly string helper;readonly Receiver receiver;readonly Timer timer;
  WindowsDesktopInputSession input;Rectangle region;bool draining,released;volatile bool closing;string failure;
  public bool Ready=>!closing&&!draining&&input!=null&&input.Ready;public bool Focused=>Ready&&input.Focused;public bool ResourcesReleased=>released;public string Failure=>failure;
  public WindowsPresentedDesktopInput(Control target,WindowsFramePresenter presenter,RuntimeSession runtime,SupervisorManagerConnection connection,string helper){
   presenter.RequireInputBinding(target,runtime);if(!File.Exists(helper))throw new FileNotFoundException("desktop-input-helper-missing",helper);
   this.target=target;this.presenter=presenter;this.runtime=runtime;this.connection=connection;this.helper=helper;
   receiver=new Receiver{Text="Spatial Wallpaper input",FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(0,0,32,32)};
   try{if(!SetLayeredWindowAttributes(receiver.Handle,0,0,2))throw new Win32Exception(Marshal.GetLastWin32Error());receiver.Show();timer=new Timer{Interval=25};timer.Tick+=(s,e)=>Tick();timer.Start();Tick();}
   catch{receiver.Dispose();throw;}
  }
  void Tick(){try{
   if(closing){input?.Tick();return;}
   bool valid=presenter.TryGetPresentedRegion(out var current);
   if(input!=null&&(!valid||current!=region)&&!draining){draining=true;input.Dispose();}
   input?.Tick();
   if(draining){if(!input.ResourcesReleased)return;input=null;draining=false;}
   if(input==null&&valid){region=current;var handle=target.Handle;var bound=current;input=new WindowsDesktopInputSession(receiver.Handle,token=>new DesktopInputChannel(connection.OpenDesktopInputStream(runtime,token).GetAwaiter().GetResult()),helper,region.Left,region.Top,region.Width,region.Height,()=>BindingMatches(handle,bound));}
   if(input!=null&&input.State==InputRecoveryState.Faulted)failure=input.Failure??"desktop-input-faulted";
  }catch(Exception error){failure=error.GetBaseException().Message;}}
  bool BindingMatches(IntPtr handle,Rectangle bound){var origin=new Point();return !closing&&runtime.State==RuntimeSessionState.Running&&GetClientRect(handle,out var size)&&ClientToScreen(handle,ref origin)&&origin.X==bound.Left&&origin.Y==bound.Top&&size.Right-size.Left==bound.Width&&size.Bottom-size.Top==bound.Height;}
  public void Stop(){if(target.InvokeRequired)throw new InvalidOperationException("desktop-input-owner-thread");closing=true;input?.Dispose();}
  public async Task StopAsync(){
   if(released)return;Stop();
   if(input!=null){while(!input.Completion.IsCompleted){input.Tick();await Task.Delay(10);}if(!input.ResourcesReleased)await input.Completion;if(!input.ResourcesReleased)throw new InvalidOperationException("desktop-input-cleanup-unproven");input=null;}
   timer.Stop();timer.Dispose();receiver.Dispose();released=true;
  }
 }
}
