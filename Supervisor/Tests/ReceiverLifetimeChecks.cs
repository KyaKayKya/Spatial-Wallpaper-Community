using System;using System.Threading;using System.Diagnostics;using System.Windows.Forms;using SpatialWallpaper.Supervision.Interactive;
static class ReceiverLifetimeChecks {
 sealed class Window:NativeWindow,IDisposable {public Window(){CreateHandle(new CreateParams{Caption="SDK receiver lifetime acceptance",Parent=new IntPtr(-3)});}public void Dispose(){DestroyHandle();}}
 static void Need(bool ok,string label){if(!ok)throw new Exception(label);Console.WriteLine("PASS "+label);}
 static void Pump(WindowsDesktopInputSession session,int ms){var clock=Stopwatch.StartNew();do{Application.DoEvents();session.Tick();Thread.Sleep(1);}while(clock.ElapsedMilliseconds<ms);}
 [System.Runtime.InteropServices.DllImport("user32.dll")]static extern bool IsWindow(IntPtr hwnd);
 static void AwaitClose(WindowsDesktopInputSession session){var clock=Stopwatch.StartNew();while(!session.Completion.IsCompleted&&clock.ElapsedMilliseconds<3000)Pump(session,1);}
 static void GuardCleanup(){foreach(bool destroy in new[]{false,true})using(var window=new Window()){
  var guard=new ReceiverWindowLifetime(window.Handle);var field=typeof(ReceiverWindowLifetime).GetField("removeSubclass",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
  try{field.SetValue(guard,new Func<bool>(()=>false));guard.Dispose();Need(!guard.Released&&guard.Completion.IsFaulted,"guard-remove-failure-retains-native-registration");var observed=guard.Completion.Exception;
   field.SetValue(guard,null);if(destroy)window.Dispose();else guard.Dispose();
   Need(guard.Released&&guard.Completion.IsFaulted,"guard-release-preserves-original-fault-"+destroy);Need(!destroy||guard.Destroyed,"guard-retained-callback-observes-real-destruction");
  }finally{field.SetValue(guard,null);guard.Dispose();Need(guard.Released,"guard-fixture-native-cleanup");}
 }
 using(var window=new Window()){
  var guard=new ReceiverWindowLifetime(window.Handle);var field=typeof(ReceiverWindowLifetime).GetField("removeSubclass",System.Reflection.BindingFlags.NonPublic|System.Reflection.BindingFlags.Instance);
  try{field.SetValue(guard,new Func<bool>(()=>false));window.Dispose();Need(guard.Destroyed&&guard.Completion.IsFaulted&&!guard.Released,"guard-destroy-callback-remove-failure-retained");var observed=guard.Completion.Exception;
   field.SetValue(guard,null);guard.Dispose();Need(guard.Released&&guard.Completion.IsFaulted,"guard-dead-window-registration-absence-proven-on-retry");
  }finally{field.SetValue(guard,null);guard.Dispose();Need(guard.Released,"guard-destroy-fault-fixture-cleanup");}
 }
 }
 [STAThread]static int Main(string[] args){try{
  if(args.Length>0&&args[0]=="cleanup"){GuardCleanup();return 0;}
  foreach(bool duringConnect in new[]{args.Length>0&&args[0]=="connecting"})using(var window=new Window())using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
   int calls=0;bool cancelled=false;
   var session=new WindowsDesktopInputSession(window.Handle,token=>{Interlocked.Increment(ref calls);entered.Set();release.Wait();cancelled=token.IsCancellationRequested;throw new InvalidOperationException("controlled-open-failure");},"unused",0,0,32,32);
   Exception primaryFailure=null;try{
    if(duringConnect){session.Tick();Need(entered.Wait(1000),"connection-factory-entered");}
    IntPtr original=window.Handle;window.Dispose();Need(window.Handle==IntPtr.Zero&&!IsWindow(original),"actual-receiver-HWND-destroyed");Pump(session,30);release.Set();AwaitClose(session);
    Need(session.State==InputRecoveryState.Closed&&session.Completion.Status==System.Threading.Tasks.TaskStatus.RanToCompletion&&session.ResourcesReleased,"destroyed-receiver-permanently-closes-"+duringConnect);
    Need(calls==(duringConnect?1:0),"destroyed-receiver-never-starts-new-open-"+duringConnect);
    Need(!duringConnect||cancelled,"destroyed-receiver-cancels-inflight-open");
    bool rejected=false;try{session.Retry();}catch(InvalidOperationException){rejected=true;}Need(rejected,"destroyed-receiver-retry-rejected");
    using(var replacement=new Window()){Pump(session,300);Need(session.State==InputRecoveryState.Closed&&calls==(duringConnect?1:0),"new-window-cannot-revive-old-session");}
   }catch(Exception error){primaryFailure=error;throw;}finally{try{release.Set();session.Dispose();AwaitClose(session);Need(session.Completion.IsCompleted,"fixture-session-cleanup");session.Completion.GetAwaiter().GetResult();}catch(Exception cleanup){Console.Error.WriteLine("fixture-cleanup-failure:"+cleanup);if(primaryFailure==null)throw;}}
  }
  return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
