using System;
using System.Diagnostics;
using System.Threading;
using System.IO;
using System.Linq;
using System.Reflection;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Sdk.Capabilities;
using SpatialWallpaper.Supervision;
using SpatialWallpaper.Supervision.Broker;
using SpatialWallpaper.Supervision.Windows;
using Newtonsoft.Json.Linq;

static class NativeInputRegistryChecks {
 static int checks;
 static void Need(bool ok,string reason){if(!ok)throw new Exception(reason);checks++;}
 static void Reject(Action action,string reason){bool rejected=false;try{action();}catch(InvalidOperationException){rejected=true;}Need(rejected,reason);}
 static WorkerIdentity Identity()=>new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);
 sealed class NoLaunch:IWorkerLauncher {public IManagedWorker Start(LaunchRequest request,AttemptReceipt receipt,Action<int> created){throw new Exception("input-check-must-not-launch-legacy-renderer");}}
 static void ServiceChecks(){
  string path=Path.Combine(Path.GetDirectoryName(typeof(NativeInputRegistryChecks).Assembly.Location),"service");Directory.CreateDirectory(path);
  var journal=new JournalActor(Path.Combine(path,"journal"),RuntimeLimits.JournalPending);journal.Initialized.GetAwaiter().GetResult();
  var clock=Stopwatch.StartNew();var policy=new WatchdogPolicy(RuntimeLimits.StartupMs,RuntimeLimits.FrameMs,RuntimeLimits.StopMs,RuntimeLimits.ProcessBytes,RuntimeLimits.CpuWindowMs,RuntimeLimits.CpuBudgetMs,RuntimeLimits.ActualGpuBytes,RuntimeLimits.GpuStaleMs);
  var runtime=new SupervisorRuntime(journal,new NoLaunch(),policy,2,RuntimeLimits.GlobalProcessBytes,()=>clock.ElapsedMilliseconds);
  try{using(var broker=new InputBroker())using(var owner=PinnedProcessOwner.PinCurrent()){
   string descriptor=Path.Combine(path,"endpoint.json");
   var type=typeof(NativeInputRegistry).Assembly.GetType("SpatialWallpaper.Supervision.Host+Service",true);
   using(var service=(IDisposable)type.GetConstructors(BindingFlags.Instance|BindingFlags.NonPublic).Single().Invoke(new object[]{runtime,journal,descriptor,null,null,null,broker})){
    var connection=new SupervisorManagerConnection(descriptor);var identity=Identity();
    connection.RegisterNativeInput(identity,false,owner).GetAwaiter().GetResult();Need(true,"authenticated-native-registration");
    using(var other=Process.Start(new ProcessStartInfo(typeof(NativeInputRegistryChecks).Assembly.Location,"--owned-test-process"){UseShellExecute=false,CreateNoWindow=true}))try{
     using(var wrongOwner=PinnedProcessOwner.Open(other.Id,PinnedProcessOwner.ReadCreationFileTime(other.Handle))){
      bool wrongRejected=false;try{connection.ReadNativeInput(identity,wrongOwner).GetAwaiter().GetResult();}catch(InvalidDataException){wrongRejected=true;}Need(wrongRejected,"live-wrong-renderer-read-accepted");
      wrongRejected=false;try{using(var wrongSource=new DesktopInputChannel(connection.OpenNativeDesktopInputStream(identity,wrongOwner,CancellationToken.None).GetAwaiter().GetResult())){}}catch(InvalidDataException){wrongRejected=true;}Need(wrongRejected,"live-wrong-renderer-source-accepted");
     }
    }finally{if(!other.HasExited){other.Kill();Need(other.WaitForExit(3000),"wrong-owner-test-process-cleanup");}}
    var initial=connection.ReadNativeInput(identity,owner).GetAwaiter().GetResult();InputSnapshotCodec.RequireShape(initial);Need(initial.Length==96,"native-input-manager-snapshot");
    var wrong=new WorkerIdentity(identity.Attempt,identity.Instance,2);bool rejected=false;try{connection.ReadNativeInput(wrong,owner).GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}Need(rejected,"manager-cross-generation-read");
    var reply=connection.Call("ReadNativeInput",new JObject{{"host",Guid.NewGuid().ToString("N")},{"attempt",identity.Attempt.ToString("N")},{"instance",identity.Instance.ToString("N")},{"generation",identity.Generation}}).GetAwaiter().GetResult();Need((bool?)reply["ok"]==false,"manager-cross-host-read");
    using(var channel=new DesktopInputChannel(connection.OpenNativeDesktopInputStream(identity,owner,CancellationToken.None).GetAwaiter().GetResult())){
     long stamp=WindowsMachineClock.Milliseconds();channel.AcquireFocus(new InputObservation(1,stamp,true,true,true,true,true,false,true,0,.2f,.8f,0,channel.CaptureEpoch));
     var deadline=Stopwatch.StartNew();while(!channel.GetFocusConfirmation().Focused&&deadline.ElapsedMilliseconds<1000)Thread.Sleep(1);
     Need(channel.GetFocusConfirmation().Focused,"native-source-focus-confirmed-by-host");
     channel.Observe(new InputObservation(2,WindowsMachineClock.Milliseconds(),true,true,true,true,true,false,false,4,.2f,.8f,120,channel.CaptureEpoch));
     var frames=new InputFrames();deadline.Restart();do{frames.Advance(connection.ReadNativeInput(identity,owner).GetAwaiter().GetResult(),WindowsMachineClock.Milliseconds(),true);if(frames.GetButton("Space"))break;Thread.Sleep(1);}while(deadline.ElapsedMilliseconds<1000);
     Need(frames.Focused&&frames.GetButton("Space")&&frames.Wheel==1,"native-source-through-original-broker");
     channel.Revoke();deadline.Restart();do{frames.Advance(connection.ReadNativeInput(identity,owner).GetAwaiter().GetResult(),WindowsMachineClock.Milliseconds(),true);if(!frames.Focused)break;Thread.Sleep(1);}while(deadline.ElapsedMilliseconds<1000);
     Need(!frames.Focused&&!frames.GetButton("Space"),"native-source-revoke-through-manager");
    }
    connection.UnregisterNativeInput(identity).GetAwaiter().GetResult();connection.UnregisterNativeInput(identity).GetAwaiter().GetResult();Need(true,"native-manager-close-idempotent");
    rejected=false;try{connection.ReadNativeInput(identity,owner).GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}Need(rejected,"closed-native-manager-read");
    identity=Identity();connection.RegisterNativeOrderedInput(identity,false,owner).GetAwaiter().GetResult();
    rejected=false;try{connection.ReadNativeInput(identity,owner).GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}Need(rejected,"ordered-registration-cumulative-downgrade");
    var empty=connection.ReadNativeOrderedInput(identity,owner,0).GetAwaiter().GetResult();Need(empty.Cursor==0&&empty.Events.Length==0,"ordered-manager-initial-state");
    rejected=false;try{connection.ReadNativeOrderedInput(identity,owner,1).GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}Need(rejected,"ordered-manager-future-cursor");
    wrong=new WorkerIdentity(identity.Attempt,identity.Instance,2);rejected=false;try{connection.ReadNativeOrderedInput(wrong,owner,0).GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}Need(rejected,"ordered-manager-cross-generation");
    using(var channel=new DesktopInputChannel(connection.OpenNativeDesktopInputStream(identity,owner,CancellationToken.None).GetAwaiter().GetResult())){
     channel.AcquireFocus(new InputObservation(1,WindowsMachineClock.Milliseconds(),true,true,true,true,true,false,true,0,.2f,.8f,0,channel.CaptureEpoch));
     var deadline=Stopwatch.StartNew();while(!channel.GetFocusConfirmation().Focused&&deadline.ElapsedMilliseconds<1000)Thread.Sleep(1);Need(channel.GetFocusConfirmation().Focused,"ordered-source-focus");
     for(int i=0;i<3;i++)channel.Observe(new InputObservation(i+2,WindowsMachineClock.Milliseconds(),true,true,true,true,true,false,false,4,.2f,.8f,i==1?120:i==2?-120:0,channel.CaptureEpoch));
     OrderedInputBatch batch;deadline.Restart();do{batch=connection.ReadNativeOrderedInput(identity,owner,0).GetAwaiter().GetResult();if(batch.Events.Length==3)break;Thread.Sleep(1);}while(deadline.ElapsedMilliseconds<1000);
     Need(batch.Events.Length==3&&batch.Cursor==3&&batch.Events[0].Pressed==4&&batch.Events[1].Wheel==120&&batch.Events[2].Wheel==-120,"authenticated-ordered-source-preserves-opposite-wheel");
     var prefix=connection.ReadNativeOrderedInput(identity,owner,1).GetAwaiter().GetResult();Need(prefix.Events.Length==2&&prefix.Events[0].Sequence==2,"authenticated-ordered-prefix");
     Need(connection.ReadNativeOrderedInput(identity,owner,1).GetAwaiter().GetResult().Events.Length==2,"authenticated-ordered-repeat-preserves-unacked");
     rejected=false;try{connection.ReadNativeOrderedInput(identity,owner,0).GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}Need(rejected,"authenticated-ordered-regressed-cursor");
    }
    connection.UnregisterNativeInput(identity).GetAwaiter().GetResult();
   }
  }}finally{runtime.Close();Need(runtime.Completion.Wait(5000),"test-runtime-cleanup");runtime.Completion.GetAwaiter().GetResult();journal.Close();Need(journal.Completion.Wait(5000),"test-journal-cleanup");journal.Completion.GetAwaiter().GetResult();}
 }
 static InputObservation Event(InputTarget target,long sequence,long now,uint held=0,bool click=false)=>new InputObservation(sequence,now,true,true,true,true,true,false,click,held,.2f,.8f,0,target.CaptureEpoch);
 static void OrderedChecks(){
  long now=100;using(var broker=new InputBroker(()=>now))using(var target=broker.Register(Identity(),true,true)){
   target.AcquireFocus(Event(target,1,now,0,true));
   uint[] held={2,2,3,3,1,0};int[] wheels={0,120,0,-120,0,0};
   for(int i=0;i<held.Length;i++)target.Observe(new InputObservation(i+2,++now,true,true,true,true,true,false,false,held[i],.2f,.8f,wheels[i],target.CaptureEpoch));
   var all=target.OrderedSnapshot(0);Need(all.Events.Length==6&&all.Cursor==6,"ordered-native-observations-folded");
   Need(all.Events[0].Pressed==2&&all.Events[1].Wheel==120&&all.Events[2].Pressed==1&&all.Events[3].Wheel==-120&&all.Events[4].Released==2&&all.Events[5].Released==1,"cross-button-or-opposite-wheel-order-lost");
   Need(BitConverter.ToInt64(all.Snapshot,32)==0,"opposite-wheel-fixture-net-not-zero");
   var rest=target.OrderedSnapshot(2);Need(rest.Events.Length==4&&rest.Events[0].Sequence==3,"ordered-input-prefix-ack");
   Need(target.OrderedSnapshot(2).Events.Length==4,"same-cursor-read-erased-unacked-input");
   Reject(()=>target.OrderedSnapshot(1),"regressed-input-cursor-accepted");Reject(()=>target.OrderedSnapshot(7),"future-input-cursor-accepted");
   ulong epoch=target.CaptureEpoch;target.Revoke();var revoked=target.OrderedSnapshot(6);Need(revoked.Events.Length==0&&BitConverter.ToUInt64(revoked.Snapshot,8)>epoch,"revoked-ordered-events-replayed");
   target.AcquireFocus(Event(target,8,++now,0,true));target.Observe(Event(target,9,++now,4));var renewed=target.OrderedSnapshot(6);Need(renewed.Events.Length==1&&renewed.Events[0].Sequence==7&&renewed.Events[0].Pressed==4,"renewed-event-sequence-or-focus");
  }
  using(var broker=new InputBroker(()=>now))using(var target=broker.Register(Identity(),true,true)){
   target.AcquireFocus(Event(target,1,++now,0,true));ulong epoch=target.CaptureEpoch;
   for(int i=0;i<257;i++)target.Observe(Event(target,i+2,++now,i%2==0?4u:0u));
   var overflow=target.OrderedSnapshot(0);Need(!target.GetFocusConfirmation().Focused&&overflow.Events.Length==0&&target.CaptureEpoch>epoch,"ordered-overflow-kept-focus-or-partial-events");
  }
  using(var broker=new InputBroker(()=>now))using(var legacy=broker.Register(Identity(),true))Reject(()=>legacy.OrderedSnapshot(0),"legacy-target-silently-gained-event-journal");
  now=100;using(var broker=new InputBroker(()=>now))using(var target=broker.Register(Identity(),true,true)){
   target.AcquireFocus(Event(target,1,now,0,true));target.Observe(Event(target,2,++now,4));ulong epoch=target.CaptureEpoch;long sequence=2;
   foreach(long stamp in new long[]{350,599,848,1000}){now=stamp;target.Observe(Event(target,++sequence,now,4));}
   var retained=target.OrderedSnapshot(0);Need(target.CaptureEpoch==epoch&&retained.Events.Length==1,"fresh-real-source-heartbeats-lost-accepted-event");
   Need(retained.Events[0].Time==101&&BitConverter.ToInt64(retained.Snapshot,16)==1000,"event-capture-time-confused-with-current-authority-evidence");
  }
  now=100;using(var broker=new InputBroker(()=>now))using(var target=broker.Register(Identity(),true,true)){
   target.AcquireFocus(Event(target,1,now,0,true));target.Observe(Event(target,2,++now,4));now=1000;var stale=target.OrderedSnapshot(0);
   Need(stale.Events.Length==0&&!target.GetFocusConfirmation().Focused,"reading-without-real-heartbeat-renewed-old-evidence");
  }
 }
 static int Main(string[] args){
  if(args.Length==1&&args[0]=="--owned-test-process"){Thread.Sleep(60000);return 0;}
  Process child=null;try{
   child=Process.Start(new ProcessStartInfo(typeof(NativeInputRegistryChecks).Assembly.Location,"--owned-test-process"){UseShellExecute=false,CreateNoWindow=true});
   long childCreation=PinnedProcessOwner.ReadCreationFileTime(child.Handle);
   using(var wrongBirth=Process.GetProcessById(child.Id)){
    bool rejected=false;try{using(var unexpected=PinnedProcessOwner.RetainOriginal(wrongBirth,childCreation+1)){} }catch(InvalidDataException){rejected=true;}
    Need(rejected&&!wrongBirth.HasExited,"retain-original-wrong-birth-must-not-adopt-or-dispose");
   }
   using(var owner=PinnedProcessOwner.PinCurrent())using(var broker=new InputBroker()){
    var registry=new NativeInputRegistry(broker);try{
     var native=Identity();registry.Register(native,false,child.Id,childCreation);
     var token=registry.Find(native,false,child.Id,childCreation);Need(token!=null,"native-target-missing");
     Need(registry.Find(native,true,child.Id,childCreation)==null,"preview-kind-crossed");
     Need(registry.Find(native,false,owner.Id,owner.CreationFileTime)==null,"registry-wrong-live-owner-source");
     Reject(()=>registry.Snapshot(native,owner.Id,owner.CreationFileTime),"registry-wrong-live-owner-snapshot");
     var wrong=new WorkerIdentity(native.Attempt,native.Instance,2);Need(registry.Find(wrong,false,child.Id,childCreation)==null&&!registry.Remove(wrong),"stale-generation-authorized");
     Reject(()=>registry.Snapshot(wrong,child.Id,childCreation),"stale-generation-snapshot");
     Reject(()=>registry.Register(Identity(),false,child.Id,childCreation),"duplicate-owner-accepted");
     var legacy=broker.Register(Identity(),true);
     Reject(()=>registry.Register(Identity(),false,owner.Id,owner.CreationFileTime),"native-bypassed-global-input-capacity");
     long now=WindowsMachineClock.Milliseconds();token.AcquireFocus(Event(token,1,now,0,true));
     Need(token.GetFocusConfirmation().Focused,"native-focus-not-acquired");
     legacy.AcquireFocus(Event(legacy,1,WindowsMachineClock.Milliseconds(),0,true));
     Need(legacy.GetFocusConfirmation().Focused&&!token.GetFocusConfirmation().Focused,"legacy-native-focus-not-exclusive");
     legacy.Dispose();token.AcquireFocus(Event(token,2,WindowsMachineClock.Milliseconds(),0,true));
     Need(token.GetFocusConfirmation().Focused,"native-focus-not-reacquired");
     var endedSample=Process.GetProcessById(child.Id);try{
      Need(PinnedProcessOwner.ReadCreationFileTime(endedSample.Handle)==childCreation,"retain-original-before-exit-identity");child.Kill();Need(child.WaitForExit(3000),"owned-test-process-not-exited");
      var deadPin=PinnedProcessOwner.RetainOriginal(endedSample,childCreation);endedSample=null;using(deadPin)Need(deadPin.HasExited&&deadPin.CreationFileTime==childCreation,"retain-exited-original-without-second-pid-lookup");
     }finally{endedSample?.Dispose();}
     var deadline=Stopwatch.StartNew();while(registry.Find(native,false,child.Id,childCreation)!=null&&deadline.ElapsedMilliseconds<2000)Thread.Sleep(10);
     Need(registry.Find(native,false,child.Id,childCreation)==null&&!token.GetFocusConfirmation().Focused,"dead-renderer-retained-input");
     bool badBirth=false;try{registry.Register(Identity(),false,owner.Id,owner.CreationFileTime+1);}catch(System.IO.InvalidDataException){badBirth=true;}Need(badBirth,"wrong-process-birth-accepted");
     var preview=Identity();registry.Register(preview,true,owner.Id,owner.CreationFileTime);
     Need(registry.Find(preview,true,owner.Id,owner.CreationFileTime)!=null&&registry.Find(preview,false,owner.Id,owner.CreationFileTime)==null,"native-preview-kind-lost");
     Need(registry.Remove(preview)&&!registry.Remove(preview),"registration-close-not-once");
    }finally{registry.Dispose();}
    registry.Dispose();Need(true,"registry-dispose-idempotence");
   }
   ServiceChecks();
   OrderedChecks();
   Console.WriteLine("PASS "+checks+" native input ownership/focus/manager-pipe checks (synthetic observations; no actual window focus claim)");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
  finally{if(child!=null){if(!child.HasExited){child.Kill();child.WaitForExit(3000);}child.Dispose();}}
 }
}
