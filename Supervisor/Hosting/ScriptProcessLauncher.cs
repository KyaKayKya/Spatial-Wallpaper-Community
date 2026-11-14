using System;
using System.IO;
using System.Linq;
using System.Diagnostics;
using System.Globalization;
using System.Security.Cryptography;
using System.Threading;
using Microsoft.Win32.SafeHandles;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Sdk.Native;
using SpatialWallpaper.Supervision.Broker;

namespace SpatialWallpaper.Supervision.Windows {
 public sealed class ScriptLaunchRequest {
  public WorkerIdentity Identity { get; }
  public string ProgramPath { get; }
  public string ProgramDigest { get; }
  public string BuildId { get; }
  public ScriptLaunchRequest(WorkerIdentity identity,string programPath,string programDigest,string buildId){
   if(identity==null||programPath==null||!Digest(programDigest)||!Digest(buildId))throw new ArgumentException("script-launch-request");
   Identity=identity;ProgramPath=Path.GetFullPath(programPath);ProgramDigest=programDigest;BuildId=buildId;
  }
  static bool Digest(string value)=>value!=null&&value.Length==64&&value.All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');
 }
 // No desktop control, Tracker, tray, or store. Must be hosted in the existing
 // trusted background role, where PrivateStation may be created before UI.
 public sealed class ScriptProcessLauncher {
  readonly PrivateStation station;readonly WorkerImage image;readonly string attempts;readonly Action<Exception> cleanupFailure;
  public ScriptProcessLauncher(PrivateStation station,WorkerImage image,string attempts,Action<Exception> cleanupFailure=null){
   this.station=station??throw new ArgumentNullException(nameof(station));this.image=image??throw new ArgumentNullException(nameof(image));
   this.attempts=Path.GetFullPath(attempts);Directory.CreateDirectory(this.attempts);if((File.GetAttributes(this.attempts)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("script-cache-link");this.cleanupFailure=cleanupFailure;
  }
  public ManagedScriptProcess Start(ScriptLaunchRequest request,BrokerSession session=null,StorageBroker storage=null){
   if(request==null)throw new ArgumentNullException(nameof(request));
   if((session==null)!=(storage==null))throw new ArgumentException("script-storage-pair");
   if(session!=null&&(session.Attempt!=request.Identity.Attempt||session.Instance!=request.Identity.Instance||session.Generation!=request.Identity.Generation))throw new ArgumentException("script-storage-identity");
   var workspace=new OwnedAttempt(attempts,request.Identity.Attempt,cleanupFailure);var result=new ManagedScriptProcess(workspace);
   try{
    image.Copy(workspace.Root);string program=Path.Combine(workspace.Root,"program.native-script.json");
    if((File.GetAttributes(request.ProgramPath)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("script-source-link");
    using(var input=new FileStream(request.ProgramPath,FileMode.Open,FileAccess.Read,FileShare.Read))using(var hash=SHA256.Create()){
     if(input.Length<1||input.Length>SceneCommandCatalog.MaxScriptBytes)throw new InvalidDataException("script-source-size");
     string digest=BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();if(digest!=request.ProgramDigest)throw new InvalidDataException("script-source-drift");input.Position=0;
     using(var output=new FileStream(program,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
    }
    string boundary=WindowsSandboxCanaries.Create(attempts,workspace);
    var arguments=new[]{"--program",program,"--build",request.BuildId,"--session",request.Identity.Instance.ToString("N"),"--attempt",request.Identity.Attempt.ToString("N"),"--generation",request.Identity.Generation.ToString(CultureInfo.InvariantCulture),
     "--input-handle","0","--output-handle","0","--stop-handle","0","--cap-request-handle","0","--cap-reply-handle","0","--lpac-canary",Path.Combine(boundary,"aap"),"--lpac-control",Path.Combine(boundary,"arap")};
    var clock=Stopwatch.StartNew();IntPtr logs=IntPtr.Zero;
    result.Child=RestrictedProcess.Start(station,Path.Combine(workspace.Root,image.Executable),arguments,workspace.Root,SceneCommandCatalog.ScriptProcessBytes,sid=>{
     if(session!=null)result.Capabilities=new CapabilityServer(sid,session,storage,()=>clock.ElapsedMilliseconds);
     result.Input=new WorkerReadPipe(sid);result.Output=new WorkerWritePipe(sid);result.Logs=new WorkerWritePipe(sid);result.Stop=new WorkerStopChannel(sid,request.Identity,()=>clock.ElapsedMilliseconds);
     arguments[Array.IndexOf(arguments,"--input-handle")+1]=result.Input.ChildReader.ToInt64().ToString(CultureInfo.InvariantCulture);
     arguments[Array.IndexOf(arguments,"--output-handle")+1]=result.Output.ChildWriter.ToInt64().ToString(CultureInfo.InvariantCulture);
     arguments[Array.IndexOf(arguments,"--stop-handle")+1]=result.Stop.ChildReader.ToInt64().ToString(CultureInfo.InvariantCulture);logs=result.Logs.ChildWriter;
     if(result.Capabilities!=null){arguments[Array.IndexOf(arguments,"--cap-request-handle")+1]=result.Capabilities.ChildRequestWriter.ToInt64().ToString(CultureInfo.InvariantCulture);arguments[Array.IndexOf(arguments,"--cap-reply-handle")+1]=result.Capabilities.ChildReplyReader.ToInt64().ToString(CultureInfo.InvariantCulture);return new[]{result.Input.ChildReader,result.Output.ChildWriter,result.Stop.ChildReader,logs,result.Capabilities.ChildRequestWriter,result.Capabilities.ChildReplyReader};}
     return new[]{result.Input.ChildReader,result.Output.ChildWriter,result.Stop.ChildReader,logs};
    },pid=>{result.Capabilities?.CloseChildCopies();result.Input.CloseChildCopy();result.Output.CloseChildCopy();result.Stop.CloseChildCopy();result.Logs.CloseChildCopy();},request.Identity.Attempt,()=>logs,cleanupFailure);
    result.Capabilities?.Start();result.StartLogs();return result;
   }catch{try{result.Dispose();}catch(Exception error){cleanupFailure?.Invoke(error);throw;}throw;}
  }
 }
 public sealed class ManagedScriptProcess:IDisposable {
  internal CapabilityServer Capabilities;
  internal RestrictedProcess Child;internal WorkerReadPipe Input;internal WorkerWritePipe Output,Logs;internal WorkerStopChannel Stop;readonly OwnedAttempt workspace;bool disposed;
  readonly object logGate=new object();readonly byte[] log=new byte[RuntimeLimits.LogBytes];int logBytes;Thread logger;Exception logError;
  internal ManagedScriptProcess(OwnedAttempt workspace){this.workspace=workspace;}
  public int ProcessId=>Child.ProcessId;
  public long CreationFileTime=>Child.CreationFileTime;
  public bool Alive=>Child.Wait(0)==258&&Capabilities?.ProtocolFault!=true;
  public uint ExitCode=>Child.ExitCode;
  public Exception LogError=>Volatile.Read(ref logError);
  public byte[] ReadLog(){lock(logGate){var result=new byte[logBytes];Buffer.BlockCopy(log,0,result,0,result.Length);return result;}}
  internal void StartLogs(){
   logger=new Thread(()=>{try{
    var clock=Stopwatch.StartNew();long window=0,total=0;var bytes=new byte[4096];int count;
    while((count=Logs.Read(bytes,0,bytes.Length))>0){
     long now=clock.ElapsedMilliseconds;if(now-window>=1000){window=now;total=0;}total+=count;
     if(total>RuntimeLimits.LogPerSecond)throw new InvalidDataException("script-log-rate-limit");
     lock(logGate){int keep=Math.Min(count,log.Length),retained=Math.Min(logBytes,log.Length-keep);if(retained>0)Buffer.BlockCopy(log,logBytes-retained,log,0,retained);Buffer.BlockCopy(bytes,count-keep,log,retained,keep);logBytes=retained+keep;}
    }
   }catch(Exception failure){Volatile.Write(ref logError,failure);try{Child.Kill(126);}catch(Exception secondary){Volatile.Write(ref logError,secondary);}}}){IsBackground=true,Name="SpatialWallpaper script diagnostics"};logger.Start();
  }
  // Streams do not own the original handles. The launch owner retains them;
  // a renderer in another process receives its own DuplicateHandle endpoints.
  public Stream OpenReader()=>new FileStream(new SafeFileHandle(Output.ParentReader,false),FileAccess.Read,4096,false);
  public Stream OpenWriter()=>new FileStream(new SafeFileHandle(Input.ParentWriter,false),FileAccess.Write,4096,false);
  public Stream OpenLogReader()=>new FileStream(new SafeFileHandle(Logs.ParentReader,false),FileAccess.Read,4096,false);
  internal IntPtr OutputHandle=>Output.ParentReader;
  internal IntPtr InputHandle=>Input.ParentWriter;
  public void RequestStop(){Capabilities?.Close();Stop?.Request();}
  public bool Wait(int milliseconds){if(milliseconds<0||milliseconds>15000)throw new ArgumentOutOfRangeException(nameof(milliseconds));return Child.Wait((uint)milliseconds)==0;}
  public void Kill(){Capabilities?.Close();Child?.Kill(124);}
  public void Dispose(){
   if(disposed)return;
   Capabilities?.Close();
   if(Child!=null){Stop?.Request();if(Child.Wait((uint)SceneCommandCatalog.ScriptStopMs)!=0)Child.Kill(124);if(Child.Wait(5000)!=0)throw new InvalidOperationException("script-kernel-exit-unproven");}
   if(logger!=null&&!logger.Join(1000))throw new InvalidOperationException("script-log-reader-still-alive");
   Capabilities?.Dispose();Stop?.Dispose();Child?.Dispose();Input?.Dispose();Output?.Dispose();Logs?.Dispose();workspace.Dispose();disposed=true;
  }
 }
}
