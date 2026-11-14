using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using System.Threading;
using SpatialWallpaper.Desktop;

static class DesktopNativeChecks {
 delegate IntPtr WindowProc(IntPtr window,uint message,IntPtr w,IntPtr l);
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct WindowClass{public uint Size,Style;public WindowProc Proc;public int ClassExtra,WindowExtra;public IntPtr Instance,Icon,Cursor,Background;public string Menu,Name;public IntPtr SmallIcon;}
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern ushort RegisterClassEx(ref WindowClass cls);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool UnregisterClass(string name,IntPtr instance);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr DefWindowProc(IntPtr window,uint message,IntPtr w,IntPtr l);
 [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern IntPtr CreateWindowEx(uint ex,string cls,string text,uint style,int x,int y,int w,int h,IntPtr parent,IntPtr menu,IntPtr instance,IntPtr param);
 [DllImport("user32.dll")]static extern bool DestroyWindow(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr window);
 [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr window);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 static void Check(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static string Wallpaper(){using(var key=Registry.CurrentUser.OpenSubKey("Control Panel\\Desktop"))return Convert.ToString(key.GetValue("WallPaper"))+"|"+Convert.ToString(key.GetValue("WallpaperStyle"))+"|"+Convert.ToString(key.GetValue("TileWallpaper"));}
 static int Main(string[] args){
  string before=Wallpaper();IntPtr window=CreateWindowEx(0,"STATIC","Spatial desktop native test",0x10CF0000,10,10,320,240,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
  try{
   WindowProc procedure=DefWindowProc;var cls=new WindowClass{Size=(uint)Marshal.SizeOf(typeof(WindowClass)),Name="UnityWndClass",Proc=procedure};Check(RegisterClassEx(ref cls)!=0,"registered-owned-Unity-window-fixture");
   IntPtr render=CreateWindowEx(0,"UnityWndClass","Render surface",0,20,20,800,640,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
   var ready=new ManualResetEvent(false);var stop=new ManualResetEvent(false);IntPtr helper=IntPtr.Zero;
   var auxiliary=new Thread(()=>{helper=CreateWindowEx(0,"UnityWndClass","Hidden graphics helper",0,20,20,128,128,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);ready.Set();stop.WaitOne();DestroyWindow(helper);});auxiliary.Start();
   try{Check(ready.WaitOne(5000)&&helper!=IntPtr.Zero,"created-hidden-auxiliary-window-on-render-thread");Check(WindowsDesktopWindow.FindOwnWindow(true)==render,"hidden-graphics-helper-cannot-replace-main-thread-render-surface");
    IntPtr sameThreadHelper=CreateWindowEx(0,"UnityWndClass","",0,20,20,128,128,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
    try{Check(sameThreadHelper!=IntPtr.Zero,"created-same-thread-untitled-Unity-helper");Check(WindowsDesktopWindow.FindOwnWindow(true)==render,"untitled-same-thread-helper-cannot-replace-player-surface");
    }finally{DestroyWindow(sameThreadHelper);}
   }
   finally{stop.Set();auxiliary.Join();ready.Dispose();stop.Dispose();}
   IntPtr activationHelper=CreateWindowEx(0,"UnityWndClass","",0,20,20,128,128,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero,IntPtr.Zero);
   try{using(var own=System.Diagnostics.Process.GetCurrentProcess())Check(WindowsDesktopWindow.ActivateManager(own.Id,own.MainModule.FileName)&&IsWindowVisible(render)&&!IsWindowVisible(activationHelper),"tray-activation-shows-manager-not-hidden-helper");}
   finally{DestroyWindow(activationHelper);DestroyWindow(render);UnregisterClass("UnityWndClass",IntPtr.Zero);GC.KeepAlive(procedure);}
   Check(window!=IntPtr.Zero,"created-owned-test-window");
   var displays=WindowsDesktopWindow.Displays();Check(displays.Length>0,"stable-display-identity");
   using(var desktop=new WindowsDesktopWindow()){
    bool rejected=false;try{desktop.Attach(window,"not-a-display");}catch(InvalidOperationException){rejected=true;}Check(rejected&&GetParent(window)==IntPtr.Zero,"offline-target-rejected-before-changing-window");
    var foreground=GetForegroundWindow();desktop.Attach(window,displays[0].Id);Check(desktop.Verify(),"actual-worker-parent-and-physical-monitor-rectangle");Check(GetForegroundWindow()==foreground,"attaching-does-not-steal-foreground");
    Check(GetParent(window)==desktop.Parent,"parent-confirmed");Check(!IsWindowEnabled(window),"desktop-window-does-not-consume-input");
    File.WriteAllText(Path.Combine(args[0],"native.txt"),"PASS parent="+desktop.Parent+" display="+desktop.DisplayId+" rectangle="+desktop.Bounds+"\n");
    desktop.Detach();Check(GetParent(window)==IntPtr.Zero&&IsWindowEnabled(window),"original-parent-and-input-restored");
   }
   using(var shell=new WindowsDesktopShell(_=>{}))Check(true,"native-tray-and-session-notifications-started");
   var layout=new DesktopDisplay{X=-1920,Y=0,Width=1920,Height=1080};Check(layout.Matches(-1920,0,1920,1080)&&!layout.Matches(0,0,1920,1080),"display-origin-changes-invalidate-old-attachment");
   Check(before==Wallpaper(),"windows-wallpaper-configuration-unchanged");return 0;
  }catch(Exception e){Console.Error.WriteLine(e);return 1;}finally{if(window!=IntPtr.Zero)DestroyWindow(window);}
 }
}
