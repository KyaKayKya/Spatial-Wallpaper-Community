using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using SpatialWallpaper.Sdk.Hosting;

namespace SpatialWallpaper.Desktop {
 public sealed class WindowsDesktopWindow:IDisposable {
  [StructLayout(LayoutKind.Sequential)]public struct Rectangle {
   public int Left,Top,Right,Bottom;
   public override string ToString()=>Left+","+Top+","+Right+","+Bottom;
  }
  [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct MonitorInfo{public int Size;public Rectangle Monitor,Work;public uint Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct DeviceInfo{public int Size;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Text;public int Flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string Key;}
  [StructLayout(LayoutKind.Sequential)]struct AdapterId{public uint Low;public int High;}
  [StructLayout(LayoutKind.Sequential)]struct Rational{public uint Numerator,Denominator;}
  [StructLayout(LayoutKind.Sequential)]struct PathSource{public AdapterId Adapter;public uint Id,Mode,Flags;}
  [StructLayout(LayoutKind.Sequential)]struct PathTarget{public AdapterId Adapter;public uint Id,Mode,Technology,Rotation,Scaling;public Rational Refresh;public uint ScanLine;public int Available;public uint Flags;}
  [StructLayout(LayoutKind.Sequential)]struct DisplayPath{public PathSource Source;public PathTarget Target;public uint Flags;}
  [StructLayout(LayoutKind.Explicit,Size=64)]struct DisplayMode{[FieldOffset(0)]public uint Type;}
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]struct SourceName{public uint Type,Size;public AdapterId Adapter;public uint Id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string Device;}
  delegate bool WindowVisitor(IntPtr window,IntPtr data);
  delegate bool MonitorVisitor(IntPtr monitor,IntPtr dc,ref Rectangle rect,IntPtr data);
  [DllImport("user32.dll")]static extern bool EnumWindows(WindowVisitor visit,IntPtr data);
  [DllImport("user32.dll")]static extern bool EnumChildWindows(IntPtr parent,WindowVisitor visit,IntPtr data);
  [DllImport("user32.dll")]static extern bool EnumDisplayMonitors(IntPtr dc,IntPtr clip,MonitorVisitor visit,IntPtr data);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool GetMonitorInfo(IntPtr monitor,ref MonitorInfo info);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool EnumDisplayDevices(string name,uint index,ref DeviceInfo info,uint flags);
  [DllImport("user32.dll")]static extern int GetDisplayConfigBufferSizes(uint flags,out uint paths,out uint modes);
  [DllImport("user32.dll")]static extern int QueryDisplayConfig(uint flags,ref uint paths,[Out]DisplayPath[] pathInfo,ref uint modes,[Out]DisplayMode[] modeInfo,IntPtr topology);
  [DllImport("user32.dll")]static extern int DisplayConfigGetDeviceInfo(ref SourceName source);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindow(string cls,string title);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr FindWindowEx(IntPtr parent,IntPtr after,string cls,string title);
  [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SendMessageTimeout(IntPtr window,uint message,IntPtr w,IntPtr l,uint flags,uint timeout,out IntPtr result);
  [DllImport("user32.dll",SetLastError=true)]static extern IntPtr SetParent(IntPtr child,IntPtr parent);
  [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr window);
  [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
  [DllImport("user32.dll")]static extern IntPtr GetDesktopWindow();
  [DllImport("user32.dll",EntryPoint="GetWindowLongPtrW",SetLastError=true)]static extern IntPtr GetStyle(IntPtr window,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongPtrW",SetLastError=true)]static extern IntPtr SetStyle(IntPtr window,int index,IntPtr value);
  [DllImport("user32.dll",EntryPoint="GetWindowLongW",SetLastError=true)]static extern int GetStyle32(IntPtr window,int index);
  [DllImport("user32.dll",EntryPoint="SetWindowLongW",SetLastError=true)]static extern int SetStyle32(IntPtr window,int index,int value);
  [DllImport("kernel32.dll")]static extern void SetLastError(uint error);
  [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
  [DllImport("user32.dll",SetLastError=true)]static extern bool SetWindowPos(IntPtr window,IntPtr after,int x,int y,int width,int height,uint flags);
  [DllImport("user32.dll")]static extern bool GetWindowRect(IntPtr window,out Rectangle rect);
  [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out Rectangle rect);
  [DllImport("user32.dll")]static extern bool ScreenToClient(IntPtr window,ref Point point);
  [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref Point point);
  [DllImport("user32.dll")]static extern bool EnableWindow(IntPtr window,bool enabled);
  [DllImport("user32.dll")]static extern bool IsWindowEnabled(IntPtr window);
  [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
  [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
  [DllImport("user32.dll")]static extern bool IsIconic(IntPtr window);
  [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,StringBuilder cls,int count);
  [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetWindowTextLength(IntPtr window);
  [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
  [DllImport("user32.dll")]static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
  [DllImport("user32.dll")]static extern bool ShowWindow(IntPtr window,int command);
  [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
  IntPtr window,originalParent,style,extended;Rectangle originalRect;bool wasEnabled;Func<bool> ownsWindow;
  public IntPtr Parent{get;private set;}
  public IntPtr Window=>window;
  public string DisplayId{get;private set;}
  public Rectangle Bounds{get;private set;}
  static Dictionary<string,Rational> RefreshRates(){
   var result=new Dictionary<string,Rational>(StringComparer.OrdinalIgnoreCase);
   for(int attempt=0;attempt<3;attempt++){
    if(GetDisplayConfigBufferSizes(2,out uint pathCount,out uint modeCount)!=0||pathCount>256||modeCount>1024)return result;
    var paths=new DisplayPath[pathCount];var modes=new DisplayMode[modeCount];int status=QueryDisplayConfig(2,ref pathCount,paths,ref modeCount,modes,IntPtr.Zero);if(status==122)continue;if(status!=0)return result;
    for(int index=0;index<pathCount;index++){
     var path=paths[index];if((path.Flags&1)==0||path.Target.Available==0)continue;
     var source=new SourceName{Type=1,Size=(uint)Marshal.SizeOf(typeof(SourceName)),Adapter=path.Source.Adapter,Id=path.Source.Id};
     if(DisplayConfigGetDeviceInfo(ref source)!=0||string.IsNullOrEmpty(source.Device))continue;
     var refresh=path.Target.Refresh;
     if(result.TryGetValue(source.Device,out var previous)&&((ulong)previous.Numerator*refresh.Denominator!=(ulong)refresh.Numerator*previous.Denominator||previous.Denominator==0))refresh=default(Rational);
     result[source.Device]=refresh;
    }
    return result;
   }
   return result;
  }
  public static DesktopDisplay[] Displays(bool includeRefresh=false){
   var result=new List<DesktopDisplay>();var refreshRates=includeRefresh?RefreshRates():new Dictionary<string,Rational>();var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
   try{EnumDisplayMonitors(IntPtr.Zero,IntPtr.Zero,(IntPtr monitor,IntPtr dc,ref Rectangle rect,IntPtr data)=>{
    var info=new MonitorInfo{Size=Marshal.SizeOf(typeof(MonitorInfo))};if(!GetMonitorInfo(monitor,ref info))throw new Win32Exception();
    refreshRates.TryGetValue(info.Device,out var refresh);var device=new DeviceInfo{Size=Marshal.SizeOf(typeof(DeviceInfo))};if(EnumDisplayDevices(info.Device,0,ref device,1)&&!string.IsNullOrEmpty(device.Id))result.Add(new DesktopDisplay{Id=device.Id,Device=info.Device,X=info.Monitor.Left,Y=info.Monitor.Top,Width=info.Monitor.Right-info.Monitor.Left,Height=info.Monitor.Bottom-info.Monitor.Top,RefreshNumerator=refresh.Numerator,RefreshDenominator=refresh.Denominator});return true;
   },IntPtr.Zero);return result.ToArray();}finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
  }
  public static void PrepareHiddenSurface(IntPtr window){
   GetWindowThreadProcessId(window,out var pid);if(window==IntPtr.Zero||pid!=Process.GetCurrentProcess().Id)throw new InvalidOperationException("desktop-bootstrap-window-not-owned");
   HideWindow(window);
   // Removing the delayed-start host frame can leave a 112x89 client, below
   // the native child Form minimum width. Normalize this hidden staging area
   // before SDK inspection; Attach later sets the actual monitor rectangle.
   if(!SetWindowPos(window,IntPtr.Zero,0,0,300,300,0x0002|0x0004|0x0010))throw new Win32Exception();
  }
  public static void ReleaseStartupWindow(IntPtr window,bool manager){
   HideWindow(window);SetLastError(0);SetParent(window,IntPtr.Zero);
   if(GetParent(window)!=IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error());
   long style=(ReadStyle(window,-16).ToInt64()&~(0x40000000L|0x80000000L))|(manager?0x00cf0000L:0x80000000L);
   SetLastError(0);if(SetStyle(window,-16,new IntPtr(style))==IntPtr.Zero&&Marshal.GetLastWin32Error()!=0)throw new Win32Exception();
   if(!SetWindowPos(window,IntPtr.Zero,0,0,0,0,0x0001|0x0002|0x0004|0x0010|0x0020))throw new Win32Exception();
  }
  public static IntPtr FindOwnWindow(bool includeHidden=false,IntPtr parent=default(IntPtr)){
   IntPtr found=IntPtr.Zero;uint own=(uint)Process.GetCurrentProcess().Id,thread=GetCurrentThreadId();
   // Unity also creates untitled hidden UnityWndClass helper windows, including
   // on the main thread. Only the titled Player surface is a render target.
   WindowVisitor inspect=(candidate,data)=>{uint candidateThread=GetWindowThreadProcessId(candidate,out var pid);var cls=new StringBuilder(128);if(pid==own&&candidateThread==thread&&GetWindowTextLength(candidate)>0&&(includeHidden||IsWindowVisible(candidate))&&GetClassName(candidate,cls,128)>0&&cls.ToString()=="UnityWndClass"){found=candidate;return false;}return true;};
   EnumWindows(inspect,IntPtr.Zero);if(found==IntPtr.Zero&&parent!=IntPtr.Zero)EnumChildWindows(parent,inspect,IntPtr.Zero);return found;
  }
  public static bool ActivateManager(int owner){
   using(var own=Process.GetCurrentProcess())return ActivateManager(owner,own.MainModule.FileName);
  }
  public static bool ActivateManager(int owner,string expectedImage){
   if(owner<=0)return false;
   try{using(var process=Process.GetProcessById(owner))if(!string.Equals(System.IO.Path.GetFullPath(process.MainModule.FileName),System.IO.Path.GetFullPath(expectedImage),StringComparison.OrdinalIgnoreCase))return false;}catch(ArgumentException){return false;}catch(InvalidOperationException){return false;}catch(Win32Exception){return false;}
   bool found=false;EnumWindows((candidate,data)=>{GetWindowThreadProcessId(candidate,out var pid);var cls=new StringBuilder(128);if(pid==(uint)owner&&GetWindowTextLength(candidate)>0&&GetClassName(candidate,cls,128)>0&&cls.ToString()=="UnityWndClass"){ShowWindow(candidate,IsIconic(candidate)?9:4);SetForegroundWindow(candidate);found=true;return false;}return true;},IntPtr.Zero);return found;
  }
  static IntPtr DesktopParent(){
   IntPtr progman=FindWindow("Progman",null);if(progman==IntPtr.Zero)throw new InvalidOperationException("explorer-desktop-unavailable");
   // This Explorer convention is bounded and deliberately isolated from content.
   if(SendMessageTimeout(progman,0x052c,IntPtr.Zero,IntPtr.Zero,2,1000,out var result)==IntPtr.Zero)throw new InvalidOperationException("explorer-desktop-timeout");
   SendMessageTimeout(progman,0x052c,new IntPtr(0xd),IntPtr.Zero,2,1000,out result);
   SendMessageTimeout(progman,0x052c,new IntPtr(0xd),new IntPtr(1),2,1000,out result);
   IntPtr worker=IntPtr.Zero;EnumWindows((top,data)=>{if(FindWindowEx(top,IntPtr.Zero,"SHELLDLL_DefView",null)==IntPtr.Zero)return true;worker=FindWindowEx(IntPtr.Zero,top,"WorkerW",null);return worker==IntPtr.Zero;},IntPtr.Zero);
   GetWindowThreadProcessId(progman,out var shellPid);GetWindowThreadProcessId(worker,out var workerPid);
   if(worker==IntPtr.Zero||!IsWindow(worker)||workerPid!=shellPid||FindWindowEx(worker,IntPtr.Zero,"SHELLDLL_DefView",null)!=IntPtr.Zero)throw new InvalidOperationException("desktop-background-layer-unavailable");return worker;
  }
  public void Attach(IntPtr ownWindow,string displayId){
   GetWindowThreadProcessId(ownWindow,out var pid);if(!IsWindow(ownWindow)||pid!=(uint)Process.GetCurrentProcess().Id)throw new InvalidOperationException("desktop-window-not-owned");
   AttachCore(ownWindow,displayId,()=>{GetWindowThreadProcessId(ownWindow,out var current);return IsWindow(ownWindow)&&current==pid;});
  }
  // Only DesktopRuntime's retained child-process owner may lend a render
  // surface. The normal own-process entry keeps its original restriction.
  public static bool MatchesPinnedChildWindow(IntPtr childWindow,PinnedProcessOwner renderer,uint renderThread){
   if(renderer==null||renderThread==0||renderer.HasExited||!IsWindow(childWindow))return false;uint thread=GetWindowThreadProcessId(childWindow,out var pid);var cls=new StringBuilder(128);
   return pid==(uint)renderer.Id&&thread==renderThread&&GetWindowTextLength(childWindow)>0&&GetClassName(childWindow,cls,128)>0&&cls.ToString()=="UnityWndClass";
  }
  public void AttachPinnedChild(IntPtr childWindow,string displayId,PinnedProcessOwner renderer,uint renderThread){
   if(renderer==null||renderThread==0)throw new ArgumentException("desktop-child-owner");
   Func<bool> matches=()=>MatchesPinnedChildWindow(childWindow,renderer,renderThread);
   if(!matches())throw new InvalidOperationException("desktop-child-window-not-owned");AttachCore(childWindow,displayId,matches);
  }
  void AttachCore(IntPtr ownWindow,string displayId,Func<bool> ownership){
   var display=Array.Find(Displays(),d=>d.Id==displayId);if(display==null)throw new InvalidOperationException("desktop-display-offline");var parent=DesktopParent();
   if(window!=IntPtr.Zero&&(window!=ownWindow||ownsWindow==null||!ownsWindow()))throw new InvalidOperationException("desktop-already-attached");
   if(window==IntPtr.Zero){window=ownWindow;ownsWindow=ownership;originalParent=GetParent(window);style=ReadStyle(window,-16);extended=ReadStyle(window,-20);GetWindowRect(window,out originalRect);wasEnabled=IsWindowEnabled(window);}
   var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
   try{
    WriteStyle(-16,(style.ToInt64()&~(0x80000000L|0x00cf0000L))|0x40000000L|0x10000000L);
    WriteStyle(-20,(extended.ToInt64()&~0x00040000L)|0x00000080L|0x08000000L);
    SetLastError(0);SetParent(window,parent);if(GetParent(window)!=parent)throw new Win32Exception(Marshal.GetLastWin32Error(),"desktop-set-parent-failed");
    Parent=parent;DisplayId=displayId;Bounds=new Rectangle{Left=display.X,Top=display.Y,Right=display.X+display.Width,Bottom=display.Y+display.Height};
    var origin=new Point{X=display.X,Y=display.Y};if(!ScreenToClient(parent,ref origin))throw new Win32Exception();
    EnableWindow(window,false);if(!SetWindowPos(window,IntPtr.Zero,origin.X,origin.Y,display.Width,display.Height,0x0010|0x0020|0x0040|0x0004))throw new Win32Exception();
    if(!Verify())throw new InvalidOperationException("desktop-native-verification-failed");
   }catch{Detach();throw;}finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
  }
  static IntPtr ReadStyle(IntPtr hwnd,int index)=>IntPtr.Size==8?GetStyle(hwnd,index):new IntPtr(GetStyle32(hwnd,index));
  void WriteStyle(int index,long value){SetLastError(0);var old=IntPtr.Size==8?SetStyle(window,index,new IntPtr(value)):new IntPtr(SetStyle32(window,index,unchecked((int)value)));int error=Marshal.GetLastWin32Error();if(old==IntPtr.Zero&&error!=0)throw new Win32Exception(error);}
  public bool Verify(){
   if(window==IntPtr.Zero||ownsWindow==null||!ownsWindow()||!IsWindow(Parent)||!IsWindowVisible(window)||!IsWindowVisible(Parent)||GetParent(window)!=Parent)return false;
   var current=Array.Find(Displays(),d=>d.Id==DisplayId);if(current==null||!current.Matches(Bounds.Left,Bounds.Top,Bounds.Right-Bounds.Left,Bounds.Bottom-Bounds.Top))return false;
   var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));try{
    if(!GetClientRect(window,out var client))return false;var origin=new Point();if(!ClientToScreen(window,ref origin))return false;
    return origin.X==Bounds.Left&&origin.Y==Bounds.Top&&client.Right-client.Left==Bounds.Right-Bounds.Left&&client.Bottom-client.Top==Bounds.Bottom-Bounds.Top;
   }finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
  }
  public bool Covered(){
   var foreground=GetForegroundWindow();if(foreground==IntPtr.Zero||foreground==window||foreground==Parent||!IsWindowVisible(foreground))return false;
   var cls=new StringBuilder(128);GetClassName(foreground,cls,128);if(cls.ToString()=="Progman"||cls.ToString()=="WorkerW")return false;
   var previous=SetThreadDpiAwarenessContext(new IntPtr(-4));try{return GetWindowRect(foreground,out var rect)&&rect.Left<=Bounds.Left&&rect.Top<=Bounds.Top&&rect.Right>=Bounds.Right&&rect.Bottom>=Bounds.Bottom;}finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
  }
  public void Hide(){if(window!=IntPtr.Zero&&ownsWindow!=null&&ownsWindow())ShowWindow(window,0);}
  public static void HideWindow(IntPtr ownWindow){GetWindowThreadProcessId(ownWindow,out var pid);if(pid!=(uint)Process.GetCurrentProcess().Id)throw new ArgumentException("window-not-owned");ShowWindow(ownWindow,0);}
  public static void ShowWindowWithoutActivation(IntPtr ownWindow){GetWindowThreadProcessId(ownWindow,out var pid);if(pid!=(uint)Process.GetCurrentProcess().Id)throw new ArgumentException("window-not-owned");ShowWindow(ownWindow,4);}
  public void Show(){if(window!=IntPtr.Zero&&ownsWindow!=null&&ownsWindow())ShowWindow(window,4);}
  public void Detach(){
   if(window==IntPtr.Zero)return;
   if(ownsWindow!=null&&ownsWindow()){
    ShowWindow(window,0);var restoreParent=IsWindow(originalParent)?originalParent:IntPtr.Zero;
    // NULL makes the desktop window the parent. Query the actual parent,
    // never a popup owner; WS_CHILD is restored only after this operation.
    SetLastError(0);SetParent(window,restoreParent);int parentError=Marshal.GetLastWin32Error();var expectedParent=restoreParent==IntPtr.Zero?GetDesktopWindow():restoreParent;
    if(GetAncestor(window,1)!=expectedParent)throw new Win32Exception(parentError,"desktop-detach-parent-failed");
    WriteStyle(-16,style.ToInt64());WriteStyle(-20,extended.ToInt64());EnableWindow(window,wasEnabled);
    if(!SetWindowPos(window,IntPtr.Zero,originalRect.Left,originalRect.Top,originalRect.Right-originalRect.Left,originalRect.Bottom-originalRect.Top,0x0010|0x0020|0x0004))throw new Win32Exception(Marshal.GetLastWin32Error(),"desktop-detach-position-failed");
   }
   window=Parent=IntPtr.Zero;DisplayId=null;ownsWindow=null;
  }
  public void Dispose(){Detach();}
 }
}
