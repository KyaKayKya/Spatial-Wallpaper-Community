using System;
using System.Threading;
using System.Threading.Tasks;
using System.Diagnostics;
using Newtonsoft.Json.Linq;

namespace SpatialWallpaper.Sdk.Native.Windows {
 // Main thread: Pump only. Background: bootstrap, pipe IO and teardown. There
 // is no wait/Result/Join/pipe operation on the native render path.
 public sealed class NativeScriptSession:IDisposable {
  readonly ScriptBridgePump pump;readonly SessionIdentity identity;readonly JObject start;
  readonly CancellationTokenSource stopped=new CancellationTokenSource();readonly Stopwatch clock=Stopwatch.StartNew();
  readonly object gate=new object();readonly Func<IScriptProcessConnection> launch;
  IScriptProcessConnection client;readonly NativeInputQueue inputQueue=new NativeInputQueue();float delta=1f/60;long frame=1,progress;int running;string error,cleanupError;
  public Task Completion{get;}
  public string Error=>Volatile.Read(ref error)??pump.Error;
  public string CleanupError=>Volatile.Read(ref cleanupError);
  public bool ResourcesReleased=>Completion.IsCompleted&&CleanupError==null;
  public bool Running=>Volatile.Read(ref running)==1&&!stopped.IsCancellationRequested&&!pump.Stopped;
  public long AcknowledgedBatches=>Interlocked.Read(ref acknowledged);
  public long LastAcknowledgedInputEvent=>Interlocked.Read(ref lastAcknowledgedInputEvent);
  public SceneSnapshot LatestSnapshot=>pump.LatestSnapshot;
  public int PendingInputEvents=>inputQueue.PendingOrderedEventCount;
  public int RejectedInputEvents=>inputQueue.RejectedOrderedEventCount;
  public int ScriptProcessId{get{lock(gate)return client?.ScriptProcessId??0;}}
  public long ScriptCreationFileTime{get{lock(gate)return client?.ScriptCreationFileTime??0;}}
  NativeViewSnapshot view;
  public void PublishView(float eyeX,float eyeY,float eyeZ,float width,float height)=>Volatile.Write(ref view,new NativeViewSnapshot(eyeX,eyeY,eyeZ,width,height));
  long acknowledged,lastAcknowledgedInputEvent;int paused,phase;
  public NativeScriptSession(SessionIdentity identity,INativeCommandTarget target,Func<IScriptProcessConnection> launch,long[] rootNodes,JObject resources,string build,Func<SpatialWallpaper.Sdk.Capabilities.InputAuthority> authoritativeInput=null,NativeViewSnapshot initialView=null){
   view=initialView;
   this.identity=identity??throw new ArgumentNullException(nameof(identity));this.launch=launch??throw new ArgumentNullException(nameof(launch));
   pump=new ScriptBridgePump(identity,target,authoritativeInput,SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds);start=new JObject{{"snapshot",ScriptWire.Snapshot(pump.LatestSnapshot)},{"root_nodes",new JArray(rootNodes)},{"resources",resources.DeepClone()},{"build",build}};
   Completion=Task.Run(Run);
   Task.Run(async()=>{while(!Completion.IsCompleted&&!stopped.IsCancellationRequested){
    int deadline=Volatile.Read(ref running)==0?SceneCommandCatalog.ScriptStartupMs:SceneCommandCatalog.ScriptPendingMs;
    // Pause may hold a completed batch or idle between ticks. An outstanding
    // script wire operation must still meet its ordinary deadline.
    if(!(Volatile.Read(ref running)==1&&Volatile.Read(ref paused)==1&&Volatile.Read(ref phase)!=1)&&clock.ElapsedMilliseconds-Interlocked.Read(ref progress)>deadline){Fail("native-script-deadline");break;}
    await Task.Delay(25).ConfigureAwait(false);
   }});
  }
  void Fail(string reason){Interlocked.CompareExchange(ref error,reason,null);Stop();}
  void Progress()=>Interlocked.Exchange(ref progress,clock.ElapsedMilliseconds);
  void Check(){if(stopped.IsCancellationRequested)throw new OperationCanceledException();}
  void Run(){
   try{
    var created=launch();lock(gate)client=created;
    using(stopped.Token.Register(()=>Task.Run(()=>{try{created.Dispose();}catch(Exception failure){Interlocked.CompareExchange(ref cleanupError,failure.Message,null);Interlocked.CompareExchange(ref error,"script-cleanup:"+failure.Message,null);}}))){
     Check();var hello=ScriptWire.Read(created.Reader,identity,false);var value=hello.Payload;
     if(hello.Type!="hello"||(int)value["pid"]!=created.ScriptProcessId||(long)value["creation_filetime"]!=created.ScriptCreationFileTime||(string)value["build"]!=(string)start["build"]||(string)value["role"]!="pure-script")throw new InvalidOperationException("native-script-hello");
     var initial=(JObject)start.DeepClone();initial.Remove("build");ScriptWire.Write(created.Writer,identity,"start",initial,true);Volatile.Write(ref running,1);Progress();
     long tick=1,lastFrame=0;
     while(!stopped.IsCancellationRequested){
      if(Volatile.Read(ref paused)==1){Thread.Sleep(1);continue;}
      long published;float dt;lock(gate){published=frame;dt=delta;}
      if(published==lastFrame){Thread.Sleep(1);continue;}lastFrame=published;
      var input=inputQueue.Take();var next=new TickInput(tick,pump.LatestSnapshot,dt,input.Snapshot,input.Events,Volatile.Read(ref view));pump.MarkTickIssued(next);Progress();
      Volatile.Write(ref phase,1);Progress();ScriptWire.Write(created.Writer,identity,"tick",ScriptWire.Tick(next),true);
      var message=ScriptWire.Read(created.Reader,identity,false);Check();if(message.Type=="fault")throw new InvalidOperationException("script-fault:"+(string)message.Payload["error"]);if(message.Type!="batch")throw new InvalidOperationException("native-script-message");
      var batch=ScriptWire.Batch(message.Payload,identity);if(pump.TryEnqueue(batch)!=QueueResult.Accepted)throw new InvalidOperationException("native-script-batch-rejected");Volatile.Write(ref phase,2);
      BatchAck ack;while(!pump.TryTakeAck(out ack)){Check();Thread.Sleep(1);}
      Volatile.Write(ref phase,1);Progress();ScriptWire.Write(created.Writer,identity,"ack",ScriptWire.Ack(ack),true);pump.ConfirmAckDelivered(ack.Sequence);if(!ack.Accepted)throw new InvalidOperationException("native-script-transaction-rejected");
      if(next.Events.Count!=0)Interlocked.Exchange(ref lastAcknowledgedInputEvent,next.Events[next.Events.Count-1].Id);
      Interlocked.Increment(ref acknowledged);tick=checked(tick+1);Progress();Volatile.Write(ref phase,0);
     }
    }
   }catch(OperationCanceledException){}catch(Exception failure){if(failure is ScriptCleanupException)Interlocked.CompareExchange(ref cleanupError,failure.Message,null);if(!stopped.IsCancellationRequested)Fail(failure.GetType().Name+":"+failure.Message);}
   finally{
    pump.Stop();IScriptProcessConnection owned;lock(gate)owned=client;
    try{owned?.Dispose();}catch(Exception failure){Interlocked.CompareExchange(ref cleanupError,failure.Message,null);Interlocked.CompareExchange(ref error,"script-cleanup:"+failure.Message,null);}
    Volatile.Write(ref running,2);
   }
  }
  // Caller supplies only the existing broker's derived snapshot, never Win32
  // raw input. Event replay/focus integration is a separate required check.
  public void Pump(float deltaTime=1f/60,byte[] derivedInputs=null){
   if(float.IsNaN(deltaTime)||float.IsInfinity(deltaTime)||deltaTime<0)throw new ArgumentException("native-delta-time");
   try{inputQueue.Publish(derivedInputs);}catch(Exception failure){Fail("native-input:"+failure.Message);return;}
   AdvanceFrame(deltaTime);
  }
  public void PumpOrdered(float deltaTime,SpatialWallpaper.Sdk.Capabilities.OrderedInputBatch derivedInputs){
   if(float.IsNaN(deltaTime)||float.IsInfinity(deltaTime)||deltaTime<0)throw new ArgumentException("native-delta-time");
   try{inputQueue.PublishOrdered(derivedInputs);}catch(Exception failure){Fail("native-input:"+failure.Message);return;}
   AdvanceFrame(deltaTime);
  }
  void AdvanceFrame(float deltaTime){
   pump.PublishInputAuthority(inputQueue.CurrentSnapshot,SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds());if(Volatile.Read(ref paused)==1)return;pump.Pump();if(pump.Stopped){Stop();return;}
   lock(gate){delta=Math.Min(deltaTime,.25f);frame=checked(frame+1);}
  }
  public void RevokeInput(){inputQueue.Revoke();pump.PublishInputAuthority(null,SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds());}
  public void SetPaused(bool value){
   if(value){if(Interlocked.Exchange(ref paused,1)==0)RevokeInput();}
   else if(Volatile.Read(ref paused)==1){if(Volatile.Read(ref running)==1&&Volatile.Read(ref phase)!=1)Progress();Volatile.Write(ref paused,0);}
  }
  public void Stop(){pump.Stop();if(!stopped.IsCancellationRequested)stopped.Cancel();}
  public void Dispose(){Stop();pump.Dispose();}
 }
}
