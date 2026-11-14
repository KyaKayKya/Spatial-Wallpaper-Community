using System;using System.Collections.Generic;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Capabilities;
static class InputBrokerChecks {
 static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
 static InputObservation Event(InputTarget target,long sequence,long time,uint held=0,bool click=false,int wheel=0)=>new InputObservation(sequence,time,true,true,true,true,true,false,click,held,.25f,.75f,wheel,target.CaptureEpoch);
 static WorkerIdentity Identity()=>new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 static WeakReference RetainedCapacity(InputBroker capacity){
  var retained=new List<InputTarget>();for(int i=0;i<64;i++){var token=capacity.Register(Identity(),true);token.Dispose();retained.Add(token);}
  bool rejected=false;try{capacity.Register(Identity(),true);}catch(InvalidOperationException){rejected=true;}Need(rejected,"unbounded-retained-input-registrations");
  var first=new WeakReference(retained[0]);GC.KeepAlive(retained);return first;
 }
 static int Main(){try{
  long now=0;using(var broker=new InputBroker(()=>now)){
   var a=broker.Register(Identity(),true);var bId=Identity();var b=broker.Register(bId,true);
   bool full=false;try{broker.Register(Identity(),true);}catch(InvalidOperationException){full=true;}Need(full,"unbounded-input-targets");
   var receivedA=new InputFrames();receivedA.Advance(a.Snapshot(),now,true);
   now=10;a.AcquireFocus(Event(a,1,5,0,true));now=11;a.Observe(Event(a,2,11,4));
   receivedA.Advance(a.Snapshot(),now,true);Need(receivedA.Focused&&receivedA.GetButtonDown("Space"),"registered-input-not-delivered");
   ulong oldEpoch=a.CaptureEpoch;var bRequest=Event(b,1,11,0,true);
   now=12;a.Observe(Event(a,3,12,4));b.AcquireFocus(bRequest);
   receivedA.Advance(a.Snapshot(),now,true);Need(!receivedA.Focused,"new-target-did-not-revoke-old");
   a.Observe(new InputObservation(4,12,true,true,true,true,true,false,true,1,0,0,0,oldEpoch));
   var receivedB=new InputFrames();receivedB.Advance(b.Snapshot(),now,true);Need(receivedB.Focused,"old-target-callback-stole-focus");
   b.Dispose();bool reused=false;try{broker.Register(bId,true);}catch(InvalidOperationException){reused=true;}Need(reused,"same-live-registration-replaced");
   now=13;b.AcquireFocus(Event(b,2,13,0,true));receivedB.Advance(b.Snapshot(),now,true);Need(!receivedB.Focused,"closed-target-revived");
   var replacement=broker.Register(new WorkerIdentity(Guid.NewGuid(),bId.Instance,bId.Generation),false);
   now=14;replacement.Observe(Event(replacement,1,14,1,true));var preview=new InputFrames();preview.Advance(replacement.Snapshot(),now,true);
   Need(preview.GetButtonDown("Primary"),"new-epoch-reset-erased-first-click");
   now=15;replacement.Observe(Event(replacement,2,15,0));now=16;replacement.Observe(Event(replacement,3,16,1,true));now=17;replacement.Observe(Event(replacement,4,17,0));
   preview.Advance(replacement.Snapshot(),now,true);Need(preview.GetButtonDown("Primary")&&preview.GetButtonUp("Primary")&&!preview.GetButton("Primary"),"coalesced-click-lost");
   now=18;preview.Advance(replacement.Snapshot(),now,true);Need(!preview.GetButtonDown("Primary")&&!preview.GetButtonUp("Primary"),"snapshot-replayed-edges");
   now=200;byte[] queued=replacement.Snapshot();preview.Advance(queued,268,true);Need(!preview.Focused,"send-time-renewed-old-evidence");
   now=268;var expired=new InputFrames();expired.Advance(replacement.Snapshot(),now,true);Need(!expired.Focused,"source-expiry-not-revoked");
   Need(!replacement.Matches(bId)&&replacement.Matches(new WorkerIdentity(replacement.Identity.Attempt,bId.Instance,bId.Generation)),"target-wire-identity-not-exact");
  }
  using(var capacity=new InputBroker(()=>now)){
   // End the creating stack frame before GC: debug JIT locals/temporaries may
   // otherwise keep the first token alive even after List.Clear().
   var first=RetainedCapacity(capacity);GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();Need(!first.IsAlive,"closed-input-token-rooted");
   using(var fresh=capacity.Register(Identity(),true))Need(fresh!=null,"collected-input-registration-not-reusable");
  }
  foreach(string counter in new[]{"down","up","wheel"})using(var overflow=new InputBroker(()=>now))using(var token=overflow.Register(Identity(),false)){
   long sequence=0;token.Observe(Event(token,++sequence,++now,1,true));
   if(counter=="up"){token.Observe(Event(token,++sequence,++now,4));token.Ups[2]=uint.MaxValue;token.Observe(Event(token,++sequence,++now));}
   else if(counter=="down"){token.Downs[2]=uint.MaxValue;token.Observe(Event(token,++sequence,++now,4));}
   else{token.Wheel=long.MaxValue;token.Observe(Event(token,++sequence,++now,1,false,1));}
   var cleared=new InputFrames();cleared.Advance(token.Snapshot(),now,true);Need(!cleared.Focused&&!cleared.GetButton("Primary")&&!cleared.GetButtonDown("Space")&&cleared.Wheel==0,"counter-overflow-kept-input:"+counter);
   token.Observe(Event(token,++sequence,++now,1,true));cleared.Advance(token.Snapshot(),now,true);Need(cleared.GetButtonDown("Primary"),"counter-overflow-blocked-new-focus:"+counter);
  }
  using(var ordering=new InputBroker(()=>now))using(var token=ordering.Register(Identity(),true,true)){
   ulong publishedEpoch=0;long publishedTime=0;ordering.BindAuthority(token,(epoch,time)=>{publishedEpoch=epoch;publishedTime=time;});
   now=706;token.AcquireFocus(Event(token,1,700,0,true));ulong epoch=token.CaptureEpoch;
   now=843;token.Observe(new InputObservation(2,843,true,true,true,true,true,false,false,0,.25f,.75f,0,epoch,true));
   Need(token.OrderedSnapshot(0).Events.Length==0,"heartbeat-added-journal-event");
   token.Observe(Event(token,3,812,1));var ordered=token.OrderedSnapshot(0);
   Need(ordered.Events.Length==1&&ordered.Events[0].Time==812&&ordered.Events[0].Pressed==1,"delayed-native-event-original-time-not-preserved");
   Need(InputSnapshotCodec.U64(ordered.Snapshot,16)==843&&publishedEpoch==epoch&&publishedTime==843,"snapshot-or-MMF-authority-time-regressed");
   now=844;token.Observe(new InputObservation(4,810,true,true,true,true,true,false,false,0,.25f,.75f,0,epoch,true));
   var unchanged=token.OrderedSnapshot(0);Need(unchanged.Events.Length==1&&unchanged.Cursor==ordered.Cursor&&InputSnapshotCodec.U32(unchanged.Snapshot,24)==1&&publishedTime==843,"older-heartbeat-overwrote-new-held-or-journal");
   now=845;token.Observe(Event(token,5,820,0));var up=token.OrderedSnapshot(0);Need(up.Events.Length==2&&up.Events[1].Time==820&&up.Events[1].Released==1,"delayed-up-original-time-not-preserved");
  }
  Console.WriteLine("PASS trusted input target identity, exclusive focus, stale callbacks, coalescing, close, original evidence expiry and heartbeat/native ordering");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
