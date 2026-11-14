using System;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;

namespace SpatialWallpaper.Desktop {
 // A separate native message loop receives shell/session notifications even when
 // Unity rendering is paused. Its callback must enqueue work onto Unity's thread.
 public sealed class WindowsDesktopShell:IDisposable,IDesktopShellState {
  delegate IntPtr WindowProc(IntPtr window,uint message,IntPtr w,IntPtr l);
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct WindowClass{public uint Size,Style;public WindowProc Proc;public int ClassExtra,WindowExtra;public IntPtr Instance,Icon,Cursor,Background;public string Menu,Name;public IntPtr SmallIcon;}
  [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
  [StructLayout(LayoutKind.Sequential)]struct Message{public IntPtr Window;public uint Id;public IntPtr W,L;public uint Time;public Point Position;}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct NotifyData{
   public uint Size;public IntPtr Window;public uint Id,Flags,Message;public IntPtr Icon;
   [MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Tip;
   public uint State,StateMask;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=256)]public string Info;public uint Version;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=64)]public string InfoTitle;public uint InfoFlags;public Guid Guid;public IntPtr Balloon;
  }
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern ushort RegisterClassEx(ref WindowClass cls);
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateWindowEx(uint ex,string cls,string name,uint style,int x,int y,int width,int height,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr DefWindowProc(IntPtr window,uint message,IntPtr w,IntPtr l);
  [DllImport("user32.dll")]static extern int GetMessage(out Message message,IntPtr window,uint min,uint max);
  [DllImport("user32.dll")]static extern bool TranslateMessage(ref Message message);
  [DllImport("user32.dll")]static extern IntPtr DispatchMessage(ref Message message);
  [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr window);
  [DllImport("user32.dll")]static extern void PostQuitMessage(int result);
  [DllImport("user32.dll")]static extern bool PostMessage(IntPtr window,uint message,IntPtr w,IntPtr l);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool UnregisterClass(string name,IntPtr instance);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern uint RegisterWindowMessage(string text);
  [DllImport("user32.dll")]static extern IntPtr LoadIcon(IntPtr instance,IntPtr name);
  [DllImport("shell32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool Shell_NotifyIcon(uint message,ref NotifyData data);
  [DllImport("user32.dll")]static extern IntPtr CreatePopupMenu();
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool AppendMenu(IntPtr menu,uint flags,UIntPtr id,string text);
  [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
  [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
  [DllImport("user32.dll")]static extern uint TrackPopupMenu(IntPtr menu,uint flags,int x,int y,int reserved,IntPtr owner,IntPtr rect);
  [DllImport("user32.dll")]static extern bool DestroyMenu(IntPtr menu);
  [DllImport("wtsapi32.dll",SetLastError=true)]static extern bool WTSRegisterSessionNotification(IntPtr window,uint flags);
  [DllImport("wtsapi32.dll")]static extern bool WTSUnRegisterSessionNotification(IntPtr window);
  readonly Action<string> dispatch;readonly Thread thread;readonly ManualResetEvent ready=new ManualResetEvent(false);WindowProc proc;IntPtr window;Exception failure;NotifyData icon;uint taskbarCreated;
  volatile bool hasWallpaper,manualPaused;
  public void SetWallpaperState(bool active,bool paused){hasWallpaper=active;manualPaused=paused;}
  public WindowsDesktopShell(Action<string> handler){dispatch=handler;thread=new Thread(Run){IsBackground=true,Name="Spatial desktop shell"};thread.Start();if(!ready.WaitOne(5000))throw new InvalidOperationException("desktop-shell-start-timeout");if(failure!=null)throw new InvalidOperationException("desktop-shell-start-failed",failure);}
  void Run(){
   string name="SpatialWallpaper.Desktop."+Guid.NewGuid().ToString("N");
   try{
    proc=Handle;var cls=new WindowClass{Size=(uint)Marshal.SizeOf(typeof(WindowClass)),Name=name,Proc=proc};if(RegisterClassEx(ref cls)==0)throw new Win32Exception();
    window=CreateWindowEx(0x80,name,"Spatial Wallpaper",0,0,0,0,0,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);if(window==IntPtr.Zero)throw new Win32Exception();
    if(!WTSRegisterSessionNotification(window,0))throw new Win32Exception();taskbarCreated=RegisterWindowMessage("TaskbarCreated");
    icon=new NotifyData{Size=(uint)Marshal.SizeOf(typeof(NotifyData)),Window=window,Id=1,Flags=1|2|4,Message=0x8001,Icon=LoadIcon(IntPtr.Zero,new IntPtr(32512)),Tip="Spatial Wallpaper"};AddIcon();ready.Set();
    while(GetMessage(out var message,IntPtr.Zero,0,0)>0){TranslateMessage(ref message);DispatchMessage(ref message);}
   }catch(Exception e){failure=e;ready.Set();dispatch("shell-error:"+e.GetType().Name);}
   finally{if(window!=IntPtr.Zero){Shell_NotifyIcon(2,ref icon);WTSUnRegisterSessionNotification(window);DestroyWindow(window);window=IntPtr.Zero;}UnregisterClass(name,IntPtr.Zero);}
  }
  void AddIcon(){if(!Shell_NotifyIcon(0,ref icon))throw new Win32Exception(Marshal.GetLastWin32Error(),"desktop-tray-unavailable");}
  IntPtr Handle(IntPtr hwnd,uint message,IntPtr w,IntPtr l){
   if(message==taskbarCreated&&taskbarCreated!=0){try{AddIcon();dispatch("explorer-changed");}catch(Exception e){dispatch("shell-error:"+e.GetType().Name);}return IntPtr.Zero;}
   switch(message){
    case 0x8001:
     if(l.ToInt64()==0x0203)dispatch("open");
     else if(l.ToInt64()==0x0205){var menu=CreatePopupMenu();try{string[] names={"打开管理器",manualPaused?"继续空间壁纸":"暂停画面与追踪","停止并恢复原壁纸","完全退出"};for(int i=0;i<names.Length;i++)AppendMenu(menu,(i==1||i==2)&&!hasWallpaper?1u:0u,new UIntPtr((uint)i+1),names[i]);GetCursorPos(out var p);SetForegroundWindow(hwnd);TrackPopupMenu(menu,0x0002,p.X,p.Y,0,hwnd,IntPtr.Zero);PostMessage(hwnd,0,IntPtr.Zero,IntPtr.Zero);}finally{DestroyMenu(menu);}}return IntPtr.Zero;
    case 0x0111:
     long choice=w.ToInt64()&0xffff;if(l==IntPtr.Zero&&choice>=1&&choice<=4)dispatch(new[]{"open","pause","stop","quit"}[choice-1]);return IntPtr.Zero;
    case 0x007e:dispatch("display-changed");break;
    case 0x02b1:if(w.ToInt64()==7)dispatch("locked");else if(w.ToInt64()==8)dispatch("unlocked");break;
    case 0x0218:if(w.ToInt64()==4)dispatch("suspended");else if(w.ToInt64()==7||w.ToInt64()==18)dispatch("resumed");return new IntPtr(1);
    case 0x0010:DestroyWindow(hwnd);return IntPtr.Zero;
    case 0x0002:PostQuitMessage(0);return IntPtr.Zero;
   }return DefWindowProc(hwnd,message,w,l);
  }
  public void Dispose(){if(window!=IntPtr.Zero)PostMessage(window,0x0010,IntPtr.Zero,IntPtr.Zero);if(Thread.CurrentThread!=thread&&!thread.Join(3000))throw new InvalidOperationException("desktop-shell-stop-timeout");ready.Dispose();}
 }
}
