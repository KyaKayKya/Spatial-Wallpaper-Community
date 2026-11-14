using System;using System.Drawing;using System.Drawing.Imaging;using System.IO;using System.Linq;using System.Runtime.InteropServices;using System.Threading;using System.Threading.Tasks;using System.Windows.Forms;using Newtonsoft.Json.Linq;using SpatialWallpaper.Content;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Supervision.Presentation {
 // UI-thread adapter for the existing library transaction. It owns the late
 // RuntimeSession, reader, input and target callbacks until Unload completes.
 public sealed class WindowsContentPresentation:ISandboxContentPresentation,IDisposable {
  readonly Control target;readonly SupervisorManagerConnection connection;readonly int width,height,thread;readonly float displayWidth,displayHeight;readonly bool preview,scaleTarget;readonly PaintEventHandler staticPaint;readonly EventHandler destroyed;
  RuntimeSession session;WindowsFramePresenter presenter;WindowsFramePresenter.TargetStamp stamp;WindowsPresentedPreviewInput input;WindowsPresentedDesktopInput desktopInput;CancellationTokenSource readerStop;Task reader=Task.CompletedTask,firstFrame=Task.CompletedTask,projectionUpdate=Task.CompletedTask,unloading;RuntimeFrame lastFrame;
  Bitmap still;bool holding,disposed;long epoch,after,projectionRevision;uint generation;string work,revision;string[] capabilities;JObject receipt,heldReceipt;Exception frameFailure;
  float musicVolume=.25f,effectsVolume=.5f;bool soundStarted;public string SoundError{get;private set;}
  readonly System.Diagnostics.Stopwatch sampling=new System.Diagnostics.Stopwatch();long presentedCount,emptyReads,staleReads;double readWaitMs,presentMs,maxPresentMs;
  public JObject FrameStatistics=>new JObject{{"scope","owned target submission counters; not GPU timings"},{"presented_count",presentedCount},{"empty_reads",emptyReads},{"stale_projection_reads",staleReads},{"elapsed_ms",sampling.Elapsed.TotalMilliseconds},{"read_wait_ms",readWaitMs},{"present_ms",presentMs},{"max_present_ms",maxPresentMs}};
  public async Task SetSoundMixAsync(float music,float effects){Available();new SpatialWallpaper.Sdk.Capabilities.SoundMix(music,effects);musicVolume=music;effectsVolume=effects;if(soundStarted&&session!=null&&session.State==RuntimeSessionState.Running)SoundError=await connection.SetSoundMix(session,music,effects);}
  public async Task RefreshSoundStateAsync(){Available();if(soundStarted&&session!=null&&session.State==RuntimeSessionState.Running)SoundError=await connection.ReadSoundState(session);}
  public WindowsContentPresentation(Control target,SupervisorManagerConnection connection,int width,int height,float displayWidth,float displayHeight,bool preview,bool scaleTarget=false){
   if(target==null||connection==null)throw new ArgumentNullException();if(!target.IsHandleCreated||target.InvokeRequired||!(SynchronizationContext.Current is WindowsFormsSynchronizationContext))throw new InvalidOperationException("content-presentation-UI-context-required");RuntimeLimits.RequireRenderSize(width,height);new ProjectionConfiguration(1,width,height,displayWidth,displayHeight,0,0,-.6f);
   this.target=target;this.connection=connection;this.width=width;this.height=height;this.displayWidth=displayWidth;this.displayHeight=displayHeight;this.preview=preview;this.scaleTarget=scaleTarget;thread=Thread.CurrentThread.ManagedThreadId;
   staticPaint=(sender,args)=>{if(holding&&still!=null){if(scaleTarget)args.Graphics.DrawImage(still,target.ClientRectangle);else args.Graphics.DrawImageUnscaled(still,0,0);}};destroyed=(sender,args)=>RequestStop();target.Paint+=staticPaint;target.HandleDestroyed+=destroyed;
  }
  void Owner(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("content-presentation-owner-thread");}
  void Available(){Owner();if(disposed)throw new ObjectDisposedException(nameof(WindowsContentPresentation));}
  public JObject LastReceipt=>receipt==null?null:(JObject)receipt.DeepClone();public JObject HeldReceipt=>heldReceipt==null?null:(JObject)heldReceipt.DeepClone();
  public bool HoldingStatic=>holding&&still!=null;public bool RuntimeResourcesReleased=>session==null&&reader.IsCompleted&&firstFrame.IsCompleted&&projectionUpdate.IsCompleted&&presenter==null&&input==null&&desktopInput==null&&(unloading==null||unloading.IsCompleted);public Exception FrameFailure=>frameFailure;
  public Task LoadAsync(ValidatedPackage package,JObject revision,CancellationToken cancellation){throw new PlatformNotSupportedException("legacy-data-presentation-requires-existing-client-adapter");}
  public async Task LoadSandboxAsync(SandboxContentBuild package,JObject metadata,CancellationToken cancellation){
   Available();cancellation.ThrowIfCancellationRequested();if(package==null)throw new ArgumentNullException(nameof(package));if(session!=null||!reader.IsCompleted||!firstFrame.IsCompleted||!projectionUpdate.IsCompleted||unloading!=null&&!unloading.IsCompleted)throw new InvalidOperationException("previous-content-runtime-still-owned");
   capabilities=package.Capabilities;soundStarted=false;SoundError=null;
   long current=checked(++epoch);work=package.Installation.ToString("D");revision=package.RevisionId;receipt=null;frameFailure=null;after=0;readerStop=new CancellationTokenSource();presentedCount=emptyReads=staleReads=0;readWaitMs=presentMs=maxPresentMs=0;sampling.Restart();
   session=SupervisorSessionClient.Start(connection,package.Installation,package.BuildId,Guid.NewGuid(),checked(++generation),package.Path,preview);
   await Cancellable(session.Running,cancellation);Check(current,cancellation);
   presenter=new WindowsFramePresenter(target,session,package.BuildId,width,height,scaleTarget);projectionRevision=1;stamp=presenter.BindProjection(projectionRevision);
   await connection.SetProjection(session,new ProjectionConfiguration(1,width,height,displayWidth,displayHeight,0,0,-.6f));Check(current,cancellation);
  }
  static async Task Cancellable(Task pending,CancellationToken cancellation){
   if(!cancellation.CanBeCanceled){await pending;return;}var cancelled=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
   using(cancellation.Register(()=>cancelled.TrySetResult(true))){if(await Task.WhenAny(pending,cancelled.Task)!=pending){_ = pending.ContinueWith(done=>{var observed=done.Exception;},TaskContinuationOptions.OnlyOnFaulted);cancellation.ThrowIfCancellationRequested();}await pending;}
  }
  void Check(long current,CancellationToken cancellation){Owner();cancellation.ThrowIfCancellationRequested();if(disposed||current!=epoch||session==null||session.State!=RuntimeSessionState.Running)throw new OperationCanceledException("content-runtime-generation-ended");}
  // Completion acknowledges the bounded Host enqueue. LastReceipt remains the
  // receipt of which projection revision was submitted to the owned target.
  public Task SetEyeAsync(float x,float y,float z,bool preserveWidth=false){
   Available();if(presenter==null||session==null||session.State!=RuntimeSessionState.Running||unloading!=null&&!unloading.IsCompleted)throw new InvalidOperationException("content-runtime-not-loaded");if(!projectionUpdate.IsCompleted)throw new InvalidOperationException("content-projection-update-pending");
   var configuration=new ProjectionConfiguration(checked(projectionRevision+1),width,height,displayWidth,displayHeight,x,y,z,preserveWidth);long current=epoch;projectionRevision=configuration.Revision;stamp=presenter.BindProjection(projectionRevision);var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);projectionUpdate=completion.Task;_ = CompleteOwned(()=>UpdateProjection(current,configuration),completion);return completion.Task;
  }
  public void RebindTarget(){Available();if(presenter!=null&&session!=null&&session.State==RuntimeSessionState.Running)stamp=presenter.BindProjection(projectionRevision);}
  public long RequestedProjectionRevision=>projectionRevision;
  async Task UpdateProjection(long current,ProjectionConfiguration configuration){
   try{await connection.SetProjection(session,configuration);Check(current,CancellationToken.None);}catch(Exception error)when(current!=epoch){throw new OperationCanceledException("content-runtime-generation-ended",error);}catch(Exception error){frameFailure=frameFailure??error;session?.Stop();throw;}
  }
  async Task Render(long current,CancellationToken cancellation){
   while(true){Check(current,cancellation);if(desktopInput?.Failure!=null)throw new InvalidOperationException("desktop-input:"+desktopInput.Failure);long began=System.Diagnostics.Stopwatch.GetTimestamp();var frame=await connection.ReadVerifiedFrame(session,width,height,after);readWaitMs+=(System.Diagnostics.Stopwatch.GetTimestamp()-began)*1000.0/System.Diagnostics.Stopwatch.Frequency;Check(current,cancellation);
    if(frame==null){emptyReads++;await Task.Delay(1,cancellation);continue;}after=frame.Sequence;if(frame.ProjectionRevision<projectionRevision){staleReads++;continue;}if(frame.ProjectionRevision!=projectionRevision)throw new InvalidDataException("content-unexpected-projection-revision");
    if(presenter.TargetGeometryChanged(stamp))stamp=presenter.BindProjection(projectionRevision);var attempted=stamp;JObject shown;began=System.Diagnostics.Stopwatch.GetTimestamp();try{shown=presenter.Present(attempted,frame);}catch(InvalidOperationException error)when(current==epoch&&(error.Message=="presenter-stale-target"||error.Message=="presenter-target-unavailable")&&(!ReferenceEquals(attempted,stamp)||presenter.TargetGeometryChanged(attempted))){Check(current,cancellation);if(presenter.TargetGeometryChanged(stamp))stamp=presenter.BindProjection(projectionRevision);continue;}double cost=(System.Diagnostics.Stopwatch.GetTimestamp()-began)*1000.0/System.Diagnostics.Stopwatch.Frequency;presentMs+=cost;maxPresentMs=Math.Max(maxPresentMs,cost);presentedCount++;Check(current,cancellation);shown["work_id"]=work;shown["revision_id"]=revision;receipt=shown;lastFrame=frame;return;
   }
  }
  public Task FirstFrameAsync(CancellationToken cancellation){
   Available();if(!firstFrame.IsCompleted||!reader.IsCompleted)throw new InvalidOperationException("content-frame-reader-already-running");if(presenter==null||session==null)throw new InvalidOperationException("content-runtime-not-loaded");var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);firstFrame=completion.Task;long current=epoch;_ = CompleteOwned(()=>FirstFrameCore(current,cancellation),completion);return completion.Task;
  }
  async Task FirstFrameCore(long current,CancellationToken cancellation){
   try{await Render(current,cancellation);Check(current,cancellation);}catch(Exception error)when(cancellation.IsCancellationRequested||current!=epoch){throw new OperationCanceledException("content-runtime-generation-ended",error,cancellation);}catch(Exception error){frameFailure=frameFailure??error;throw;}
   if(capabilities.Contains("input")){if(preview)input=new WindowsPresentedPreviewInput(target,presenter,session,connection);else{desktopInput=new WindowsPresentedDesktopInput(target,presenter,session,connection,Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DesktopHitTester.exe"));while(!desktopInput.Ready){Check(current,cancellation);if(desktopInput.Failure!=null)throw new InvalidOperationException("desktop-input:"+desktopInput.Failure);await Task.Delay(10,cancellation);}}}
   Check(current,cancellation);
   if(capabilities.Contains("sound-playback")){SoundError=await connection.SetSoundMix(session,musicVolume,effectsVolume);Check(current,cancellation);soundStarted=true;}
   reader=ReadFrames(current,readerStop.Token);
  }
  async Task ReadFrames(long current,CancellationToken cancellation){
   try{while(true){await Render(current,cancellation);}}
   catch(Exception)when(cancellation.IsCancellationRequested||current!=epoch){}
   catch(Exception error){frameFailure=frameFailure??error;session?.Stop();}
  }
  static Bitmap Snapshot(RuntimeFrame frame){
   var pixels=new byte[WorkerFrameCodec.PayloadBytes(frame.Width,frame.Height)];frame.CopyPixels(pixels);var bitmap=new Bitmap(frame.Width,frame.Height,PixelFormat.Format32bppArgb);
   try{var data=bitmap.LockBits(new Rectangle(0,0,frame.Width,frame.Height),ImageLockMode.WriteOnly,PixelFormat.Format32bppArgb);try{var row=new byte[frame.Width*4];for(int y=0;y<frame.Height;y++){for(int x=0;x<frame.Width;x++){int at=(y*frame.Width+x)*4;row[x*4]=pixels[at+2];row[x*4+1]=pixels[at+1];row[x*4+2]=pixels[at];row[x*4+3]=255;}Marshal.Copy(row,0,IntPtr.Add(data.Scan0,y*data.Stride),row.Length);}}finally{bitmap.UnlockBits(data);}return bitmap;}catch{bitmap.Dispose();throw;}
  }
  public Task HoldStaticAsync(){Available();if(holding&&still!=null)return Task.CompletedTask;if(lastFrame!=null&&receipt!=null){var next=Snapshot(lastFrame);still?.Dispose();still=next;heldReceipt=(JObject)receipt.DeepClone();holding=true;}return Task.CompletedTask;}
  public void ReleaseStatic(){Available();holding=false;still?.Dispose();still=null;heldReceipt=null;if(!target.IsDisposed&&target.IsHandleCreated)target.Invalidate();}
  void RequestStop(){Owner();checked{epoch++;}readerStop?.Cancel();desktopInput?.Stop();try{input?.Dispose();input=null;}finally{presenter?.Dispose();presenter=null;session?.Stop();}}
  static async Task CompleteOwned(Func<Task> operation,TaskCompletionSource<bool> completion){try{await operation();completion.TrySetResult(true);}catch(Exception error){completion.TrySetException(error);}}
  public Task UnloadAsync(){Available();if(unloading!=null&&!unloading.IsCompleted)return unloading;var completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);unloading=completion.Task;_ = CompleteOwned(UnloadCore,completion);return completion.Task;}
  async Task UnloadCore(){
   Available();var owned=session;bool retryCleanup=owned!=null&&owned.Completion.IsCompleted&&!owned.ResourcesReleased;RequestStop();try{await firstFrame;}catch{}try{await projectionUpdate;}catch{}await reader;if(desktopInput!=null){await desktopInput.StopAsync();desktopInput=null;}
   if(owned!=null){if(retryCleanup)await owned.RetryCleanupAsync();else try{await owned.Completion;}catch{if(!owned.ResourcesReleased)throw;}if(!owned.ResourcesReleased)throw new InvalidOperationException("content-runtime-cleanup-unproven");}
   Owner();if(ReferenceEquals(session,owned))session=null;readerStop?.Dispose();readerStop=null;lastFrame=null;receipt=null;
   if(!target.IsDisposed&&target.IsHandleCreated)target.Refresh();
  }
  public void Dispose(){Owner();if(disposed)return;if(!RuntimeResourcesReleased)throw new InvalidOperationException("content-runtime-must-unload-before-dispose");disposed=true;target.Paint-=staticPaint;target.HandleDestroyed-=destroyed;still?.Dispose();still=null;holding=false;}
 }
}
