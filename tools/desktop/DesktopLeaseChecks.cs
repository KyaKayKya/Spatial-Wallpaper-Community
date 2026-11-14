using System;
using System.IO;
using SpatialWallpaper.Desktop;
static class DesktopLeaseChecks {
 static int Probe(string file){using(var lease=SpatialTracking.RuntimeLease.TryAcquire(file))return lease==null?0:10;}
 static int Child(string file){var info=new System.Diagnostics.ProcessStartInfo(System.Diagnostics.Process.GetCurrentProcess().MainModule.FileName,"\""+System.Reflection.Assembly.GetExecutingAssembly().Location+"\" --probe \""+file+"\""){UseShellExecute=false};using(var process=System.Diagnostics.Process.Start(info)){process.WaitForExit();return process.ExitCode;}}
 static int Main(string[] args){if(args[0]=="--probe")return Probe(args[1]);string root=Path.Combine(args[0],"lease-"+Guid.NewGuid().ToString("N"));
  using(var first=DesktopManagerLease.TryAcquire(root,out var owner)){
   if(first==null)throw new Exception("first-manager-denied");
   var query=typeof(DesktopManagerLease).GetMethod("ActiveOwner");if(query==null)throw new Exception("missing-existing-manager-query");
   if((int)query.Invoke(null,new object[]{root})!=System.Diagnostics.Process.GetCurrentProcess().Id)throw new Exception("active-manager-query-missed-owner");
   if(Child(Path.Combine(root,"manager.lock"))!=0)throw new Exception("cross-process-lock-failed");
   using(var second=DesktopManagerLease.TryAcquire(root,out owner))if(second!=null||owner!=System.Diagnostics.Process.GetCurrentProcess().Id)throw new Exception("second-manager-was-not-rejected");
  }
  if((int)typeof(DesktopManagerLease).GetMethod("ActiveOwner").Invoke(null,new object[]{root})!=0)throw new Exception("closed-manager-stale-pid-returned");
  if(Child(Path.Combine(root,"manager.lock"))!=10)throw new Exception("cross-process-lock-not-released");
  using(var reopened=DesktopManagerLease.TryAcquire(root,out var owner))if(reopened==null)throw new Exception("closed-manager-cannot-reopen");
  var lifetime=typeof(DesktopManagerLease).Assembly.GetType("SpatialWallpaper.Desktop.DesktopLifetime");if(lifetime==null)throw new Exception("missing-explicit-full-exit-state");
  lifetime.GetMethod("RequestShutdown").Invoke(null,new object[]{root});
  if(!(bool)lifetime.GetMethod("ShutdownRequested").Invoke(null,new object[]{root}))throw new Exception("quit-not-visible-to-manager");
  lifetime.GetMethod("BeginLaunch").Invoke(null,new object[]{root});
  if((bool)lifetime.GetMethod("ShutdownRequested").Invoke(null,new object[]{root}))throw new Exception("explicit-relaunch-blocked-by-stale-quit");
  Console.WriteLine("PASS one-manager-owner-and-reopen-after-close");return 0;
 }
}
