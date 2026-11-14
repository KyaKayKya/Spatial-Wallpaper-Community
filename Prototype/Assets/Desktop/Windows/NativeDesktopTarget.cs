using System;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Hosting;

namespace SpatialWallpaper.Desktop {
 // One display target, retained by DesktopRuntime. Window operations stay on
 // its owning thread; process startup/control/exit remain asynchronous.
 public sealed class NativeDesktopTarget {
  readonly NativeRenderProcess process;readonly WindowsDesktopWindow surface=new WindowsDesktopWindow();
  readonly int ownerThread=Thread.CurrentThread.ManagedThreadId;
  PinnedProcessOwner pin;NativeRenderSurface render;Task closing;
  public string DisplayId{get;}
  public Task<NativeRenderSurface> Ready=>process.Ready;
  public JObject State=>process.State;
  public string Error=>process.Error;
  public string CleanupError=>process.CleanupError;
  public bool ResourcesReleased=>closing!=null&&closing.IsCompleted&&process.ResourcesReleased&&pin==null&&surface.Window==IntPtr.Zero;
  public NativeDesktopTarget(string player,string cache,string displayId,JObject configuration){
   if(string.IsNullOrEmpty(displayId))throw new ArgumentException("native-target-display");
   DisplayId=displayId;process=new NativeRenderProcess(player,cache,configuration);
  }
  void OnOwnerThread(){if(Thread.CurrentThread.ManagedThreadId!=ownerThread)throw new InvalidOperationException("native-target-window-thread");}
  public void AttachReady(){
   OnOwnerThread();if(closing!=null)throw new InvalidOperationException("native-target-closing");
   if(Ready.Status!=TaskStatus.RanToCompletion)throw new InvalidOperationException("native-target-not-ready");
   if(render==null){var ready=Ready.GetAwaiter().GetResult();var owned=ready.Pin();render=ready;pin=owned;}
   surface.AttachPinnedChild(render.Window,DisplayId,pin,render.RenderThread);
  }
  public bool Verify(){OnOwnerThread();return surface.Verify();}
  public bool Covered(){OnOwnerThread();return surface.Covered();}
  public void Reconcile(){OnOwnerThread();if(!Verify())AttachReady();}
  public void PublishPose(JObject pose)=>process.PublishPose(pose);
  public Task CloseAsync(){
   OnOwnerThread();if(closing!=null)return closing;
   // Detach while the original renderer pin is retained and before requesting
   // its exit. If detach fails, retain the session so the owner can retry.
   surface.Detach();process.Stop();closing=ReleaseAsync();return closing;
  }
  async Task ReleaseAsync(){
   try{await process.Completion.ConfigureAwait(false);}
   finally{if(process.ResourcesReleased){pin?.Dispose();pin=null;}}
  }
 }
}
