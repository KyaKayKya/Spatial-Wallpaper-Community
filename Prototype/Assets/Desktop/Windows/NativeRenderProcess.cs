using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Hosting;

namespace SpatialWallpaper.Desktop {
 public sealed class NativeRenderSurface {
  public int ProcessId{get;}public long CreationFileTime{get;}public IntPtr Window{get;}public uint RenderThread{get;}
  internal NativeRenderSurface(int pid,long creation,IntPtr window,uint thread){ProcessId=pid;CreationFileTime=creation;Window=window;RenderThread=thread;}
  // DesktopRuntime retains its own pin until AFTER detaching this surface.
  public PinnedProcessOwner Pin()=>PinnedProcessOwner.Open(ProcessId,CreationFileTime);
 }
 // Owned by DesktopRuntime, never by author code. This class owns only one
 // render process and metadata control pipe. It does not attach a window,
 // start a Tracker, create a tray icon, or read/present rendered pixels.
 public sealed class NativeRenderProcess:IDisposable {
  readonly object gate=new object();readonly string executable,cache;readonly JObject configuration;
  readonly TaskCompletionSource<NativeRenderSurface> ready=new TaskCompletionSource<NativeRenderSurface>(TaskCreationOptions.RunContinuationsAsynchronously);
  JObject pose,state=new JObject{{"ready",false}};long inputPauseRevision;volatile bool stopping;string error,cleanupError,diagnosticLog;
  public Task<NativeRenderSurface> Ready=>ready.Task;
  public Task Completion{get;}
  public string Error=>Volatile.Read(ref error);
  public string CleanupError=>Volatile.Read(ref cleanupError);
  public string DiagnosticLog=>Volatile.Read(ref diagnosticLog);
  public bool ResourcesReleased=>Completion.IsCompleted&&CleanupError==null;
  public JObject State{get{lock(gate)return (JObject)state.DeepClone();}}
  public NativeRenderProcess(string trustedPlayer,string ownedCache,JObject trustedConfiguration){
   executable=Path.GetFullPath(trustedPlayer);cache=Path.GetFullPath(ownedCache);
   configuration=(JObject)(trustedConfiguration??throw new ArgumentNullException(nameof(trustedConfiguration))).DeepClone();
   pose=configuration["pose"] as JObject??throw new ArgumentException("native-render-initial-pose");pose=(JObject)pose.DeepClone();
   if((bool?)pose["paused"]==true)inputPauseRevision=1;pose["input_pause_revision"]=inputPauseRevision;configuration["pose"]=pose.DeepClone();
   Completion=Task.Run(Run);
  }
  // Memory publication only; the Unity frame loop never waits on this pipe.
  public void PublishPose(JObject value){if(value==null)throw new ArgumentNullException(nameof(value));lock(gate){if(stopping)return;if((bool?)pose["paused"]!=true&&(bool?)value["paused"]==true)inputPauseRevision=checked(inputPauseRevision+1);pose=(JObject)value.DeepClone();pose["input_pause_revision"]=inputPauseRevision;}}
  public void Stop(){stopping=true;}
  public void Dispose()=>Stop();
  JObject Exchange(NamedPipeServerStream pipe,string nonce,string operation,JObject value=null){
   var request=new JObject{{"nonce",nonce},{"operation",operation}};if(value!=null)request["pose"]=value;
   return NativeScriptControl.Deadline(()=>{NativeScriptControl.Write(pipe,request);var reply=NativeScriptControl.Read(pipe);if((string)reply["nonce"]!=nonce)throw new InvalidDataException("native-render-reply-nonce");return reply;},2000);
  }
  static PinnedProcessOwner PinObservedScript(int pid,long creation){
   Process sample;try{sample=Process.GetProcessById(pid);}catch(ArgumentException){return null;}
   bool retained=false;try{
    // Acquire exactly one kernel handle. It can outlive script exit; no second
    // PID lookup may turn an ordinary script exit into a renderer failure.
    var pin=PinnedProcessOwner.RetainOriginal(sample,creation);retained=true;return pin;
   }catch(InvalidDataException failure)when(failure.Message=="process-owner-original-identity"){return null;}
   catch(InvalidOperationException)when(sample.HasExited){return null;}
   catch(System.ComponentModel.Win32Exception)when(sample.HasExited){return null;}
   finally{if(!retained)sample.Dispose();}
  }
  void Run(){
   Process child=null;PinnedProcessOwner renderer=null,script=null,input=null;NamedPipeServerStream pipe=null;FileStream image=null;Exception primary=null,cleanup=null;
   string root=null;bool connected=false,scriptAlreadyExited=false,inputAlreadyExited=false;int observedScriptPid=0,observedInputPid=0;long observedScriptBirth=0,observedInputBirth=0;
   try{
    if(stopping)throw new OperationCanceledException();
    root=Path.Combine(cache,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);NativeScriptControl.ProtectDirectory(root);
    string name="SpatialWallpaper.NativeRender."+Guid.NewGuid().ToString("N"),nonce=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");
    pipe=NativeScriptControl.CreatePrivateServer(name);var config=(JObject)configuration.DeepClone();config["version"]=1;config["pipe"]=name;config["nonce"]=nonce;
    using(var owner=PinnedProcessOwner.PinCurrent())config["owner"]=new JObject{{"pid",owner.Id},{"creation_filetime",owner.CreationFileTime}};
    string path=Path.Combine(root,"session.json"),log=Path.Combine(root,"player.log");diagnosticLog=log;File.WriteAllText(path,config.ToString(Formatting.None));
    image=new FileStream(executable,FileMode.Open,FileAccess.Read,FileShare.Read);
    child=Process.Start(new ProcessStartInfo(executable,"--native-render-session \""+path+"\" -screen-fullscreen 0 -screen-width 800 -screen-height 450 -logFile \""+log+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(executable)});
    if(child==null)throw new InvalidOperationException("native-render-start-failed");renderer=PinnedProcessOwner.Open(child.Id,PinnedProcessOwner.ReadCreationFileTime(child.Handle));
    var connection=pipe.WaitForConnectionAsync();connection.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);
    var clock=Stopwatch.StartNew();while(!connection.Wait(25)){if(stopping)throw new OperationCanceledException();if(renderer.HasExited)throw new InvalidOperationException("native-render-exited-before-connect");if(clock.ElapsedMilliseconds>15000)throw new TimeoutException("native-render-connect-deadline");}
    connection.GetAwaiter().GetResult();connected=true;if(NativeScriptControl.Peer(pipe,true)!=renderer.Id||renderer.HasExited)throw new InvalidDataException("native-render-control-peer");
    clock.Restart();
    while(!stopping){
     if(renderer.HasExited)throw new InvalidOperationException("native-render-unexpected-exit");
     JObject latest;lock(gate)latest=(JObject)pose.DeepClone();var reply=Exchange(pipe,nonce,"pose",latest);
     if(reply["error"]?.Type==JTokenType.String)throw new InvalidOperationException("native-render-failed:"+(string)reply["error"]);
     if((int?)reply["render_errors"]>0)throw new InvalidOperationException("native-render-scene-errors");
     if((bool?)reply["ready"]==true){
      if((int?)reply["pid"]!=renderer.Id||(long?)reply["creation_filetime"]!=renderer.CreationFileTime)throw new InvalidDataException("native-render-kernel-identity");
      var window=new IntPtr((long)reply["window"]);uint thread=(uint)reply["render_thread"];
      if(!WindowsDesktopWindow.MatchesPinnedChildWindow(window,renderer,thread))throw new InvalidDataException("native-render-surface-identity");
      int inputPid=(int?)reply["input_pid"]??0;long inputBirth=(long?)reply["input_creation"]??0;
      if(inputPid>0){
       if(inputBirth<=0)throw new InvalidDataException("native-render-input-identity");
       if(observedInputPid==0){observedInputPid=inputPid;observedInputBirth=inputBirth;}
       else if(observedInputPid!=inputPid||observedInputBirth!=inputBirth)throw new InvalidDataException("native-render-input-identity-changed");
       if(input==null&&!inputAlreadyExited){input=PinObservedScript(inputPid,inputBirth);inputAlreadyExited=input==null;}
      }
      ready.TrySetResult(new NativeRenderSurface(renderer.Id,renderer.CreationFileTime,window,thread));
      int scriptPid=(int?)reply["script_pid"]??0;long scriptBirth=(long?)reply["script_creation"]??0;
      if(scriptPid>0){
       if(scriptBirth<=0)throw new InvalidDataException("native-render-script-identity");
       if(observedScriptPid==0){observedScriptPid=scriptPid;observedScriptBirth=scriptBirth;}
       else if(observedScriptPid!=scriptPid||observedScriptBirth!=scriptBirth)throw new InvalidDataException("native-render-script-identity-changed");
       if(script==null&&!scriptAlreadyExited){script=PinObservedScript(scriptPid,scriptBirth);scriptAlreadyExited=script==null;}
      }
     }else if(clock.ElapsedMilliseconds>30000)throw new TimeoutException("native-render-scene-ready-deadline");
     lock(gate)state=(JObject)reply.DeepClone();Thread.Sleep(25);
    }
    if(!renderer.HasExited)Exchange(pipe,nonce,"stop");
   }catch(OperationCanceledException){if(!stopping){primary=new InvalidOperationException("native-render-cancelled");error=primary.Message;}}
   catch(Exception failure){primary=failure;error=failure.GetType().Name+":"+failure.Message;}
   finally{
    stopping=true;
    // EOF also asks the child to clean up. Keep the original kernel handles
    // until both the renderer and its observed script process have exited.
    try{pipe?.Dispose();}catch(Exception failure){cleanup=failure;}
    try{
     if(child!=null&&!child.HasExited&&!child.WaitForExit(12000)){child.Kill();if(!child.WaitForExit(5000))throw new InvalidOperationException("native-render-kernel-exit-unproven");throw new TimeoutException("native-render-forced-exit");}
     if(child!=null&&primary==null&&connected&&child.ExitCode!=0)throw new InvalidOperationException("native-render-exit-code:"+child.ExitCode);
     if(script!=null){var wait=Stopwatch.StartNew();while(!script.HasExited&&wait.ElapsedMilliseconds<5000)Thread.Sleep(10);if(!script.HasExited)throw new InvalidOperationException("native-render-script-exit-unproven");}
    }catch(Exception failure){cleanup=cleanup==null?failure:new AggregateException(cleanup,failure);cleanupError=cleanup.GetType().Name+":"+cleanup.Message;}
    // This child owns global hooks. Even after renderer failure/forced exit,
    // retain its original kernel pin until its own cleanup has actually ended.
    try{while(input!=null&&!input.HasExited)Thread.Sleep(25);if(input!=null&&input.ExitCode!=0)throw new InvalidOperationException("native-render-input-exit-code:"+input.ExitCode);if(inputAlreadyExited)throw new InvalidOperationException("native-render-input-exited-before-pin");}
    catch(Exception failure){cleanup=cleanup==null?failure:new AggregateException(cleanup,failure);}
    finally{foreach(var resource in new IDisposable[]{input,script,renderer,child,image})try{resource?.Dispose();}catch(Exception failure){cleanup=cleanup==null?failure:new AggregateException(cleanup,failure);}}
    try{if(root!=null){string path=Path.Combine(root,"session.json");if(File.Exists(path))File.Delete(path);if(primary==null&&cleanup==null){if(File.Exists(diagnosticLog))File.Delete(diagnosticLog);Directory.Delete(root,false);}}}catch(Exception failure){cleanup=cleanup==null?failure:new AggregateException(cleanup,failure);}
    if(cleanup!=null)cleanupError=cleanup.GetType().Name+":"+cleanup.Message;
    if(!ready.Task.IsCompleted){if(primary!=null)ready.TrySetException(primary);else if(cleanup!=null)ready.TrySetException(cleanup);else ready.TrySetCanceled();}
   }
   if(primary!=null&&cleanup!=null)throw new AggregateException(primary,cleanup);if(cleanup!=null)throw cleanup;if(primary!=null)throw primary;
  }
 }
}
