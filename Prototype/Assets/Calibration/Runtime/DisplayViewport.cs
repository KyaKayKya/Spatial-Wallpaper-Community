using System;
using System.Runtime.InteropServices;
using System.Text;
using UnityEngine;
using SpatialTracking;

// Physical desktop pixels, including when moving between DPI domains.
internal static class WindowsDisplayViewport {
 [StructLayout(LayoutKind.Sequential)] struct R {public int left,top,right,bottom;}
 [StructLayout(LayoutKind.Sequential)] struct P {public int x,y;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct M {public int size;public R monitor,work;public uint flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string device;}
 [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)] struct D {public int size;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=32)]public string name;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string text;public int flags;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string id;[MarshalAs(UnmanagedType.ByValTStr,SizeConst=128)]public string key;}
 [DllImport("user32.dll")] static extern IntPtr GetActiveWindow();
 delegate bool WindowVisitor(IntPtr handle,IntPtr context);
 [DllImport("user32.dll")] static extern bool EnumWindows(WindowVisitor visitor,IntPtr context);
 [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr handle,out uint process);
 [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr handle);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern int GetClassName(IntPtr handle,StringBuilder text,int count);
 [DllImport("user32.dll")] static extern bool IsWindow(IntPtr window);
 [DllImport("user32.dll")] static extern bool IsIconic(IntPtr window);
 [DllImport("user32.dll")] static extern IntPtr MonitorFromWindow(IntPtr window,uint flags);
 [DllImport("user32.dll")] static extern IntPtr MonitorFromRect(ref R rect,uint flags);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool GetMonitorInfo(IntPtr monitor,ref M info);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern bool EnumDisplayDevices(string device,uint index,ref D info,uint flags);
 [DllImport("user32.dll")] static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
 [DllImport("user32.dll")] static extern bool GetClientRect(IntPtr window,out R rect);
 [DllImport("user32.dll")] static extern bool ClientToScreen(IntPtr window,ref P point);
 [DllImport("user32.dll")] static extern uint GetDpiForWindow(IntPtr handle);
 static IntPtr window;
 internal static IntPtr RegisteredRenderWindow=>window;
 public static void RegisterRenderWindow(IntPtr ownedWindow){uint own=(uint)System.Diagnostics.Process.GetCurrentProcess().Id;GetWindowThreadProcessId(ownedWindow,out var pid);if(ownedWindow==IntPtr.Zero||pid!=own||!IsWindow(ownedWindow))throw new ArgumentException("render-window-not-owned");window=ownedWindow;}
 public static string Diagnostic{get;private set;}
 static void ResolveWindow(){
  if(window!=IntPtr.Zero&&IsWindow(window))return;
  uint own;using(var process=System.Diagnostics.Process.GetCurrentProcess())own=(uint)process.Id;
  Func<IntPtr,bool> renderWindow=handle=>{
   if(handle==IntPtr.Zero||!IsWindowVisible(handle))return false;GetWindowThreadProcessId(handle,out var id);if(id!=own)return false;
   var name=new StringBuilder(128);return GetClassName(handle,name,name.Capacity)>0&&name.ToString()=="UnityWndClass";
  };
  var active=GetActiveWindow();if(renderWindow(active)){window=active;return;}
  // A background Player has no active window on this thread. Resolve only its
  // own visible render window; never use another application's foreground.
  window=IntPtr.Zero;EnumWindows((handle,context)=>{if(!renderWindow(handle))return true;window=handle;return false;},IntPtr.Zero);
 }
 public static float UiScale{get{
  if(Application.platform!=RuntimePlatform.WindowsPlayer||Application.isEditor)return 1;
  ResolveWindow();
  float dpi=window==IntPtr.Zero?96:GetDpiForWindow(window);
  return Mathf.Max(.75f,Mathf.Min(Mathf.Max(1,dpi/96),Mathf.Min(Screen.width/900f,Screen.height/700f)));
 }}
 public static bool TryMeasure(Rect pixelRect,double monitorWidth,out ViewportMetrics metrics,out string display)=>TryMeasure(pixelRect,monitorWidth,out metrics,out display,out var ignored);
 public static bool TryMeasure(Rect pixelRect,double monitorWidth,out ViewportMetrics metrics,out string display,out int[] physicalRegion){
  metrics=null;display=null;physicalRegion=null;
  if(Application.isEditor||Application.platform!=RuntimePlatform.WindowsPlayer){
   display="editor-preview";
   return ViewportGeometry.TryMap(0,0,Screen.width,Screen.height,0,0,Screen.width,Screen.height,Screen.width,Screen.height,pixelRect.x,pixelRect.y,pixelRect.width,pixelRect.height,monitorWidth,out metrics);
  }
  ResolveWindow();
  if(window==IntPtr.Zero||IsIconic(window)){Diagnostic=window==IntPtr.Zero?"no-active-window":"window-minimized";return false;}
  IntPtr previous=SetThreadDpiAwarenessContext(new IntPtr(-4));
  try{
   var info=new M{size=Marshal.SizeOf(typeof(M))};var origin=new P();
   if(!GetClientRect(window,out var client)||!ClientToScreen(window,ref origin)){Diagnostic="client-rectangle-unavailable";return false;}
   double sx=(client.right-client.left)/(double)Math.Max(1,Screen.width),sy=(client.bottom-client.top)/(double)Math.Max(1,Screen.height);
   var preview=new R{left=(int)Math.Floor(origin.x+pixelRect.x*sx),right=(int)Math.Ceiling(origin.x+pixelRect.xMax*sx),top=(int)Math.Floor(origin.y+(Screen.height-pixelRect.yMax)*sy),bottom=(int)Math.Ceiling(origin.y+(Screen.height-pixelRect.y)*sy)};
   if(!GetMonitorInfo(MonitorFromRect(ref preview,2),ref info)){Diagnostic="monitor-unavailable";return false;}
   var device=new D{size=Marshal.SizeOf(typeof(D))};
   if(!EnumDisplayDevices(info.device,0,ref device,1)||string.IsNullOrEmpty(device.id)){Diagnostic="display-identity-unavailable";return false;}
   display=device.id;
   bool valid=ViewportGeometry.TryMap(info.monitor.left,info.monitor.top,info.monitor.right-info.monitor.left,info.monitor.bottom-info.monitor.top,origin.x,origin.y,client.right-client.left,client.bottom-client.top,Screen.width,Screen.height,pixelRect.x,pixelRect.y,pixelRect.width,pixelRect.height,monitorWidth,out metrics);
   if(valid)physicalRegion=new[]{preview.left,preview.top,preview.right-preview.left,preview.bottom-preview.top};
   Diagnostic=valid?"valid":FormattableString.Invariant($"invalid-viewport monitor={info.monitor.left},{info.monitor.top},{info.monitor.right},{info.monitor.bottom} client={origin.x},{origin.y},{client.right-client.left},{client.bottom-client.top} render={Screen.width},{Screen.height} viewport={pixelRect}");return valid;
  }finally{if(previous!=IntPtr.Zero)SetThreadDpiAwarenessContext(previous);}
 }
 public static bool TryMeasure(Rect pixelRect,float monitorWidth,out float width,out Vector3 center){
  bool ok=TryMeasure(pixelRect,(double)monitorWidth,out var m,out var display);width=ok?(float)m.Width:0;center=ok?new Vector3((float)m.CenterX,(float)m.CenterY,0):Vector3.zero;return ok;
 }
}

// Shared facade stays at this source path: SDK regression project builders copy it.
public static class DisplayViewport {
 internal static IntPtr RegisteredRenderWindow=>WindowsDisplayViewport.RegisteredRenderWindow;
 public static string Diagnostic{get;private set;}
 public static void RegisterRenderWindow(IntPtr ownedWindow)=>WindowsDisplayViewport.RegisterRenderWindow(ownedWindow);
 public static float UiScale{get{
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
  return SpatialWallpaper.Desktop.MacDesktopSurface.UiScale;
#else
  return WindowsDisplayViewport.UiScale;
#endif
 }}
 public static bool TryMeasure(Rect rect,double monitorWidth,out ViewportMetrics metrics,out string display){
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
  bool valid=SpatialWallpaper.Desktop.MacDesktopSurface.TryMeasure(rect,monitorWidth,out metrics,out display);Diagnostic=valid?"valid":"mac-viewport-unavailable";
#else
  bool valid=WindowsDisplayViewport.TryMeasure(rect,monitorWidth,out metrics,out display);Diagnostic=WindowsDisplayViewport.Diagnostic;
#endif
  return valid;
 }
 public static bool TryMeasure(Rect rect,double monitorWidth,out ViewportMetrics metrics,out string display,out int[] physicalRegion){
#if UNITY_STANDALONE_OSX && !UNITY_EDITOR
  physicalRegion=null;return TryMeasure(rect,monitorWidth,out metrics,out display);
#else
  bool valid=WindowsDisplayViewport.TryMeasure(rect,monitorWidth,out metrics,out display,out physicalRegion);Diagnostic=WindowsDisplayViewport.Diagnostic;return valid;
#endif
 }
 public static bool TryMeasure(Rect rect,float monitorWidth,out float width,out Vector3 center){
  bool valid=TryMeasure(rect,(double)monitorWidth,out var metrics,out var display);width=valid?(float)metrics.Width:0;center=valid?new Vector3((float)metrics.CenterX,(float)metrics.CenterY,0):Vector3.zero;return valid;
 }
}
