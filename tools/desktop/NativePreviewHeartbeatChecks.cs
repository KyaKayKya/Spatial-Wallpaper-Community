using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SpatialWallpaper.Sdk.Capabilities;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Sdk.Editor.Native;
using SpatialWallpaper.Supervision.Broker;

// Actual original preview subclass/timer, no synthetic observation or author
// execution. A heartbeat must be distinguishable from queued window input.
static class NativePreviewHeartbeatChecks {
 sealed class Tap:IPreviewInputTarget {
  public ulong CaptureEpoch{get;private set;}=1;
  public int Observations,Heartbeats,Revocations;
  public void Observe(InputObservation value){Observations++;if(value.Heartbeat)Heartbeats++;}
  public void Revoke(){CaptureEpoch++;Revocations++;}
 }
 [StructLayout(LayoutKind.Sequential)]struct Point{public int X,Y;}
 [DllImport("user32.dll")]static extern IntPtr GetForegroundWindow();
 [DllImport("user32.dll")]static extern bool SetForegroundWindow(IntPtr window);
 [DllImport("user32.dll")]static extern uint GetWindowThreadProcessId(IntPtr window,out uint pid);
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 [DllImport("user32.dll")]static extern bool AttachThreadInput(uint from,uint to,bool attach);
 [DllImport("user32.dll")]static extern bool GetCursorPos(out Point point);
 [DllImport("user32.dll")]static extern bool SetCursorPos(int x,int y);
 [DllImport("user32.dll")]static extern IntPtr WindowFromPoint(Point point);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)]static extern int GetClassName(IntPtr window,System.Text.StringBuilder name,int capacity);
 [DllImport("user32.dll")]static extern IntPtr GetAncestor(IntPtr window,uint flags);
 [DllImport("user32.dll")]static extern short GetAsyncKeyState(int key);
 [DllImport("user32.dll")]static extern void mouse_event(uint flags,uint x,uint y,uint data,UIntPtr extra);
 [DllImport("user32.dll")]static extern void keybd_event(byte key,byte scan,uint flags,UIntPtr extra);
 static void Pump(int ms){var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<ms){Application.DoEvents();Thread.Sleep(1);}}
 static void Need(bool value,string why){if(!value)throw new Exception(why);Console.WriteLine("PASS "+why);}
 [STAThread]static int Main(){
  var previous=GetForegroundWindow();Point cursor;Need(GetCursorPos(out cursor),"cursor-captured");bool primary=false,space=false;
  try{
   using(var window=new Form{Text="Spatial native preview heartbeat development check",StartPosition=FormStartPosition.Manual,Left=100,Top=100,Width=360,Height=240,TopMost=true}){
    window.Show();uint observed;var foreground=GetWindowThreadProcessId(previous,out observed);var own=GetCurrentThreadId();bool attached=foreground!=0&&foreground!=own&&AttachThreadInput(own,foreground,true);
    try{window.Activate();SetForegroundWindow(window.Handle);window.Focus();}finally{if(attached)AttachThreadInput(own,foreground,false);}
    Need(GetForegroundWindow()==window.Handle,"owned-preview-foreground");var center=window.PointToScreen(new System.Drawing.Point(160,100));Need(SetCursorPos(center.X,center.Y),"cursor-in-preview");
    var tap=new Tap();using(var source=new WindowsPreviewInput(window.Handle,tap)){
     var clock=Stopwatch.StartNew();while(clock.ElapsedMilliseconds<300){Application.DoEvents();Thread.Sleep(1);}
     Point actual;GetCursorPos(out actual);Console.WriteLine("window="+window.Handle+" foreground="+GetForegroundWindow()+" pointerWindow="+WindowFromPoint(actual)+" cursor="+actual.X+","+actual.Y+" revocations="+tap.Revocations);
     var hit=WindowFromPoint(actual);GetWindowThreadProcessId(hit,out var hitPid);var hitClass=new System.Text.StringBuilder(256);GetClassName(hit,hitClass,hitClass.Capacity);Console.WriteLine("hit_pid="+hitPid+" class="+hitClass+" root="+GetAncestor(hit,2));
     Need(tap.Observations>0,"actual-original-preview-timer-observed");
     Need(tap.Heartbeats>0,"preview-heartbeat-preserves-raw-input-order");
     Console.WriteLine("observations="+tap.Observations+" heartbeats="+tap.Heartbeats);
    }
    Need((GetAsyncKeyState(1)&0x8000)==0&&(GetAsyncKeyState(32)&0x8000)==0,"no-preheld-injected-buttons");
    using(var broker=new InputBroker())using(var target=broker.Register(new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),false)){
     string name="SpatialWallpaper.Preview.Heartbeat.QA."+Guid.NewGuid().ToString("N");
     using(var server=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous))using(var client=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous)){
      var serving=Task.Run(()=>{try{server.WaitForConnection();PreviewInputChannel.Serve(server,target);}catch(IOException){}catch(TimeoutException){}catch(ObjectDisposedException){}});
      try{client.Connect(1000);using(var channel=new PreviewInputChannel(client)){
     var idle=Stopwatch.StartNew();while(idle.ElapsedMilliseconds<600){channel.KeepAlive();Thread.Sleep(25);}
     var idleFrames=new InputFrames();idleFrames.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(!serving.IsCompleted&&!idleFrames.Focused,"idle-real-channel-lives-without-fabricated-input-authority");
     using(var source=new WindowsPreviewInput(window.Handle,channel)){
     primary=true;mouse_event(2,0,0,0,UIntPtr.Zero);mouse_event(4,0,0,0,UIntPtr.Zero);primary=false;Pump(40);
     var frames=new InputFrames();frames.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Console.WriteLine("positive focused="+frames.Focused+" down="+frames.GetButtonDown("Primary")+" up="+frames.GetButtonUp("Primary"));Need(frames.Focused&&frames.GetButtonDown("Primary")&&frames.GetButtonUp("Primary"),"same-window-real-button-positive");
     space=true;keybd_event(32,0,0,UIntPtr.Zero);Thread.Sleep(15);Need((GetAsyncKeyState(32)&0x8000)!=0,"OS-key-arrived-before-window-dispatch");
     // Force the exact timer/message race at its production Sample boundary:
     // OS state is new; the owning UI thread has not dispatched the key yet.
     typeof(WindowsPreviewInput).GetMethod("Sample",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(source,new object[]{0x113u,IntPtr.Zero,true,false});
     frames.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(frames.Focused&&!frames.GetButton("Space")&&!frames.GetButtonDown("Space"),"heartbeat-keeps-committed-state-before-queued-key");
     Pump(25);frames.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(frames.Focused&&frames.GetButton("Space")&&frames.GetButtonDown("Space"),"queued-native-key-dispatched-once-after-heartbeat");
     frames.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(frames.GetButton("Space")&&!frames.GetButtonDown("Space"),"queued-native-key-edge-not-repeated");
     keybd_event(32,0,2,UIntPtr.Zero);space=false;Pump(25);frames.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(frames.Focused&&frames.GetButtonUp("Space")&&!frames.GetButton("Space"),"queued-native-key-release-preserved");
     idle.Restart();while(idle.ElapsedMilliseconds<350){channel.KeepAlive();Thread.Sleep(25);}
     frames.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(!serving.IsCompleted&&!frames.Focused,"transport-keepalive-does-not-extend-real-input-evidence-deadline");
      }
     idle.Restart();while(idle.ElapsedMilliseconds<350){channel.KeepAlive();Thread.Sleep(25);}
     idleFrames.Advance(target.Snapshot(),WindowsMachineClock.Milliseconds(),true);Need(!serving.IsCompleted&&!idleFrames.Focused&&!idleFrames.GetButton("Space"),"transport-only-keepalive-cannot-revive-revoked-authority");
      }}finally{client.Dispose();server.Dispose();Need(serving.Wait(1000),"actual-preview-channel-reader-cleanup");serving.GetAwaiter().GetResult();}
     }
    }
   }
   return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
  finally{if(primary)mouse_event(4,0,0,0,UIntPtr.Zero);if(space)keybd_event(32,0,2,UIntPtr.Zero);Need(SetCursorPos(cursor.X,cursor.Y),"cursor-restored");if(previous!=IntPtr.Zero)SetForegroundWindow(previous);}
 }
}
