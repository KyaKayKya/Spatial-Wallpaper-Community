using System;
using System.Collections;
using UnityEngine;
namespace SpatialWallpaper.Desktop {
 public static class WindowsStartupFlow {
  public static IEnumerator Complete(bool visible){
   if(!long.TryParse(CalibrationRuntime.Argument("-parentHWND"),out var parent))yield break;
   IntPtr window=IntPtr.Zero;float deadline=Time.realtimeSinceStartup+15;
   while(Time.realtimeSinceStartup<deadline){window=WindowsDesktopWindow.FindOwnWindow(true,new IntPtr(parent));if(window!=IntPtr.Zero)break;yield return null;}
   if(window==IntPtr.Zero){UnityEngine.Debug.LogError("Unity delayed startup window unavailable");Application.Quit(1);yield break;}
   // This runs from managed Start, after graphics initialization. The documented
   // GWLP_USERDATA readiness bits remain zero in this 2022.3 Player (probe kept
   // in evidence), so do not use them as a startup gate or alter the HWND early.
   yield return null;yield return null;
   WindowsDesktopWindow.ReleaseStartupWindow(window,visible);
   if(visible&&!DesktopLifetime.ShutdownRequested(DesktopRuntime.DataRoot))WindowsDesktopWindow.ShowWindowWithoutActivation(window);
  }
 }
}
