using System;using System.Collections.Generic;using System.Diagnostics;using System.IO;using System.Threading;using System.Threading.Tasks;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Security;
namespace SpatialWallpaper.Sdk.Editor {
 internal sealed class SandboxPreviewRun:IDisposable {
  readonly SandboxPreviewSnapshot snapshot;readonly Action<JObject> save;readonly CancellationTokenSource cancel=new CancellationTokenSource();readonly Stopwatch heartbeat=Stopwatch.StartNew();
  Process viewer;PinnedProcessOwner host;string workspace;long viewerCreation,lastSequence,lastProgress;int width,height;bool released=true,disposed;Task starting=Task.CompletedTask,stopping=Task.CompletedTask;JObject status,receipt;
  internal string State{get;private set;}="Stopped";internal string Error{get;private set;}internal JObject LastReceipt=>receipt==null?null:(JObject)receipt.DeepClone();
  internal bool ResourcesReleased=>released&&starting.IsCompleted&&stopping.IsCompleted;
  internal bool NativeResourcesReleased=>released;
  internal SandboxPreviewRun(SandboxPreviewSnapshot snapshot,Action<JObject> save){this.snapshot=snapshot;this.save=save;}
  static async void Complete(Func<Task> operation,TaskCompletionSource<bool> completion){try{await operation();completion.TrySetResult(true);}catch(OperationCanceledException){completion.TrySetCanceled();}catch(Exception error){completion.TrySetException(error);}}
  internal Task StartAsync(string runtime,string attempts,int pixelsWide,int pixelsHigh,float physicalWidth,float physicalHeight){
   if(disposed||viewer!=null||!starting.IsCompleted)throw new InvalidOperationException("preview-run-already-owned");RuntimeLimits.RequireRenderSize(pixelsWide,pixelsHigh);new ProjectionConfiguration(1,pixelsWide,pixelsHigh,physicalWidth,physicalHeight,0,0,-.6f);width=pixelsWide;height=pixelsHigh;released=false;State="CheckingRuntime";var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);starting=completion.Task;Complete(()=>StartCore(runtime,attempts,physicalWidth,physicalHeight),completion);return starting;
  }
  async Task StartCore(string runtime,string attempts,float physicalWidth,float physicalHeight){try{
   var rules=RuleCatalog.Current.Snapshot;var identity=new Dictionary<string,string>();foreach(string key in new[]{"sdk","engine","pipeline","vm","backend"})identity.Add(key,(string)rules[key]);identity.Add("rules_sha256",RuleCatalog.Current.Digest);identity.Add("policy_id",RuntimeLimits.Id);runtime=Path.GetFullPath(runtime);
   await Task.Run(()=>TrustedRuntimeDistribution.Verify(runtime,identity,Path.Combine(runtime,"SpatialWallpaper.Preview.exe"),cancel.Token));cancel.Token.ThrowIfCancellationRequested();
   SandboxPreviewCache.TrimLaunches(snapshot.DataDirectory);string launch=Path.Combine(snapshot.DataDirectory,"launches",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(launch);workspace=Path.Combine(launch,"native");string configuration=Path.Combine(launch,"preview.json");
   using(var owner=PinnedProcessOwner.PinCurrent()){var config=new JObject{{"format",1},{"toolchain",runtime},{"package",snapshot.Path},{"workspace",workspace},{"library",Path.Combine(snapshot.DataDirectory,"preview-library")},{"attempts",Path.GetFullPath(attempts)},{"width",width},{"height",height},{"display_width",physicalWidth},{"display_height",physicalHeight},{"owner",new JObject{{"pid",owner.Id},{"creation_filetime",owner.CreationFileTime}}},{"llm",null},{"audio",PlayAudioSelection.Read()}};File.WriteAllText(configuration,config.ToString(Newtonsoft.Json.Formatting.None));}
   cancel.Token.ThrowIfCancellationRequested();viewer=Process.Start(new ProcessStartInfo(Path.Combine(runtime,"SpatialWallpaper.Preview.exe"),"\""+configuration+"\""){UseShellExecute=false,CreateNoWindow=true,WindowStyle=ProcessWindowStyle.Hidden});if(viewer==null)throw new InvalidOperationException("preview-native-create-failed");viewerCreation=PinnedProcessOwner.ReadCreationFileTime(viewer.Handle);State="Starting";Save();
  }catch(Exception error){Error=error is OperationCanceledException?null:error.GetBaseException().Message;State=Error==null?"Stopped":"Failed";if(viewer==null)released=true;else{RequestStop();await StopViewer();}throw;}}
  void Save(){if(viewer==null){save(null);return;}var record=snapshot.Record();record["viewer_pid"]=viewer.Id;record["viewer_creation"]=viewerCreation;record["workspace"]=workspace;record["width"]=width;record["height"]=height;save(record);}
  void ReadStatus(){
   var next=PreviewStatusFile.Read(Path.Combine(workspace,"status.json"),viewer.Id,viewerCreation);if(next==null)return;long sequence=(long)next["sequence"];if(sequence<lastSequence)throw new InvalidDataException("preview-status-sequence-regressed");bool previouslyObserved=lastSequence>0,progress=sequence>lastSequence;if(progress){lastProgress=heartbeat.ElapsedMilliseconds;lastSequence=sequence;}status=next;
   if(host==null&&(int)next["host_pid"]>0&&!viewer.HasExited){try{host=PinnedProcessOwner.Open((int)next["host_pid"],(long)next["host_creation_filetime"]);}catch(ArgumentException){}catch(InvalidOperationException)when(viewer.HasExited){}}
   if(host!=null&&((int)next["host_pid"]!=host.Id||(long)next["host_creation_filetime"]!=host.CreationFileTime))throw new InvalidDataException("preview-host-identity-changed");
   var shown=next["receipt"] as JObject;if(shown!=null){if((string)shown["build"]!=snapshot.BuildId||(int)shown["width"]!=width||(int)shown["height"]!=height)throw new InvalidDataException("preview-receipt-snapshot-mismatch");receipt=(JObject)shown.DeepClone();}
   if(next["error"].Type==JTokenType.String)Error=(string)next["error"];
   if(State!="Stopping"){State=(string)next["state"];if(State=="Running"){if(!progress||!previouslyObserved)State=snapshot.Presented?"Running":"Starting";else if(!viewer.HasExited){snapshot.Presented=true;}}}
  }
  internal void Poll(){
   if(viewer==null||disposed)return;try{ReadStatus();if(viewer.HasExited){if(stopping.IsCompleted){if(!released)Finish();}return;}if((State=="Running")&&heartbeat.ElapsedMilliseconds-lastProgress>3000)throw new InvalidDataException("preview-status-heartbeat-stalled");}
   catch(Exception error){Error=error.GetBaseException().Message;State="Faulted";RequestStop();}
  }
  internal void RequestStop(){cancel.Cancel();if(viewer!=null&&!viewer.HasExited){Directory.CreateDirectory(workspace);File.WriteAllText(Path.Combine(workspace,"stop-request"),"stop");}if(viewer!=null)State="Stopping";}
  // Native UI and Host run independently. Reload cannot await a continuation on
  // the Unity context that is about to disappear; join the retained process here.
  internal void StopForReload(){RequestStop();if(viewer==null){released=true;return;}if(!viewer.WaitForExit(45000))throw new TimeoutException("preview-reload-cleanup-deadline");ReadStatus();Finish();}
  internal Task StopAsync(){if(!stopping.IsCompleted)return stopping;RequestStop();var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);stopping=completion.Task;Complete(async()=>{try{await starting;}catch{}await StopViewer();},completion);return stopping;}
  async Task StopViewer(){if(viewer==null){released=true;if(Error==null)State="Stopped";return;}RequestStop();var deadline=Stopwatch.StartNew();while(!viewer.HasExited){if(deadline.ElapsedMilliseconds>45000)throw new TimeoutException("preview-native-cleanup-deadline");await Task.Delay(25);}ReadStatus();Finish();}
  void Finish(){
   if(viewer==null)return;if(!viewer.HasExited)throw new InvalidOperationException("preview-native-still-running");
   if(status==null||!(bool)status["window_resources_released"]||!(bool)status["host_resources_released"]||(bool)status["forced_host_termination"]||host!=null&&!host.HasExited||(int)status["host_pid"]>0&&host==null&&!SandboxPreviewRecovery.Ended((int)status["host_pid"],(long)status["host_creation_filetime"]))throw new InvalidOperationException("preview-native-cleanup-unproven");
   int code=viewer.ExitCode;SandboxPreviewCache.Completed(workspace);released=true;viewer.Dispose();viewer=null;host?.Dispose();host=null;save(null);State=code==0&&Error==null?"Stopped":"Failed";if(code!=0){Error=Error??"preview-native-exit:"+code;throw new InvalidOperationException(Error);}
  }
  internal static SandboxPreviewRun Restore(SandboxPreviewSnapshot snapshot,JObject record,Action<JObject> save){
   var result=new SandboxPreviewRun(snapshot,save){workspace=(string)record["workspace"],width=(int)record["width"],height=(int)record["height"],viewerCreation=(long)record["viewer_creation"],released=false,State="Stopping"};var process=Process.GetProcessById((int)record["viewer_pid"]);
   try{if(PinnedProcessOwner.ReadCreationFileTime(process.Handle)!=result.viewerCreation)throw new InvalidDataException("preview-recovery-viewer-identity");result.viewer=process;result.RequestStop();return result;}catch{process.Dispose();throw;}
  }
  public void Dispose(){if(disposed)return;if(!ResourcesReleased)throw new InvalidOperationException("preview-run-must-stop-before-dispose");disposed=true;viewer?.Dispose();host?.Dispose();cancel.Dispose();}
 }
}
