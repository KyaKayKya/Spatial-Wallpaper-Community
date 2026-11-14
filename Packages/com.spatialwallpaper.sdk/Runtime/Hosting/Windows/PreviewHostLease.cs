using System;using System.Diagnostics;using System.IO;using System.Threading;using System.Threading.Tasks;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Supervision.Preview {
 // The bootstrap owns this object before awaiting Start. Stop also owns a Host
 // whose creation/endpoint arrives after cancellation; it never follows a new Host.
 public sealed class PreviewHostLease:IDisposable {
  readonly object gate=new object();readonly string executable,configuration,state;readonly PinnedProcessOwner owner;readonly CancellationTokenSource cancel=new CancellationTokenSource();Process host;Task starting=Task.CompletedTask,stopping,stdout=Task.CompletedTask,stderr=Task.CompletedTask;bool started,stopRequested,disposed;string outputTail="",errorTail="";
  public SupervisorManagerConnection Connection{get;private set;}public int HostId{get;private set;}public long HostCreationFileTime{get;private set;}public bool ForcedTermination{get;private set;}
  public int? ObservedExitCode{get{lock(gate)return host!=null&&host.HasExited?(int?)host.ExitCode:null;}}
  public string DiagnosticTail{get{lock(gate)return outputTail+errorTail;}}
  public bool ResourcesReleased{get{lock(gate)return disposed||starting.IsCompleted&&!ForcedTermination&&(host==null||host.HasExited&&HostExitContract.ProvesCleanup(host.ExitCode))&&stdout.Status==TaskStatus.RanToCompletion&&stderr.Status==TaskStatus.RanToCompletion&&(stopping==null||stopping.IsCompleted);}}
  public PreviewHostLease(string executable,string configuration,JObject options){
   this.executable=Path.GetFullPath(executable);this.configuration=Path.GetFullPath(configuration);if(options==null||!File.Exists(this.executable))throw new ArgumentException("preview-host-distribution");var config=(JObject)options.DeepClone();state=Path.GetFullPath((string)config["state"]??throw new ArgumentException("preview-host-state"));owner=PinnedProcessOwner.PinCurrent();
   try{HostExitContract.Require(this.executable);config["preview_owner"]=new JObject{{"pid",owner.Id},{"creation_filetime",owner.CreationFileTime}};byte[] bytes=System.Text.Encoding.UTF8.GetBytes(config.ToString(Newtonsoft.Json.Formatting.None));if(bytes.Length>16384)throw new ArgumentException("preview-host-configuration-size");using(var file=new FileStream(this.configuration,FileMode.CreateNew,FileAccess.Write,FileShare.Read)){file.Write(bytes,0,bytes.Length);file.Flush(true);}}catch{owner.Dispose();throw;}
  }
  static async Task Complete(Func<Task> operation,TaskCompletionSource<bool> completion){try{await operation().ConfigureAwait(false);completion.TrySetResult(true);}catch(OperationCanceledException){completion.TrySetCanceled();}catch(Exception error){completion.TrySetException(error);}}
  public Task StartAsync(){TaskCompletionSource<bool> completion;lock(gate){if(disposed)throw new ObjectDisposedException(nameof(PreviewHostLease));if(started)return starting;started=true;completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);starting=completion.Task;}_ = Complete(()=>Task.Run(StartCore),completion);return completion.Task;}
  async Task StartCore(){
   cancel.Token.ThrowIfCancellationRequested();var command=new ProcessStartInfo(executable,"\""+configuration+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden,WorkingDirectory=Path.GetDirectoryName(executable),RedirectStandardOutput=true,RedirectStandardError=true};
   var created=Process.Start(command);lock(gate)host=created??throw new InvalidOperationException("preview-host-create-failed");HostId=created.Id;HostCreationFileTime=PinnedProcessOwner.ReadCreationFileTime(created.Handle);stdout=Drain(created.StandardOutput,false);stderr=Drain(created.StandardError,true);
   var deadline=Stopwatch.StartNew();while(true){cancel.Token.ThrowIfCancellationRequested();if(created.HasExited)throw new InvalidOperationException("preview-host-start-exit:"+created.ExitCode);string endpoint=Path.Combine(state,"endpoint.json");if(File.Exists(endpoint)){var connection=ReadOwnEndpoint(endpoint);if(connection!=null){Connection=connection;return;}}if(deadline.ElapsedMilliseconds>=15000)throw new TimeoutException("preview-host-start-deadline");await Task.Delay(25,cancel.Token).ConfigureAwait(false);}
  }
  SupervisorManagerConnection ReadOwnEndpoint(string endpoint){
   var connection=new SupervisorManagerConnection(endpoint);
   return connection.HostProcessId==HostId&&connection.HostCreationFileTime==HostCreationFileTime?connection:null;
  }
  async Task Drain(StreamReader reader,bool error){var buffer=new char[256];while(true){int count=await reader.ReadAsync(buffer,0,buffer.Length).ConfigureAwait(false);if(count==0)return;lock(gate){string tail=(error?errorTail:outputTail)+new string(buffer,0,count);if(tail.Length>4096)tail=tail.Substring(tail.Length-4096);if(error)errorTail=tail;else outputTail=tail;}}}
  public Task StopAsync(){TaskCompletionSource<bool> completion;lock(gate){if(disposed)throw new ObjectDisposedException(nameof(PreviewHostLease));if(stopping!=null&&(!stopping.IsFaulted||host==null||host.HasExited))return stopping;stopRequested=true;cancel.Cancel();completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);stopping=completion.Task;}_ = Complete(StopCore,completion);return completion.Task;}
  async Task StopCore(){
   try{await starting.ConfigureAwait(false);}catch{}Process retained;lock(gate)retained=host;if(retained==null)return;var deadline=Stopwatch.StartNew();bool requested=false;Exception exitError=null;
   while(!retained.HasExited&&deadline.ElapsedMilliseconds<20000){if(!requested&&File.Exists(Path.Combine(state,"endpoint.json"))){try{var connection=Connection??ReadOwnEndpoint(Path.Combine(state,"endpoint.json"));if(connection==null){await Task.Delay(25).ConfigureAwait(false);continue;}var reply=await connection.Call("Exit",new JObject()).ConfigureAwait(false);if((bool?)reply["ok"]!=true)throw new InvalidDataException("preview-host-exit-rejected");Connection=connection;requested=true;}catch(Exception error){exitError=error;}}if(!retained.HasExited)await Task.Delay(25).ConfigureAwait(false);}
   if(!retained.HasExited){ForcedTermination=true;retained.Kill();if(!await Task.Run(()=>retained.WaitForExit(5000)).ConfigureAwait(false))throw new InvalidOperationException("preview-host-kernel-exit-unproven",exitError);}
   await Task.WhenAll(stdout,stderr).ConfigureAwait(false);if(ForcedTermination)throw new InvalidOperationException("preview-host-required-forced-termination",exitError);if(retained.ExitCode!=0)throw new InvalidOperationException("preview-host-exit:"+retained.ExitCode+":"+DiagnosticTail);
  }
  public void Dispose(){lock(gate){if(disposed)return;if(!ResourcesReleased||started&&!stopRequested)throw new InvalidOperationException("preview-host-must-stop-before-dispose");disposed=true;host?.Dispose();owner.Dispose();cancel.Dispose();}}
 }
}
