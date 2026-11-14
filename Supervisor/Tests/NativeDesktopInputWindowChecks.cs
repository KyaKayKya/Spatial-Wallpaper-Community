using System;using System.IO;using System.Diagnostics;using System.Drawing;using System.Runtime.InteropServices;using System.Windows.Forms;using Newtonsoft.Json.Linq;
using SpatialWallpaper.Supervision.Interactive;using SpatialWallpaper.Supervision.Broker;
static partial class NativeDesktopInputHostChecks {
 static Form replacementReceiver;static int recoveryConnections;
 [DllImport("user32.dll")]static extern bool IsWindow(IntPtr window);
 sealed class ReleaseReceiver:Form{public int MouseUps;protected override void WndProc(ref Message message){if(message.Msg==0x202)MouseUps++;base.WndProc(ref message);}}
 static void CheckLateWindowConnection(string output,string helper,JObject identity,Rectangle area){
  // Task signals remain owned by a late factory even if the UI pump fails;
  // unlike disposable wait handles they cannot be invalidated by scope exit.
  var opened=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);var release=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);{
   DesktopInputChannel late=null;bool cancelled=false;IntPtr original=window.Handle;int before=recoveryConnections;
   recoverySession=new WindowsDesktopInputSession(original,token=>{System.Threading.Interlocked.Increment(ref recoveryConnections);late=new DesktopInputChannel(Open("OpenDesktopInput",identity));opened.TrySetResult(true);release.Task.GetAwaiter().GetResult();cancelled=token.IsCancellationRequested;return late;},helper,area.Left,area.Top,area.Width,area.Height);
   Exception primaryFailure=null;try{var start=Stopwatch.StartNew();while(!opened.Task.IsCompleted&&start.ElapsedMilliseconds<5000)Pump(1);Need(opened.Task.IsCompleted&&late!=null&&recoverySession.CurrentSource==null,"window-late-real-channel-open-before-source");
    window.Dispose();Need(!IsWindow(original),"window-late-receiver-actually-destroyed");Pump(20);Need(!recoverySession.Completion.IsCompleted,"window-late-pending-factory-still-owned");release.TrySetResult(true);
    var drain=Stopwatch.StartNew();while(!recoverySession.Completion.IsCompleted&&drain.ElapsedMilliseconds<3000)Pump(1);Need(recoverySession.Completion.IsCompleted,"window-late-session-closes-before-fallback");recoverySession.Completion.GetAwaiter().GetResult();
    Need(cancelled&&late.Completion.Status==System.Threading.Tasks.TaskStatus.RanToCompletion&&recoverySession.CurrentSource==null&&recoverySession.ResourcesReleased,"window-late-successful-channel-closed-without-native-source");Pump(300);Need(recoveryConnections==before+1&&!recoverySession.CanRetry,"window-late-no-reopen-or-retry");
    File.WriteAllText(Path.Combine(output,"window-late-connection.json"),new JObject{{"real_host_channel",true},{"cancel_observed",cancelled},{"source_created",false},{"late_channel_closed_before_fallback",true},{"opens",recoveryConnections-before}}.ToString());
   }catch(Exception error){primaryFailure=error;throw;}finally{release.TrySetResult(true);try{if(!recoverySession.Completion.IsCompleted){recoverySession.Dispose();var cleanup=Stopwatch.StartNew();while(!recoverySession.Completion.IsCompleted&&cleanup.ElapsedMilliseconds<6000)Pump(1);}Need(recoverySession.Completion.IsCompleted,"window-late-factory-owned-before-fixture-return");recoverySession.Completion.GetAwaiter().GetResult();}catch(Exception cleanup){Console.Error.WriteLine("window-late-fixture-cleanup:"+cleanup);if(primaryFailure==null)throw;}}
   replacementReceiver=new Receiver{Text="SDK replacement after late channel",FormBorderStyle=FormBorderStyle.None,Bounds=new Rectangle(0,0,32,32)};window=replacementReceiver;Need(SetLayeredWindowAttributes(window.Handle,0,0,2),"window-late-new-transparent-receiver");window.Show();
  }
 }
 static void CheckWindowDestruction(string mode,string output,string snapshot,string helper,JObject identity,Rectangle area,ref WindowsDesktopInput source,ref DesktopInputChannel channel,ref Process sourceHelper){
  bool held=mode=="mouse",explicitClose=mode=="close";var oldSession=recoverySession;var oldSource=source;var oldChannel=channel;IntPtr oldWindow=window.Handle;int opens=recoveryConnections;ulong epoch=channel.CaptureEpoch;
  Need(GetCursorPos(out var originalCursor),"window-boundary-original-cursor");
  BlankClick();var focused=Stopwatch.StartNew();while(!source.Focused&&focused.ElapsedMilliseconds<1000)Pump(1);Need(source.Focused,"window-boundary-old-source-focused");
  if(held){Send(new Event{Data=new Payload{Mouse=new Mouse{Flags=2}}});Need((uint)typeof(WindowsDesktopInput).GetField("consumed",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(source)==1,"window-boundary-down-was-consumed");}
  if(explicitClose)oldSession.Dispose();window.Dispose();Need(!IsWindow(oldWindow),"window-boundary-real-receiver-destroyed");Need(!oldSession.Ready&&!oldSession.Focused,"window-boundary-immediate-focus-revoked");
  Pump(100);
  if(held){Need(!oldSession.Completion.IsCompleted&&!oldSource.Completion.IsCompleted&&oldSession.State==InputRecoveryState.Draining,"window-boundary-drains-owned-up-after-HWND-destruction");
   using(var other=new ReleaseReceiver{Text="SDK paired release acceptance",StartPosition=FormStartPosition.Manual,Bounds=new Rectangle(400,100,260,180)}){other.Show();other.Activate();Need(SetForegroundWindow(other.Handle),"window-boundary-other-owned-window-focused");Need(SetCursorPos(other.Left+100,other.Top+100),"window-boundary-release-cursor");Pump(20);Need(Inject(new[]{new Event{Data=new Payload{Mouse=new Mouse{Flags=4}}}})==1,"window-boundary-paired-release-injected");Pump(50);Need(other.MouseUps==0,"window-boundary-no-orphan-up-to-other-window");}
  }
  var close=Stopwatch.StartNew();while(!oldSession.Completion.IsCompleted&&close.ElapsedMilliseconds<3000)Pump(1);Need(oldSession.Completion.IsCompleted,"window-boundary-session-closes-before-fallback");oldSession.Completion.GetAwaiter().GetResult();
  Need(oldSession.ResourcesReleased&&oldSource.Released&&oldChannel.Completion.Status==System.Threading.Tasks.TaskStatus.RanToCompletion&&sourceHelper.WaitForExit(1000),"window-boundary-all-owned-resources-released");
  Pump(400);Need(recoveryConnections==opens&&oldSession.State==InputRecoveryState.Closed,"window-boundary-no-reopen-on-dead-HWND");bool denied=false;try{oldSession.Retry();}catch(InvalidOperationException){denied=true;}Need(denied,"window-boundary-retry-permanently-rejected");
  replacementReceiver=new Receiver{Text="SDK independent replacement receiver",FormBorderStyle=FormBorderStyle.None,Bounds=new Rectangle(0,0,32,32)};window=replacementReceiver;Need(SetLayeredWindowAttributes(window.Handle,0,0,2),"window-boundary-new-transparent-receiver");window.Show();
  recoverySession=new WindowsDesktopInputSession(window.Handle,token=>{System.Threading.Interlocked.Increment(ref recoveryConnections);token.ThrowIfCancellationRequested();return new DesktopInputChannel(Open("OpenDesktopInput",identity));},helper,area.Left,area.Top,area.Width,area.Height);
  var ready=Stopwatch.StartNew();while(!recoverySession.Ready&&ready.ElapsedMilliseconds<7000)Pump(1);Need(recoverySession.Ready,"window-boundary-independent-new-session-ready");source=recoverySession.CurrentSource;channel=recoverySession.CurrentChannel;var replacementHelper=Process.GetProcessById(source.HelperProcessId);try{Need(replacementHelper.Handle!=IntPtr.Zero,"window-boundary-new-helper-observed");}catch{replacementHelper.Dispose();throw;}sourceHelper.Dispose();sourceHelper=replacementHelper;ObserveHeartbeat(source);
  oldSession.Dispose();Need(recoverySession.Ready&&channel.CaptureEpoch>epoch&&!source.Focused&&Revision(snapshot)==0,"window-boundary-old-session-cannot-affect-new-owner");
  Need(SetCursorPos(originalCursor.X,originalCursor.Y),"window-boundary-restore-blank-cursor");Pump(20);
  File.WriteAllText(Path.Combine(output,"window-boundary.json"),new JObject{{"mode",mode},{"old_hwnd",oldWindow.ToInt64()},{"new_hwnd",window.Handle.ToInt64()},{"numeric_hwnd_reused",oldWindow==window.Handle},{"old_epoch",epoch},{"new_epoch",channel.CaptureEpoch},{"old_session_closed",true},{"release_not_leaked",held},{"opens_before",opens},{"opens_after",recoveryConnections}}.ToString());
 }
}
