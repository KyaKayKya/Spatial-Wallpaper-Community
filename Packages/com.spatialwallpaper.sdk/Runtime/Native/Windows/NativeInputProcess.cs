using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Sdk.Capabilities;

namespace SpatialWallpaper.Sdk.Native.Windows {
 // Trusted per-renderer input role. All process/manager/pipe work stays on this
 // background owner; the render thread only publishes flags and takes metadata.
 public sealed class NativeInputProcess:INativeInputSession {
  readonly object gate=new object();readonly string imagePath,cache,descriptor;readonly WorkerIdentity identity;
  readonly PinnedProcessOwner renderer;readonly IntPtr window;readonly uint renderThread;readonly int[] region;
  OrderedInputBatch latest;long accepted,creation,pauseVersion,pausedVersion;bool enabled=true;volatile bool stopping,ready,connected,resourcesReleased;string error,cleanupError,firstCleanupError;int pid;int[] helpers=new int[0];
  NativeInputAuthorityReader authority;
  public Task Completion{get;}
  public WorkerIdentity Identity=>identity;
  public bool Ready=>ready&&!stopping;
  public bool Connected=>connected;
  public InputAuthority ReadAuthority(){var source=Volatile.Read(ref authority);return !Ready||source==null?default(InputAuthority):source.Read();}
  public int ProcessId=>Volatile.Read(ref pid);
  public long CreationFileTime=>Interlocked.Read(ref creation);
  public string Error=>Volatile.Read(ref error);
  public string CleanupError=>Volatile.Read(ref cleanupError);
  public string FirstCleanupError=>Volatile.Read(ref firstCleanupError);
  public bool ResourcesReleased=>Completion.Status==TaskStatus.RanToCompletion&&resourcesReleased;
  public OrderedInputBatch Latest{get{lock(gate)return latest==null?null:new OrderedInputBatch(latest.Snapshot,latest.Events,latest.Cursor);}}
  public int[] HelperProcessIds{get{lock(gate)return (int[])helpers.Clone();}}
  public NativeInputProcess(string trustedHelper,string ownedCache,string managerDescriptor,Guid instance,uint generation,PinnedProcessOwner renderOwner,IntPtr renderWindow,uint thread,int[] physicalRegion){
   if(renderOwner==null)throw new ArgumentNullException(nameof(renderOwner));if(physicalRegion==null||physicalRegion.Length!=4||physicalRegion[2]<=0||physicalRegion[3]<=0||thread==0)throw new ArgumentException("native-input-process-binding");
   // A private attempt belongs only to this owner, so compensation after an
   // uncertain registration reply can never remove a previous owner's entry.
   identity=new WorkerIdentity(Guid.NewGuid(),instance,generation);
   imagePath=Path.GetFullPath(trustedHelper);cache=Path.GetFullPath(ownedCache);descriptor=Path.GetFullPath(managerDescriptor);window=renderWindow;renderThread=thread;region=(int[])physicalRegion.Clone();
   renderer=PinnedProcessOwner.Open(renderOwner.Id,renderOwner.CreationFileTime);Completion=Task.Run(Run);
  }
  public void SetEnabled(bool value){lock(gate){if(enabled&&!value)pauseVersion=checked(pauseVersion+1);enabled=value;if(!value)ready=false;}}
  public void Pump(){}
  // Confirm only after the render-side bounded queue accepted this prefix.
  public void Accept(long cursor){lock(gate){if(latest==null||cursor<accepted||cursor>latest.Cursor)throw new ArgumentException("native-input-process-cursor");accepted=cursor;}}
  public void Stop(){stopping=true;ready=false;}
  public void Dispose()=>Stop();
  JObject Exchange(NamedPipeServerStream pipe,string nonce,bool stop,bool retry=false){
   bool active;long pause;lock(gate){pause=pauseVersion;active=enabled&&pausedVersion>=pause;}
   return NativeScriptControl.Deadline(()=>{NativeScriptControl.Write(pipe,new JObject{{"nonce",nonce},{"enabled",active},{"pause_revision",pause},{"stop",stop},{"retry",retry}});var value=NativeScriptControl.Read(pipe);if((string)value["nonce"]!=nonce)throw new InvalidDataException("native-input-role-nonce");return value;},2000);
  }
  void Run(){
   Process child=null;PinnedProcessOwner owner=null,host=null;FileStream image=null;NamedPipeServerStream pipe=null;SupervisorManagerConnection connection=null;bool registered=false,released=false;string root=null;
   try{
    if(stopping)return;connection=new SupervisorManagerConnection(descriptor);if(!connection.HostProcessId.HasValue||!connection.HostCreationFileTime.HasValue)throw new InvalidDataException("native-input-host-pin-required");host=PinnedProcessOwner.Open(connection.HostProcessId.Value,connection.HostCreationFileTime.Value);registered=true;connection.RegisterNativeOrderedInput(identity,false,renderer).GetAwaiter().GetResult();
    authority=new NativeInputAuthorityReader(connection.OpenNativeInputAuthority(identity,renderer).GetAwaiter().GetResult(),identity,host,renderer);
    root=Path.Combine(cache,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);NativeScriptControl.ProtectDirectory(root);
    string name="SpatialWallpaper.NativeInput."+Guid.NewGuid().ToString("N"),nonce=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N"),path=Path.Combine(root,"input.json");
    pipe=NativeScriptControl.CreatePrivateServer(name);
    using(var parent=PinnedProcessOwner.PinCurrent())File.WriteAllText(path,new JObject{{"version",1},{"pipe",name},{"nonce",nonce},{"parent",new JObject{{"pid",parent.Id},{"creation_filetime",parent.CreationFileTime}}},{"renderer",new JObject{{"pid",renderer.Id},{"creation_filetime",renderer.CreationFileTime}}},{"window",window.ToInt64()},{"thread",renderThread},{"region",new JArray(region)},{"descriptor",descriptor},{"attempt",identity.Attempt.ToString("N")},{"instance",identity.Instance.ToString("N")},{"generation",identity.Generation}}.ToString(Formatting.None));
    image=new FileStream(imagePath,FileMode.Open,FileAccess.Read,FileShare.Read);
    child=Process.Start(new ProcessStartInfo(imagePath,"--native-input-source \""+path+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(imagePath)});if(child==null)throw new InvalidOperationException("native-input-role-start");
    owner=PinnedProcessOwner.Open(child.Id,PinnedProcessOwner.ReadCreationFileTime(child.Handle));Interlocked.Exchange(ref creation,owner.CreationFileTime);Volatile.Write(ref pid,owner.Id);
    var connected=pipe.WaitForConnectionAsync();connected.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);var clock=Stopwatch.StartNew();
    while(!connected.Wait(25)){if(stopping)throw new OperationCanceledException();if(owner.HasExited)throw new InvalidOperationException("native-input-role-early-exit");if(clock.ElapsedMilliseconds>10000)throw new TimeoutException("native-input-role-connect");}
    connected.GetAwaiter().GetResult();if(NativeScriptControl.Peer(pipe,true)!=owner.Id||owner.HasExited)throw new InvalidDataException("native-input-role-peer");
    this.connected=true;
    while(!stopping){
     if(renderer.HasExited||owner.HasExited)throw new InvalidOperationException("native-input-role-owner-exit");
     var state=Exchange(pipe,nonce,false);if(state["error"]?.Type==JTokenType.String)throw new InvalidOperationException("native-input-role:"+(string)state["error"]);
     long cursor;lock(gate)cursor=accepted;var batch=connection.ReadNativeOrderedInput(identity,renderer,cursor).GetAwaiter().GetResult();
     lock(gate){long paused=(long)state["paused_revision"];if(paused<pausedVersion||paused>pauseVersion)throw new InvalidDataException("native-input-pause-confirmation");pausedVersion=paused;latest=batch;helpers=((JArray)state["helpers"]).ToObject<int[]>();ready=enabled&&pausedVersion>=pauseVersion&&(bool)state["ready"];}
     Thread.Sleep(10);
    }
    clock.Restart();while(!owner.HasExited){var state=Exchange(pipe,nonce,true,true);if((bool?)state["released"]==true){released=true;break;}if(clock.ElapsedMilliseconds>12000)throw new TimeoutException("native-input-role-cleanup");Thread.Sleep(25);}
   }catch(OperationCanceledException)when(stopping){}catch(Exception failure){error=failure.GetType().Name+":"+failure.Message;}
   finally{
    stopping=true;ready=false;pipe?.Dispose();
    // EOF requests the helper's original InputRecovery cleanup. Never kill a
    // helper whose global hooks may still be owned: retain its kernel handle.
    if(child!=null&&!child.HasExited&&!child.WaitForExit(12000)){
     cleanupError="native-input-role-kernel-exit-unproven";Interlocked.CompareExchange(ref firstCleanupError,cleanupError,null);
     // Keep the original owner and completion alive. A delayed successful
     // cleanup still has to unregister the broker entry and release its pins.
     while(!child.WaitForExit(1000)){}
     cleanupError=null;
    }
    if(child!=null&&child.HasExited&&child.ExitCode!=0)cleanupError="native-input-role-exit:"+child.ExitCode;
    if(child==null||child.HasExited)released=true;
    if(released){
     while(registered&&!host.HasExited){
      try{connection.UnregisterNativeInput(identity).GetAwaiter().GetResult();registered=false;if(cleanupError!=null&&cleanupError.StartsWith("native-input-unregister:",StringComparison.Ordinal))cleanupError=null;}
      catch(Exception failure){cleanupError="native-input-unregister:"+failure.Message;Interlocked.CompareExchange(ref firstCleanupError,cleanupError,null);Thread.Sleep(250);}
     }
     if(registered&&host.HasExited&&cleanupError!=null&&cleanupError.StartsWith("native-input-unregister:",StringComparison.Ordinal))cleanupError=null;
     Interlocked.Exchange(ref authority,null)?.Dispose();
     foreach(var value in new IDisposable[]{owner,child,image,renderer,host})value?.Dispose();
     try{if(root!=null){File.Delete(Path.Combine(root,"input.json"));Directory.Delete(root,false);}}catch(Exception failure){cleanupError="native-input-config-cleanup:"+failure.Message;}
     resourcesReleased=cleanupError==null;
    }
   }
  }
 }
}
