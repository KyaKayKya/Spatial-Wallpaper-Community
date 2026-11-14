using System;
using System.ComponentModel;
using System.Drawing;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Supervision.Broker;

namespace SpatialWallpaper.Supervision.Interactive {
 // The original collector and broker retain focus/hit-test authority. This
 // adapter replaces only the pixel presenter's region binding with a pinned
 // native Unity desktop window. It never collects input in the renderer.
 public sealed class WindowsNativeDesktopInput {
  sealed class Receiver:Form {
   protected override CreateParams CreateParams{get{var p=base.CreateParams;p.ExStyle|=0x80000|0x20|0x80;return p;}}
   protected override bool ShowWithoutActivation=>true;
  }
  [StructLayout(LayoutKind.Sequential)]struct Rect{public int Left,Top,Right,Bottom;}
  [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
  [DllImport("user32.dll",SetLastError=true)]static extern bool SetLayeredWindowAttributes(IntPtr window,uint key,byte alpha,uint flags);
  [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out Rect rect);
  [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref Point point);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder name,int capacity);
  [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr window);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string name,string title);
  [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  readonly int ownerThread=Thread.CurrentThread.ManagedThreadId;
  readonly PinnedProcessOwner renderer;readonly IntPtr window;readonly uint renderThread;readonly Rectangle region;
  readonly Func<CancellationToken,DesktopInputChannel> connect;readonly string helper;readonly Receiver receiver;readonly System.Windows.Forms.Timer timer;
  WindowsDesktopInputSession input;bool enabled=true,draining,closing,released;string failure;TaskCompletionSource<bool> stopped;Stopwatch cleanupRetry;
  public bool Ready=>!closing&&!draining&&enabled&&input!=null&&input.Ready;
  public bool Focused=>Ready&&input.Focused;
  public bool Idle=>input==null;
  public bool ResourcesReleased=>released;
  public string Failure=>failure;
  public int[] HelperProcessIds{get{Owner();var source=input?.CurrentSource;return source==null?new int[0]:new[]{source.HelperProcessId,source.HeartbeatHelperProcessId};}}
  void Owner(){if(Thread.CurrentThread.ManagedThreadId!=ownerThread)throw new InvalidOperationException("native-input-owner-thread");}
  static string Class(IntPtr value){var name=new StringBuilder(128);return GetClassName(value,name,name.Capacity)>0?name.ToString():null;}
  bool MatchesRenderer()=>!renderer.HasExited&&GetWindowThreadProcessId(window,out var pid)==renderThread&&pid==(uint)renderer.Id&&Class(window)=="UnityWndClass";
  bool BindingMatches(){
   if(closing||!enabled||!MatchesRenderer()||!IsWindowVisible(window))return false;
   var parent=GetParent(window);if(Class(parent)!="WorkerW"||!IsWindowVisible(parent)||FindWindowEx(parent,IntPtr.Zero,"SHELLDLL_DefView",null)!=IntPtr.Zero)return false;
   var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));try{var origin=new Point();return GetClientRect(window,out var size)&&ClientToScreen(window,ref origin)&&origin.X==region.Left&&origin.Y==region.Top&&size.Right-size.Left==region.Width&&size.Bottom-size.Top==region.Height;}finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
  }
  public WindowsNativeDesktopInput(PinnedProcessOwner renderOwner,IntPtr renderWindow,uint thread,Rectangle contentRegion,Func<CancellationToken,DesktopInputChannel> open,string hitHelper){
   if(renderOwner==null)throw new ArgumentNullException(nameof(renderOwner));if(open==null)throw new ArgumentNullException(nameof(open));
   if(contentRegion.Width<=0||contentRegion.Height<=0)throw new ArgumentException("native-input-region");if(!File.Exists(hitHelper))throw new FileNotFoundException("desktop-input-helper-missing",hitHelper);
   renderer=PinnedProcessOwner.Open(renderOwner.Id,renderOwner.CreationFileTime);window=renderWindow;renderThread=thread;region=contentRegion;connect=open;helper=hitHelper;
   try{
    if(thread==0||!MatchesRenderer())throw new InvalidOperationException("native-input-renderer-binding");
    receiver=new Receiver{Text="Spatial Wallpaper input",FormBorderStyle=FormBorderStyle.None,ShowInTaskbar=false,StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(0,0,32,32)};
    if(!SetLayeredWindowAttributes(receiver.Handle,0,0,2))throw new Win32Exception(Marshal.GetLastWin32Error());receiver.Show();
    timer=new System.Windows.Forms.Timer{Interval=25};timer.Tick+=(s,e)=>Tick();timer.Start();Tick();
   }catch{timer?.Dispose();receiver?.Dispose();renderer.Dispose();throw;}
  }
  public void SetEnabled(bool value){Owner();if(closing)throw new InvalidOperationException("native-input-closing");enabled=value;Tick();}
  void Tick(){Owner();try{
   if(closing){DrainStop();return;}
   bool valid=BindingMatches();if(input!=null&&!valid&&!draining){draining=true;input.Dispose();}
   input?.Tick();if(draining){if(!input.ResourcesReleased)return;input=null;draining=false;}
   if(input==null&&valid)input=new WindowsDesktopInputSession(receiver.Handle,connect,helper,region.Left,region.Top,region.Width,region.Height,BindingMatches);
   if(input!=null&&input.State==InputRecoveryState.Faulted)failure=input.Failure??"native-input-faulted";
  }catch(Exception error){failure=error.GetBaseException().Message;if(closing)stopped?.TrySetException(error);}}
  public Task StopAsync(){
   Owner();if(stopped!=null)return stopped.Task;stopped=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);closing=true;
   try{input?.Dispose();DrainStop();}catch(Exception error){failure=error.GetBaseException().Message;stopped.TrySetException(error);}return stopped.Task;
  }
  public Task RetryCleanupAsync(){
   Owner();if(!closing||stopped==null||!stopped.Task.IsFaulted)throw new InvalidOperationException("native-input-cleanup-retry-state");
   stopped=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
   cleanupRetry=Stopwatch.StartNew();
   try{input?.RetryCleanup();DrainStop();}catch(Exception error){failure=error.GetBaseException().Message;stopped.TrySetException(error);}return stopped.Task;
  }
  void DrainStop(){
   if(released||stopped.Task.IsCompleted)return;
   // Timer callbacks remain on the receiver UI thread. Do not depend on the
   // caller installing a SynchronizationContext for asynchronous shutdown.
   if(input!=null){input.Tick();if(!input.Completion.IsCompleted)return;if(!input.ResourcesReleased){
    // The original completion deliberately retains its first cleanup fault.
    // A retry must observe actual released resources as helpers finish, rather
    // than immediately replaying that already-recorded fault as its result.
    if(cleanupRetry!=null&&cleanupRetry.ElapsedMilliseconds<5000)return;
    input.Completion.GetAwaiter().GetResult();throw new InvalidOperationException("native-input-cleanup-unproven");
   }input=null;}
   timer.Stop();timer.Dispose();receiver.Dispose();renderer.Dispose();released=true;stopped.TrySetResult(true);
  }
 }
}
