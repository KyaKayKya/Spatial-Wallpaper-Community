using System;
using System.IO;
using System.IO.Pipes;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Hosting;

namespace SpatialWallpaper.Sdk.Native.Windows {
 public interface IScriptProcessConnection:IDisposable {
  Stream Reader{get;}Stream Writer{get;}int ScriptProcessId{get;}long ScriptCreationFileTime{get;}
 }
 public sealed class ScriptCleanupException:IOException {public ScriptCleanupException(string reason,Exception inner):base(reason,inner){}}
 // Trusted renderer bootstrap. Construct/dispose on a background thread. These
 // streams carry metadata commands only; rendering never reads or waits on them.
 public sealed class ScriptProcessClient:IScriptProcessConnection {
  readonly NamedPipeServerStream control;readonly string root,nonce;readonly FileStream image;
  Process helper;PinnedProcessOwner scriptOwner;Stream reader,writer;bool disposed;Exception cleanupFailure;readonly object disposeGate=new object();
  public Stream Reader=>reader;
  public Stream Writer=>writer;
  public int ScriptProcessId{get;private set;}
  public long ScriptCreationFileTime{get;private set;}
  public bool HelperAlive=>helper!=null&&!helper.HasExited;
  public int HelperProcessId=>helper.Id;
  static string Hash(Stream input){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(input)).Replace("-","").ToLowerInvariant();}
  public ScriptProcessClient(string executable,string expectedDigest,string ownedCache,JObject configuration){
   root=Path.Combine(Path.GetFullPath(ownedCache),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);NativeScriptControl.ProtectDirectory(root);
   nonce=Guid.NewGuid().ToString("N")+Guid.NewGuid().ToString("N");string pipeName="SpatialWallpaper.NativeScript."+Guid.NewGuid().ToString("N");
   try{
    image=new FileStream(executable,FileMode.Open,FileAccess.Read,FileShare.Read);if(Hash(image)!=expectedDigest)throw new InvalidDataException("script-helper-image-drift");
    control=NativeScriptControl.CreatePrivateServer(pipeName);
    var config=(JObject)configuration.DeepClone();using(var owner=PinnedProcessOwner.PinCurrent()){config["parent_pid"]=owner.Id;config["parent_creation"]=owner.CreationFileTime;}
    config["pipe"]=pipeName;config["nonce"]=nonce;string path=Path.Combine(root,"launch.json");File.WriteAllText(path,config.ToString(Formatting.None));
    helper=Process.Start(new ProcessStartInfo(Path.GetFullPath(executable),"--script-launch \""+path+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(Path.GetFullPath(executable))});
    if(helper==null)throw new InvalidOperationException("script-helper-start");long helperCreation=PinnedProcessOwner.ReadCreationFileTime(helper.Handle);
    var pending=control.WaitForConnectionAsync();if(!pending.Wait(15000))throw new TimeoutException("script-helper-connect");pending.GetAwaiter().GetResult();
    if(NativeScriptControl.Peer(control,true)!=helper.Id||helper.HasExited||PinnedProcessOwner.ReadCreationFileTime(helper.Handle)!=helperCreation)throw new InvalidDataException("script-helper-peer");
    var ready=NativeScriptControl.Deadline(()=>NativeScriptControl.Read(control),15000);
    if((string)ready["nonce"]!=nonce)throw new InvalidDataException("script-helper-nonce");
    long read=(long)ready["reader"],write=(long)ready["writer"];if(read<=0||write<=0||read==write)throw new InvalidDataException("script-helper-endpoints");
    var readHandle=new SafeFileHandle(new IntPtr(read),true);var writeHandle=new SafeFileHandle(new IntPtr(write),true);
    try{reader=new FileStream(readHandle,FileAccess.Read,4096,false);writer=new FileStream(writeHandle,FileAccess.Write,4096,false);}catch{readHandle.Dispose();writeHandle.Dispose();throw;}
    ScriptProcessId=(int)ready["script_pid"];ScriptCreationFileTime=(long)ready["script_creation"];
    scriptOwner=PinnedProcessOwner.Open(ScriptProcessId,ScriptCreationFileTime);
   }catch(Exception original){try{Dispose();}catch(Exception cleanup){throw new ScriptCleanupException("script-launch-cleanup-unproven",new AggregateException(original,cleanup));}throw;}
  }
  public void Dispose(){lock(disposeGate){if(cleanupFailure!=null)throw cleanupFailure;try{Close();}catch(Exception failure){cleanupFailure=new ScriptCleanupException("script-process-cleanup-unproven",failure);throw cleanupFailure;}}}
  void Close(){
   if(disposed)return;disposed=true;Exception failure=null;
   try{
    if(helper!=null&&!helper.HasExited){
     try{if(control?.IsConnected==true)NativeScriptControl.Deadline(()=>{NativeScriptControl.Write(control,new JObject{{"nonce",nonce},{"operation","stop"}});return true;},500);}catch(Exception error){failure=error;}
     if(!helper.WaitForExit(8000)){helper.Kill();if(!helper.WaitForExit(5000))throw new InvalidOperationException("script-helper-exit-unproven");failure=new TimeoutException("script-helper-cleanup-deadline");}
     if(helper.ExitCode!=0)failure=new InvalidOperationException("script-helper-failed:"+helper.ExitCode);
    }
    if(scriptOwner!=null){var deadline=Stopwatch.StartNew();while(!scriptOwner.HasExited&&deadline.ElapsedMilliseconds<5000)System.Threading.Thread.Sleep(10);if(!scriptOwner.HasExited)throw new InvalidOperationException("script-worker-kernel-exit-unproven");}
   }finally{
    reader?.Dispose();writer?.Dispose();control?.Dispose();helper?.Dispose();scriptOwner?.Dispose();image?.Dispose();
    // Only our single known bootstrap file and freshly allocated directory.
    string path=Path.Combine(root,"launch.json");if(File.Exists(path))File.Delete(path);if(Directory.Exists(root))Directory.Delete(root,false);
   }
   if(failure!=null)throw failure;
  }
 }
}
