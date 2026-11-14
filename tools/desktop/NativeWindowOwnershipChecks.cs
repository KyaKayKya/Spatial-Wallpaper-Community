using System;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Sdk.Hosting;

static class NativeWindowOwnershipChecks {
 static int checks;
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);checks++;}
 static int Main(string[] args){try{
  if(args.Length!=4)throw new ArgumentException("pid creation window render-thread required");
  int pid=int.Parse(args[0]);long birth=long.Parse(args[1]);var window=new IntPtr(long.Parse(args[2]));uint thread=uint.Parse(args[3]);
  using(var renderer=PinnedProcessOwner.Open(pid,birth))using(var current=PinnedProcessOwner.PinCurrent())using(var desktop=new WindowsDesktopWindow()){
   Need(WindowsDesktopWindow.MatchesPinnedChildWindow(window,renderer,thread),"actual-player-surface-not-owned");
   Need(!WindowsDesktopWindow.MatchesPinnedChildWindow(window,current,thread),"foreign-process-surface-accepted");
   Need(!WindowsDesktopWindow.MatchesPinnedChildWindow(window,renderer,thread+1),"wrong-unity-thread-accepted");
   Need(!WindowsDesktopWindow.MatchesPinnedChildWindow(window,renderer,0),"zero-thread-accepted");
   Need(!WindowsDesktopWindow.MatchesPinnedChildWindow(IntPtr.Zero,renderer,thread),"null-window-accepted");
   Need(!WindowsDesktopWindow.MatchesPinnedChildWindow(window,null,thread),"missing-owner-accepted");
   bool rejected=false;try{desktop.Attach(window,"must-not-access-explorer");}catch(InvalidOperationException error){rejected=error.Message=="desktop-window-not-owned";}Need(rejected,"normal-own-window-restriction-weakened");
   rejected=false;try{desktop.AttachPinnedChild(window,"must-not-access-explorer",renderer,thread+1);}catch(InvalidOperationException error){rejected=error.Message=="desktop-child-window-not-owned";}Need(rejected,"pinned-child-path-skipped-window-check");
   rejected=false;try{using(var wrong=PinnedProcessOwner.Open(pid,birth+1)){} }catch(System.IO.InvalidDataException){rejected=true;}Need(rejected,"wrong-renderer-creation-accepted");
  }
  Console.WriteLine("PASS "+checks+" actual Player HWND ownership checks; no desktop attachment or visible-image claim");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
