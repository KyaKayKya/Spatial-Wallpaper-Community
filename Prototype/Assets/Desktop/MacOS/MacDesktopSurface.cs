using System;
using System.Collections;
using System.Linq;
using System.IO;
using System.Runtime.InteropServices;
using Newtonsoft.Json.Linq;
using UnityEngine;
using SpatialTracking;
namespace SpatialWallpaper.Desktop {
 public sealed class MacDesktopSurface:IDesktopSurface {
  const string Plugin="SpatialMac";
  [DllImport(Plugin)]static extern int SWInitialize();
  [DllImport(Plugin)]static extern int SWPrepareStartup(int visible);
  [DllImport(Plugin)]static extern int SWAttach(string id);
  [DllImport(Plugin)]static extern int SWVerify();
  [DllImport(Plugin)]static extern void SWHide();
  [DllImport(Plugin)]static extern void SWDetach();
  [DllImport(Plugin)]static extern IntPtr SWDisplays();
  [DllImport(Plugin)]static extern IntPtr SWViewport();
  [DllImport(Plugin)]static extern int SWActivateManager(int owner);
  [DllImport(Plugin)]static extern int SWCovered();
  public static IEnumerator CompleteStartup(bool visible){
   float deadline=Time.realtimeSinceStartup+15;
   while(SWPrepareStartup(visible?1:0)==0){if(Time.realtimeSinceStartup>deadline){UnityEngine.Debug.LogError("mac-unity-startup-window-unavailable");Application.Quit(1);yield break;}yield return null;}
  }
  public JObject Diagnostics=>new JObject{["native_backend"]="appkit",["capability_warning"]="即时锁屏保护暂不可用；锁屏前请先在菜单栏完全退出。",["lock_detection"]="session-and-display-sleep-only"};
  public bool Initialize(){
   string library=Path.Combine(Application.dataPath,"Frameworks/libSpatialCamera.dylib");
   Environment.SetEnvironmentVariable("SPATIAL_MAC_CAMERA_LIBRARY",library);
   return SWInitialize()!=0;
  }
  public void Attach(string displayId){if(SWAttach(displayId)==0)throw new InvalidOperationException("mac-desktop-attach-failed");}
  public bool Verify()=>SWVerify()!=0;
  public bool Covered()=>SWCovered()!=0;
  public void Hide()=>SWHide();
  public void Detach()=>SWDetach();
  public void Dispose()=>Detach();
  public static bool ActivateManager(int owner)=>SWActivateManager(owner)!=0;
  static string Text(IntPtr text)=>Marshal.PtrToStringAnsi(text)??throw new InvalidOperationException("mac-native-result-unavailable");
  public static DesktopDisplay[] Displays()=>JArray.Parse(Text(SWDisplays())).Select(x=>new DesktopDisplay{Id=(string)x["id"],Device=(string)x["name"],X=(int)x["x"],Y=(int)x["y"],Width=(int)x["width"],Height=(int)x["height"]}).ToArray();
  public static float UiScale=>Mathf.Max(.75f,Mathf.Min(2,Mathf.Min(Screen.width/900f,Screen.height/700f)));
  static int Pixel(JToken value)=>checked((int)Math.Round((double)value));
  public static bool TryMeasure(Rect rect,double monitorWidth,out ViewportMetrics metrics,out string display){
   metrics=null;display=null;var v=JObject.Parse(Text(SWViewport()));if((bool?)v["valid"]!=true)return false;display=(string)v["id"];
   return ViewportGeometry.TryMap(Pixel(v["mx"]),Pixel(v["my"]),Pixel(v["mw"]),Pixel(v["mh"]),Pixel(v["x"]),Pixel(v["y"]),Pixel(v["width"]),Pixel(v["height"]),Screen.width,Screen.height,rect.x,rect.y,rect.width,rect.height,monitorWidth,out metrics);
  }
 }
 public sealed class MacDesktopShell:IDisposable {
  [UnmanagedFunctionPointer(CallingConvention.Cdecl)]delegate void NativeEvent(IntPtr text);
  [DllImport("SpatialMac")]static extern int SWStartShell(NativeEvent callback);
  [DllImport("SpatialMac")]static extern void SWStopShell();
  readonly NativeEvent callback;
  public MacDesktopShell(Action<string> dispatch){callback=text=>dispatch(Marshal.PtrToStringAnsi(text));if(SWStartShell(callback)==0)throw new InvalidOperationException("mac-shell-start-failed");}
  public void Dispose(){SWStopShell();GC.KeepAlive(callback);}
 }
}
