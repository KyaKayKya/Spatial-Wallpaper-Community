using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Sdk.Hosting;

static class NativePreviewTargetChecks {
 [StructLayout(LayoutKind.Sequential)]struct Bounds {public int Left,Top,Right,Bottom;}
 [StructLayout(LayoutKind.Sequential)]struct Point {public int X,Y;}
 [DllImport("user32.dll")]static extern bool GetClientRect(IntPtr window,out Bounds bounds);
 [DllImport("user32.dll")]static extern bool ClientToScreen(IntPtr window,ref Point point);
 [DllImport("user32.dll")]static extern bool IsWindowVisible(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr GetParent(IntPtr window);
 [DllImport("user32.dll")]static extern IntPtr GetShellWindow();
 [DllImport("user32.dll")]static extern IntPtr SendMessage(IntPtr window,uint message,IntPtr first,IntPtr second);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr GetProp(IntPtr window,string name);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern IntPtr RemoveProp(IntPtr window,string name);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern bool SetProp(IntPtr window,string name,IntPtr value);
 [DllImport("kernel32.dll")]static extern IntPtr OpenThread(uint access,bool inherit,uint thread);
 [DllImport("kernel32.dll")]static extern uint GetProcessIdOfThread(IntPtr thread);
 [DllImport("kernel32.dll")]static extern uint SuspendThread(IntPtr thread);
 [DllImport("kernel32.dll")]static extern uint ResumeThread(IntPtr thread);
 [DllImport("kernel32.dll")]static extern bool GetExitCodeThread(IntPtr thread,out uint code);
 [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 sealed class WindowSuspension:IDisposable {
  IntPtr handle;PinnedProcessOwner pin;
  public WindowSuspension(NativeRenderSurface surface){pin=surface.Pin();try{handle=OpenThread(0x802,false,surface.RenderThread);if(handle==IntPtr.Zero||GetProcessIdOfThread(handle)!=(uint)pin.Id)throw new Exception("owned-window-thread-pin");uint previous=SuspendThread(handle);if(previous!=0){if(previous!=uint.MaxValue)ResumeThread(handle);throw new Exception("owned-window-thread-not-running");}}catch{if(handle!=IntPtr.Zero)CloseHandle(handle);handle=IntPtr.Zero;pin.Dispose();throw;}}
  public void Dispose(){IntPtr thread=Interlocked.Exchange(ref handle,IntPtr.Zero);if(thread==IntPtr.Zero)return;bool observed=GetExitCodeThread(thread,out uint code),resumed=observed&&(code!=259||ResumeThread(thread)==1),closed=CloseHandle(thread);pin.Dispose();if(!resumed||!closed)throw new Exception("owned-window-thread-release-unproven");}
 }
 static int checks;
 static void Need(bool value,string why){if(!value)throw new Exception(why);checks++;Console.WriteLine("PASS "+why);}
 static void Until(Func<bool> condition,string why,int timeout=10000){var clock=Stopwatch.StartNew();while(!condition()){if(clock.ElapsedMilliseconds>timeout)throw new TimeoutException(why);Application.DoEvents();Thread.Sleep(2);}Need(true,why);}
 static JObject Config()=>new JObject{{"artwork","showcase"},{"session",Guid.NewGuid().ToString("N")},{"generation",1},{"desktop",false},{"pose",new JObject{{"width",.53},{"height",.298125},{"keep_height",true},{"eye",new JArray(0,0,-.67)},{"fresh",false},{"paused",false}}}};
 static int[] Region(Form parent,int x=80,int y=60,int width=400,int height=220){var origin=parent.PointToScreen(new System.Drawing.Point(x,y));return new[]{origin.X,origin.Y,width,height};}
 static void Exact(NativePreviewTarget target,IntPtr parent,int[] crop){var surface=target.Ready.GetAwaiter().GetResult();Need(GetParent(surface.Window)==parent,"native-preview-owned-parent");Need(GetClientRect(surface.Window,out var rectangle)&&rectangle.Right==crop[2]&&rectangle.Bottom==crop[3],"native-preview-exact-client-pixels-no-caption");var origin=new Point();Need(ClientToScreen(surface.Window,ref origin)&&origin.X==crop[0]&&origin.Y==crop[1],"native-preview-exact-physical-crop-origin");Need(target.Verify(),"native-preview-retained-window-pin-verified");}
 [STAThread]static int Main(string[] args){NativePreviewTarget target=null;string output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);var report=new JObject{{"status","failed"},{"scope","Actual owned WinForms parent and complete native Unity child; no input/Library/quality/RC claim"}};
  if(Console.ReadLine()!="RUN")return 2;
  try{using(var parent=new Form{Text="Owned native preview target",ClientSize=new Size(640,360),Location=new System.Drawing.Point(80,80),StartPosition=FormStartPosition.Manual}){
   parent.Show();Application.DoEvents();bool foreign=false;try{new NativePreviewTarget(args[0],Path.Combine(output,"foreign"),GetShellWindow(),Config());}catch(ArgumentException){foreign=true;}Need(foreign,"native-preview-rejects-foreign-parent-before-start");
   target=new NativePreviewTarget(args[0],Path.Combine(output,"owned"),parent.Handle,Config());Until(()=>target.Ready.IsCompleted,"native-preview-target-ready",40000);var crop=Region(parent);var attachClock=Stopwatch.StartNew();target.AttachReady(crop);Need(attachClock.ElapsedMilliseconds<50,"native-preview-attach-does-not-wait-cross-process-window");Until(target.Verify,"native-preview-placement-asynchronously-confirmed");Exact(target,parent.Handle,crop);
   Until(()=>(int?)target.State["screen_width"]==crop[2]&&(int?)target.State["screen_height"]==crop[3],"native-preview-player-real-client-resolution");report["attached"]=target.State;
   bool outside=false;try{target.SetRegion(Region(parent,620,0,100,100));}catch(ArgumentException){outside=true;}Need(outside,"native-preview-rejects-crop-outside-owned-client");Exact(target,parent.Handle,crop);
   bool wrongThread=Task.Run(()=>{try{target.SetRegion(crop);return false;}catch(InvalidOperationException){return true;}}).GetAwaiter().GetResult();Need(wrongThread,"native-preview-rejects-wrong-window-thread");
   parent.Location=new System.Drawing.Point(180,140);Until(()=>!IsWindowVisible(target.Ready.GetAwaiter().GetResult().Window),"parent-move-hides-child-before-new-measurement");crop=Region(parent,40,40,500,260);target.SetRegion(crop);Until(target.Verify,"native-preview-resize-asynchronously-confirmed");Exact(target,parent.Handle,crop);Until(()=>IsWindowVisible(target.Ready.GetAwaiter().GetResult().Window),"new-measured-crop-restores-child");
   long frame=(long)target.State["frame"];Until(()=>(long?)target.State["frame"]>frame+5,"native-preview-frames-continue-after-resize");report["resized"]=target.State;
   long displayRevision=target.DisplayRevision;SendMessage(parent.Handle,0x7e,new IntPtr(32),new IntPtr((1080<<16)|1920));Need(target.DisplayRevision==displayRevision+1&&!target.Verify(),"controlled-display-change-revokes-old-preview-measurement");target.SetRegion(crop);Until(target.Verify,"controlled-display-change-requires-new-measurement-before-show");Exact(target,parent.Handle,crop);
   using(var heldPlacement=new WindowSuspension(target.Ready.GetAwaiter().GetResult())){
    var releasePlacement=Task.Run(()=>{Thread.Sleep(150);heldPlacement.Dispose();});target.SetRegion(Region(parent,40,40,460,250));crop=Region(parent,60,50,450,240);target.SetRegion(crop);Until(target.Verify,"pending-latest-region-completes-with-only-Verify-poll");releasePlacement.GetAwaiter().GetResult();Exact(target,parent.Handle,crop);
   }
   var closing=target.CloseAsync();Until(()=>closing.IsCompleted,"native-preview-target-close",25000);closing.GetAwaiter().GetResult();Need(target.ResourcesReleased,"native-preview-target-all-resources-released");bool retired=false;try{target.AttachReady(crop);}catch(InvalidOperationException){retired=true;}Need(retired,"native-preview-retired-owner-cannot-reattach");target=null;
   target=new NativePreviewTarget(args[0],Path.Combine(output,"lost-cookie"),parent.Handle,Config());Until(()=>target.Ready.IsCompleted,"cookie-target-ready",40000);target.AttachReady(crop);Until(target.Verify,"cookie-target-attached");
   var propertyField=typeof(NativePreviewTarget).GetField("lifetimeProperty",BindingFlags.Instance|BindingFlags.NonPublic);Need(propertyField!=null,"native-preview-window-lifetime-cookie-present");string property=(string)propertyField.GetValue(target);IntPtr child=target.Ready.GetAwaiter().GetResult().Window,cookie=GetProp(child,property);Need(cookie!=IntPtr.Zero&&RemoveProp(child,property)==cookie,"controlled-own-child-lifetime-cookie-removed");Need(!target.Verify(),"native-preview-missing-lifetime-cookie-refuses-unknown-window");
   var unknownClose=target.CloseAsync();Until(()=>unknownClose.IsCompleted,"native-preview-unknown-window-stops-original-process",25000);bool unknownRefused=false;try{unknownClose.GetAwaiter().GetResult();}catch(InvalidOperationException error){unknownRefused=error.Message=="native-preview-detach-identity-unproven";}Need(unknownRefused&&target.ResourcesReleased,"native-preview-unknown-window-error-and-kernel-release-proved");report["lost_cookie_error"]=target.Error;target=null;
   target=new NativePreviewTarget(args[0],Path.Combine(output,"blocked-window"),parent.Handle,Config());Until(()=>target.Ready.IsCompleted,"blocked-window-target-ready",40000);target.AttachReady(crop);Until(target.Verify,"blocked-window-target-attached");
   using(var blocked=new WindowSuspension(target.Ready.GetAwaiter().GetResult())){
    var release=Task.Run(()=>{Thread.Sleep(2600);blocked.Dispose();});var resizeClock=Stopwatch.StartNew();target.SetRegion(Region(parent,40,40,420,230));Need(resizeClock.ElapsedMilliseconds<50,"blocked-renderer-resize-returns-without-window-rpc-wait");var phase=typeof(NativePreviewTarget).GetField("windowPhase",BindingFlags.Instance|BindingFlags.NonPublic);Until(()=>(int)phase.GetValue(target)==3,"original-renderer-SetWindowPos-is-actually-outstanding");var stopClock=Stopwatch.StartNew();var blockedClose=target.CloseAsync();Need(stopClock.ElapsedMilliseconds<50,"blocked-renderer-close-returns-without-window-rpc-wait");
    long maximumPumpMs=0;var deadline=Stopwatch.StartNew();while(!blockedClose.IsCompleted&&deadline.ElapsedMilliseconds<25000){var pumping=Stopwatch.StartNew();Application.DoEvents();maximumPumpMs=Math.Max(maximumPumpMs,pumping.ElapsedMilliseconds);Thread.Sleep(2);}release.GetAwaiter().GetResult();report["blocked_window_maximum_parent_pump_ms"]=maximumPumpMs;Need(maximumPumpMs<100,"blocked-renderer-parent-message-pump-remains-responsive");Need(blockedClose.IsCompleted,"blocked-renderer-close-completed-after-owned-resume");bool deadlineRefused=false;try{blockedClose.GetAwaiter().GetResult();}catch(Exception){deadlineRefused=target.Error!=null&&target.Error.Contains("native-preview-window-deadline");}Need(deadlineRefused&&target.ResourcesReleased,"blocked-renderer-window-deadline-reported-and-original-kernel-released");report["blocked_window_error"]=target.Error;
   }target=null;
   target=new NativePreviewTarget(args[0],Path.Combine(output,"parent-cookie"),parent.Handle,Config());Until(()=>target.Ready.IsCompleted,"parent-cookie-target-ready",40000);var parentProperty=typeof(NativePreviewTarget).GetField("parentLifetimeProperty",BindingFlags.Instance|BindingFlags.NonPublic);Need(parentProperty!=null,"parent-window-lifetime-cookie-present");string parentCookie=(string)parentProperty.GetValue(target);
   using(var heldParent=new WindowSuspension(target.Ready.GetAwaiter().GetResult())){
    var releaseParent=Task.Run(()=>{Thread.Sleep(150);heldParent.Dispose();});target.AttachReady(crop);var phase=typeof(NativePreviewTarget).GetField("windowPhase",BindingFlags.Instance|BindingFlags.NonPublic);Until(()=>(int)phase.GetValue(target)==1,"old-placement-style-call-outstanding-before-parent-revocation");Need(RemoveProp(parent.Handle,parentCookie)!=IntPtr.Zero,"controlled-parent-lifetime-proof-revoked");releaseParent.GetAwaiter().GetResult();Until(()=>target.Error!=null,"late-old-placement-refuses-parent-lifetime-change");var failurePhase=typeof(NativePreviewTarget).GetField("failurePhase",BindingFlags.Instance|BindingFlags.NonPublic);Need((int)failurePhase.GetValue(target)==1&&GetParent(target.Ready.GetAwaiter().GetResult().Window)!=parent.Handle,"parent-lifetime-refused-before-SetParent");
   }
   var parentClose=target.CloseAsync();Until(()=>parentClose.IsCompleted,"revoked-parent-original-owner-close",25000);bool parentRefused=false;try{parentClose.GetAwaiter().GetResult();}catch(Exception){parentRefused=target.Error!=null&&target.Error.Contains("native-preview-parent-lifetime");}Need(parentRefused&&target.ResourcesReleased,"revoked-parent-error-preserved-and-resources-released");report["parent_cookie_error"]=target.Error;target=null;
   using(var destroyed=new Form{Text="Owned parent destruction",ClientSize=new Size(640,360)}){
    destroyed.Show();Application.DoEvents();target=new NativePreviewTarget(args[0],Path.Combine(output,"parent-destroyed"),destroyed.Handle,Config());Until(()=>target.Ready.IsCompleted,"destroyed-parent-target-ready",40000);target.AttachReady(Region(destroyed));Until(target.Verify,"destroyed-parent-target-attached");IntPtr oldParent=destroyed.Handle;destroyed.Close();Need(!destroyed.IsHandleCreated&&!target.Verify(),"real-parent-NCDESTROY-revokes-child");var destroyedClose=target.CloseAsync();Until(()=>destroyedClose.IsCompleted,"destroyed-parent-close-completed",25000);try{destroyedClose.GetAwaiter().GetResult();}catch(Exception error){report["destroyed_parent_close_error"]=error.ToString();}Need(target.ResourcesReleased,"real-parent-NCDESTROY-releases-original-owner");report["destroyed_parent_handle"]=oldParent.ToInt64();report["destroyed_parent_error"]=target.Error;target=null;
   }
   target=new NativePreviewTarget(args[0],Path.Combine(output,"cancelled"),parent.Handle,Config());var cancelled=target.CloseAsync();Until(()=>cancelled.IsCompleted,"native-preview-cancel-before-attach-close",25000);cancelled.GetAwaiter().GetResult();Need(target.ResourcesReleased,"native-preview-cancel-releases-original-owner");target=null;
   report["status"]="passed-native-preview-target-lifecycle-development";report["checks"]=checks;return 0;
  }}catch(Exception error){report["error"]=error.ToString();report["target_error"]=target?.Error;report["target_state"]=target?.State;Console.Error.WriteLine(error);return 1;}finally{if(target!=null)try{var cleanup=target.CloseAsync();Until(()=>cleanup.IsCompleted,"failure-cleanup",25000);cleanup.GetAwaiter().GetResult();Need(target.ResourcesReleased,"failure-cleanup-proven");}catch(Exception error){report["cleanup_error"]=error.ToString();}File.WriteAllText(Path.Combine(output,"report.json"),report.ToString());}
 }
}
