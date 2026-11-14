using System;using System.Reflection;using System.Runtime.Serialization;using System.Runtime.InteropServices;using System.Threading.Tasks;
using SpatialWallpaper.Supervision.Interactive;using SpatialWallpaper.Sdk.Capabilities;
static class DesktopMotionReconcileChecks {
 const BindingFlags F=BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public;static readonly Type T=typeof(WindowsDesktopInput);
 static void Set(object o,string n,object v)=>T.GetField(n,F).SetValue(o,v);static object Get(object o,string n)=>T.GetField(n,F).GetValue(o);
 static void Need(bool b,string n){if(!b)throw new Exception(n);Console.WriteLine("PASS "+n);}
 sealed class Target:IDesktopInputTarget {
   public readonly InputGate Gate=new InputGate(true);public readonly System.Collections.Generic.List<InputState> States=new System.Collections.Generic.List<InputState>();public Action AfterObserve;public long Now=1000;public int Observations;public InputObservation Last;
  public Target(){Gate.AcquireFocus(new InputObservation(1,1000,true,true,true,true,true,false,true,0,.1f,.1f,0,Gate.CaptureEpoch),1000);}
   public ulong CaptureEpoch=>Gate.Read(Now).Epoch;public void Observe(InputObservation v){Observations++;Last=v;States.Add(Gate.Observe(v,Now));AfterObserve?.Invoke();}
  public void AcquireFocus(InputObservation v){throw new Exception("unexpected-focus");}public void Revoke()=>Gate.Revoke(Now);
  public InputFocusConfirmation GetFocusConfirmation(){var s=Gate.Read(Now);return new InputFocusConfirmation(s.Epoch,s.Focused);}
 }
 static object Point(int x,int y){var t=T.GetNestedType("Point",BindingFlags.NonPublic);var p=Activator.CreateInstance(t);t.GetField("X").SetValue(p,x);t.GetField("Y").SetValue(p,y);return p;}
 static void Clock(Target target,long at){target.Now=at;T.GetField("AcceptanceNow",BindingFlags.Static|BindingFlags.NonPublic).SetValue(null,at);}
 static WindowsDesktopInput Source(out Target target){target=new Target();var s=(WindowsDesktopInput)FormatterServices.GetUninitializedObject(T);Set(s,"target",target);Set(s,"fence",new InputCommitFence());Set(s,"active",true);Set(s,"activeEpoch",target.CaptureEpoch);Set(s,"activeFence",((InputCommitFence)Get(s,"fence")).Capture());Set(s,"sequence",1L);Set(s,"committedPointerPoint",Point(10,10));Set(s,"committedPointerValid",true);Set(s,"committedPointerEpoch",target.CaptureEpoch);Set(s,"committedPointerFence",Get(s,"activeFence"));Set(s,"width",1000);Set(s,"height",1000);Set(s,"acceptanceOwns",true);Set(s,"acceptanceCursor",Point(20,20));Set(s,"mouseTrace",new long[256]);
   Set(s,"drained",new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously));Set(s,"deferredKeys",Array.CreateInstance(T.GetNestedType("DeferredKey",BindingFlags.NonPublic),64));var st=T.GetNestedType("HeartbeatSample",BindingFlags.NonPublic);var h=Activator.CreateInstance(typeof(InputHeartbeat<>).MakeGenericType(st),true);Set(s,"heartbeat",h);Clock(target,1020);return s;}
 static Func<U> Return<U>(object value)=>()=> (U)value;
 static void Sample(WindowsDesktopInput s,Target t){var st=T.GetNestedType("HeartbeatSample",BindingFlags.NonPublic);var v=Activator.CreateInstance(st,true);foreach(var pair in new[]{Tuple.Create("Point",Point(10,10)),Tuple.Create("Time",(object)1010L),Tuple.Create("Fence",Get(s,"activeFence")),Tuple.Create("Epoch",(object)t.CaptureEpoch),Tuple.Create("Hit",(object)new DesktopHitResult(1,1,1,new IntPtr(1),1010))})st.GetField(pair.Item1,F).SetValue(v,pair.Item2);
  var h=Get(s,"heartbeat");var q=typeof(DesktopMotionReconcileChecks).GetMethod("Return",BindingFlags.Static|BindingFlags.NonPublic).MakeGenericMethod(st).Invoke(null,new[]{v});Need((bool)h.GetType().GetMethod("Begin").Invoke(h,new[]{q}),"sample-begins");((Task)h.GetType().GetProperty("Completion").GetValue(h)).GetAwaiter().GetResult();}
 static void Beat(WindowsDesktopInput s)=>T.GetMethod("Heartbeat",F).Invoke(s,null);
  static void Key(WindowsDesktopInput s,uint time,bool up){var dt=T.GetNestedType("KeyData",BindingFlags.NonPublic);var d=Activator.CreateInstance(dt);dt.GetField("Key").SetValue(d,32u);dt.GetField("Time").SetValue(d,time);dt.GetField("Flags").SetValue(d,up?0x80u:0u);IntPtr p=Marshal.AllocHGlobal(Marshal.SizeOf(dt));try{Marshal.StructureToPtr(d,p,false);T.GetMethod("Keyboard",F).Invoke(s,new object[]{0,new IntPtr(up?0x101:0x100),p});}finally{Marshal.FreeHGlobal(p);}}
  static void Until(Func<bool> condition,string name){var timer=System.Diagnostics.Stopwatch.StartNew();while(!condition()){if(timer.ElapsedMilliseconds>1500)throw new Exception(name);System.Threading.Thread.Sleep(1);}Need(true,name);}
  sealed class DelayedReply: System.IO.Stream {
   readonly System.IO.Stream inner;internal bool Block;internal Func<bool> ShouldBlock;internal readonly TaskCompletionSource<bool> Entered=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);readonly TaskCompletionSource<bool> release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
   internal DelayedReply(System.IO.Stream stream){inner=stream;}
   public override async Task<int> ReadAsync(byte[] b,int o,int c,System.Threading.CancellationToken token){int n=await inner.ReadAsync(b,o,c,token);if(Block||(ShouldBlock?.Invoke()??false)){Entered.TrySetResult(true);await release.Task;}return n;}
   public override Task WriteAsync(byte[] b,int o,int c,System.Threading.CancellationToken token)=>inner.WriteAsync(b,o,c,token);
   protected override void Dispose(bool disposing){release.TrySetCanceled();inner.Dispose();base.Dispose(disposing);}
   public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>throw new NotSupportedException();public override long Position{get=>0;set=>throw new NotSupportedException();}public override void Flush()=>inner.Flush();public override int Read(byte[] b,int o,int c)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int c)=>throw new NotSupportedException();public override long Seek(long o,System.IO.SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long length)=>throw new NotSupportedException();
  }
  static void Pipe(bool saturated=false,bool acceptedDown=false){
   var s=Source(out var t);Sample(s,t);Beat(s);Clock(t,1000);
   try{using(var broker=new InputBroker(()=>t.Now))using(var target=broker.Register(new SpatialWallpaper.Sdk.Hosting.WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),true)){
    string name="SpatialWallpaper.DeferredKeys."+Guid.NewGuid().ToString("N");
    using(var server=new System.IO.Pipes.NamedPipeServerStream(name,System.IO.Pipes.PipeDirection.InOut,1,System.IO.Pipes.PipeTransmissionMode.Byte,System.IO.Pipes.PipeOptions.Asynchronous))using(var client=new System.IO.Pipes.NamedPipeClientStream(".",name,System.IO.Pipes.PipeDirection.InOut,System.IO.Pipes.PipeOptions.Asynchronous)){
     var serving=Task.Run(()=>{server.WaitForConnection();try{SpatialWallpaper.Supervision.Broker.DesktopInputChannel.Serve(server,target);}catch(System.IO.IOException){}catch(TimeoutException){}});client.Connect(1000);
     using(var replies=new DelayedReply(client))using(var channel=new SpatialWallpaper.Supervision.Broker.DesktopInputChannel(replies)){
      channel.AcquireFocus(new InputObservation(1,1000,true,true,true,true,true,false,true,0,.1f,.1f,0,channel.CaptureEpoch));Until(()=>channel.GetFocusConfirmation().Focused,"pipe-focus-confirmed");
      var frames=new InputFrames();frames.Advance(target.Snapshot(),1000,true);Need(frames.Focused,"pipe-frame-baseline");
      Set(s,"target",channel);Set(s,"activeEpoch",channel.CaptureEpoch);Set(s,"acceptanceCursor",Point(20,20));Clock(t,1020);Key(s,1020,false);Clock(t,1021);Key(s,1021,true);
      Need(target.Downs[2]==0&&target.Ups[2]==0,"pipe-no-edges-before-move-validation");
      if(saturated){
       if(acceptedDown){
        replies.ShouldBlock=()=>target.Downs[2]==1;Move(s,20,20,1019);
        Need(replies.Entered.Task.Wait(1000),"pipe-held-down-confirmation-blocked");
        Need(target.Downs[2]==1&&target.Ups[2]==0,"pipe-host-accepted-down-before-capacity-fault");
        frames.Advance(target.Snapshot(),1021,true);Need(frames.Focused&&frames.GetButton("Space")&&frames.GetButtonDown("Space"),"pipe-author-observed-held-down-before-overflow");
        // The release is already queued behind the in-flight down reply. Fill
        // its remaining 63 slots before a new raw MOVE reaches the full queue.
        for(int i=5;i<=67;i++)channel.Observe(new InputObservation(i,1021,true,true,true,true,true,false,false,4,.02f,.02f,0,channel.CaptureEpoch));
        Set(s,"sequence",67L);Set(s,"acceptanceCursor",Point(30,30));Clock(t,1022);
        Move(s,30,30,1022,allowFailure:true);
       }else{
       replies.Block=true;channel.Observe(new InputObservation(2,1018,true,true,true,true,true,false,false,0,.1f,.1f,0,channel.CaptureEpoch));Need(replies.Entered.Task.Wait(1000),"pipe-confirmation-held-after-host-acceptance");
       for(int i=3;i<=64;i++)channel.Observe(new InputObservation(i,1018,true,true,true,true,true,false,false,0,.1f,.1f,0,channel.CaptureEpoch));Set(s,"sequence",64L);
       Move(s,20,20,1019,allowFailure:true);}
       Need((bool)Get(s,"closed")&&!channel.GetFocusConfirmation().Ready,"pipe-partial-enqueue-overflow-closes-source");
       Need(channel.Completion.Wait(1000),"pipe-overflow-client-cleaned");Need(serving.Wait(1000),"pipe-overflow-server-cleaned");serving.GetAwaiter().GetResult();
       Need((bool)T.GetMethod("ReleaseOwner",F).Invoke(s,null)&&(int)Get(s,"deferredKeyCount")==0,"pipe-overflow-owner-releases-deferred-events");
       frames.Advance(target.Snapshot(),t.Now,true);Need(!frames.Focused&&!frames.GetButton("Space")&&!frames.GetButtonDown("Space"),acceptedDown?"pipe-accepted-down-overflow-final-author-state-cleared":"pipe-overflow-final-author-state-cleared");
      }else{
      Move(s,20,20,1019);
      Until(()=>target.Downs[2]==1&&target.Ups[2]==1,"pipe-host-preserves-both-deferred-edges");
      frames.Advance(target.Snapshot(),1021,true);Need(frames.Focused&&!frames.GetButton("Space")&&frames.GetButtonDown("Space")&&frames.GetButtonUp("Space"),"pipe-worker-frame-exposes-short-tap");
      Need(target.Gate.Read(1021).EvidenceTime==1021,"pipe-retains-original-evidence-time");channel.Dispose();Need(channel.Completion.Wait(1000),"pipe-client-cleaned");}
     }
     Need(serving.Wait(1000),"pipe-server-cleaned");serving.GetAwaiter().GetResult();Need(!target.GetFocusConfirmation().Focused,"pipe-disconnect-clears-author-focus");
    }
   }}finally{var h=Get(s,"heartbeat");h.GetType().GetMethod("Close").Invoke(h,null);((Task)h.GetType().GetProperty("Completion").GetValue(h)).GetAwaiter().GetResult();}
  }
  static IntPtr Move(WindowsDesktopInput s,int x,int y,uint time,uint flags=0,int kind=0x200,bool allowFailure=false){var dt=T.GetNestedType("MouseData",BindingFlags.NonPublic);object d=Activator.CreateInstance(dt);dt.GetField("Point").SetValue(d,Point(x,y));dt.GetField("Time").SetValue(d,time);dt.GetField("Flags").SetValue(d,flags);IntPtr p=Marshal.AllocHGlobal(Marshal.SizeOf(dt));IntPtr result=IntPtr.Zero;try{Marshal.StructureToPtr(d,p,false);Set(s,"mouseTraceCount",(long)Get(s,"mouseTraceCount")+1);result=(IntPtr)T.GetMethod("MouseCore",F).Invoke(s,new object[]{0,new IntPtr(kind),p,0});}finally{Marshal.FreeHGlobal(p);}Need(allowFailure?(string)Get(s,"lastMouseFailure")=="System.IO.IOException":Get(s,"lastMouseFailure")==null,allowFailure?"move-reports-channel-overflow":"move-no-hidden-exception");return result;}
 static void Run(string kind){var s=Source(out var t);try{Sample(s,t);ulong epoch=t.CaptureEpoch;Beat(s);Need((bool)Get(s,"active")&&t.CaptureEpoch==epoch&&t.Observations==0,"cursor-ahead-of-hook-drops-sample-without-revoke-or-renew");
  Set(s,"acceptanceCursor",Point(10,10));for(int i=0;i<4;i++)Beat(s);Need(!(bool)Get(s,"heartbeat").GetType().GetProperty("Busy").GetValue(Get(s,"heartbeat"))&&(int)Get(s,"acceptanceQueries")==0&&t.Observations==0,"return-to-old-point-cannot-restart-heartbeat-before-raw-move");
  Need((int)Get(s,"mismatchSampleX")==10&&(int)Get(s,"mismatchCursorX")==20&&(long)Get(s,"mismatchMoveOrdinal")==0,"mismatch-records-sample-current-and-raw-ordinal");
  if(kind=="blank"||kind=="uninjected"){Set(s,"acceptanceCursor",Point(30,30));Move(s,20,20,1015);Need(t.Observations==0&&(bool)Get(s,"active"),"valid-intermediate-move-does-not-renew");Move(s,30,30,1016,kind=="blank"?1u:0u);Need(t.Observations==1&&t.Last.Time==1016&&t.CaptureEpoch==epoch,"caught-up-blank-move-publishes-original-time");Need((long)Get(s,"committedMoveOrdinal")==2&&!(bool)Get(s,"movementPending"),"only-successful-catchup-commits-move-ordinal");}
  else if(kind=="icon"||kind=="boundary"){Set(s,"acceptanceCursor",Point(10,10));Move(s,kind=="icon"?99:1100,20,1015);Move(s,10,10,1016);Need(!(bool)Get(s,"active")&&!t.Gate.Read(t.Now).Focused&&t.Observations==0,"intermediate-"+kind+"-and-return-cannot-revive");}
   else if(kind.StartsWith("queued-")){
    Set(s,"acceptanceCursor",Point(20,20));if(kind=="queued-stale-hit")Set(s,"acceptanceHitAge",40L);Key(s,1020,false);Need(t.Observations==0&&(bool)Get(s,"active"),"queued-key-starts-without-publishing");
    if(kind=="queued-full"||kind=="queued-same-time"){
     int count=kind=="queued-full"?64:2;for(int i=1;i<count;i++)Key(s,1020,(i&1)!=0);Move(s,20,20,1020);
     Need(t.States.Count==count+1&&t.CaptureEpoch==epoch&&t.States[count].Held==0,"queued-full-or-same-time-batch-preserves-epoch");
     for(int i=0;i<count;i++)Need(t.States[i].EvidenceTime==1020&&((i&1)==0?t.States[i].Pressed==4:t.States[i].Released==4),"queued-batch-preserves-every-edge-"+i);
    }
    else if(kind=="queued-mid-expiry"||kind=="queued-mid-epoch"||kind=="queued-mid-fence"){
     Clock(t,1021);Key(s,1021,true);t.AfterObserve=()=>{if(t.Observations!=1)return;if(kind=="queued-mid-expiry")Clock(t,1100);else if(kind=="queued-mid-epoch")t.Revoke();else{var f=(InputCommitFence)Get(s,"fence");f.Invalidate(()=>{},false);Set(s,"activeFence",f.Capture());}};
     Move(s,20,20,1019);Need(t.Observations==1&&!(bool)Get(s,"active")&&(int)Get(s,"deferredKeyCount")==0,"queued-mid-flush-invalidation-stops-suffix");
    }
    else if(kind=="queued-newer-move"||kind=="queued-between-move"||kind=="queued-equal-move"){
     Clock(t,1023);Key(s,1022,true);uint moveTime=kind=="queued-newer-move"?1023u:kind=="queued-between-move"?1021u:1020u;Move(s,20,20,moveTime);
     Need(t.States.Count==3&&t.CaptureEpoch==epoch&&t.States[0].Pressed==4&&t.States[2].Held==0,"queued-order-keeps-both-key-edges");
     if(kind=="queued-newer-move")Need(t.States[1].Released==4&&t.States[2].EvidenceTime==1023,"newer-move-follows-key-pair");
     else Need(t.States[1].EvidenceTime==moveTime&&t.States[1].Held==4&&t.States[2].Released==4,"middle-or-equal-move-cannot-use-future-key-state");
    }else{
     Clock(t,1022);
     if(kind=="queued-overflow"){for(int i=0;i<64;i++)Key(s,1020,false);}
     else if(kind=="queued-lease"){Clock(t,1251);Beat(s);}
     else if(kind=="queued-stale-hit"){Set(s,"acceptanceHitAge",0L);Clock(t,1031);Need(t.Gate.Read(t.Now).Focused,"queued-stale-hit-original-lease-still-valid");Move(s,20,20,1031);}
     else if(kind=="queued-changed-hit"){Set(s,"acceptanceBlockedX",20);Set(s,"acceptanceCursor",Point(30,30));Move(s,30,30,1021);}
     else if(kind=="queued-icon"){Move(s,99,20,1021);Move(s,20,20,1022);}
     else if(kind=="queued-epoch"){t.Revoke();Move(s,20,20,1021);}
     else if(kind=="queued-fence"){((InputCommitFence)Get(s,"fence")).Invalidate(t.Revoke,false);Move(s,20,20,1021);}
     else if(kind=="queued-owner"){Set(s,"acceptanceOwns",false);Move(s,20,20,1021);}
     else if(kind=="queued-time-regression"){Key(s,1019,true);}
     else if(kind=="queued-mouse-release"){Set(s,"consumed",1u);Set(s,"routed",1u);int forwards=(int)Get(s,"acceptanceForwards");Need(Move(s,20,20,1021,0,0x202)==new IntPtr(1)&&(int)Get(s,"acceptanceForwards")==forwards,"queued-key-preserves-consumed-mouse-up");}
     else throw new Exception("unknown-queued-case");
     Need(!(bool)Get(s,"active")&&t.Observations==0&&(int)Get(s,"deferredKeyCount")==0,"invalid-queued-path-revokes-and-discards-without-publishing");
    }
   }
   else if(kind=="keyboard-interleave"){
    Set(s,"acceptanceCursor",Point(20,20));
    var dt=T.GetNestedType("KeyData",BindingFlags.NonPublic);var d=Activator.CreateInstance(dt);dt.GetField("Key").SetValue(d,32u);dt.GetField("Time").SetValue(d,1020u);IntPtr p=Marshal.AllocHGlobal(Marshal.SizeOf(dt));
    try{Marshal.StructureToPtr(d,p,false);T.GetMethod("Keyboard",F).Invoke(s,new object[]{0,new IntPtr(0x100),p});}finally{Marshal.FreeHGlobal(p);}
    Need(t.Observations==0,"interleaved-key-does-not-authorize-unconsumed-move");
    Need((bool)Get(s,"active")&&t.CaptureEpoch==epoch,"valid-key-interleave-retains-original-focus-epoch");
    dt.GetField("Flags").SetValue(d,0x80u);dt.GetField("Time").SetValue(d,1021u);Clock(t,1021);p=Marshal.AllocHGlobal(Marshal.SizeOf(dt));try{Marshal.StructureToPtr(d,p,false);T.GetMethod("Keyboard",F).Invoke(s,new object[]{0,new IntPtr(0x101),p});}finally{Marshal.FreeHGlobal(p);}
    Need(t.Observations==0,"short-key-pair-waits-without-renewal");
    Move(s,20,20,1019);
    Need(t.CaptureEpoch==epoch&&t.States.Count==3,"older-move-and-both-key-edges-keep-epoch");
    Need(t.States[0].EvidenceTime==1019&&t.States[0].Held==0&&t.States[1].EvidenceTime==1020&&t.States[1].Pressed==4&&t.States[2].EvidenceTime==1021&&t.States[2].Released==4,"queued-short-pair-preserves-original-time-and-both-edges");
   }
   else if(kind=="keyboard-up"){Set(s,"keyHeld",4u);Set(s,"committedHeld",4u);t.Gate.Observe(new InputObservation(2,1020,true,true,true,true,true,false,false,4,.1f,.1f,0,t.CaptureEpoch),1020);Need(t.Gate.Read(t.Now).Held==4,"mapped-key-held-before-reconciliation-up");Set(s,"sequence",2L);
   var dt=T.GetNestedType("KeyData",BindingFlags.NonPublic);var d=Activator.CreateInstance(dt);dt.GetField("Key").SetValue(d,32u);dt.GetField("Flags").SetValue(d,0x80u);dt.GetField("Time").SetValue(d,1020u);IntPtr p=Marshal.AllocHGlobal(Marshal.SizeOf(dt));int forwards=(int)Get(s,"acceptanceForwards");try{Marshal.StructureToPtr(d,p,false);var result=(IntPtr)T.GetMethod("Keyboard",F).Invoke(s,new object[]{0,new IntPtr(0x101),p});Need(result==IntPtr.Zero&&(int)Get(s,"acceptanceForwards")==forwards+1,"mapped-key-up-still-forwards-once");}finally{Marshal.FreeHGlobal(p);}
    Need((uint)Get(s,"keyHeld")==0&&(bool)Get(s,"active")&&t.Observations==0,"mapped-key-up-queues-without-renewing-waiting-focus");Move(s,10,10,1020);Need(t.CaptureEpoch==epoch&&t.Gate.Read(t.Now).Held==0&&t.States.Exists(v=>v.Released==4),"mapped-key-up-releases-after-valid-move");
  }
   else if(kind=="raw-move-mid-fence"){
    Set(s,"acceptanceCursor",Point(20,20));t.AfterObserve=()=>{var f=(InputCommitFence)Get(s,"fence");f.Invalidate(()=>{},false);Set(s,"activeFence",f.Capture());};Move(s,20,20,1015);
    Need(t.Observations==1&&!(bool)Get(s,"active")&&!(bool)Get(s,"committedPointerValid"),"old-move-cannot-adopt-reentrant-new-fence");
   }
   else if(kind=="cursor-ahead-start"){
    Set(s,"acceptanceCursor",Point(20,20));Move(s,20,20,1015);Set(s,"acceptanceCursor",Point(30,30));Beat(s);var h=Get(s,"heartbeat");((Task)h.GetType().GetProperty("Completion").GetValue(h)).GetAwaiter().GetResult();Need(t.Observations==1&&(int)Get(s,"acceptanceQueries")==0,"ahead-cursor-waits-for-raw-move-before-query");Move(s,30,30,1016);Beat(s);((Task)h.GetType().GetProperty("Completion").GetValue(h)).GetAwaiter().GetResult();Beat(s);Need(t.Observations==3&&t.Last.X==.03f&&t.CaptureEpoch==epoch,"ahead-cursor-resumes-after-valid-raw-move");
   }
   else if(kind.StartsWith("cursor-lags-")){
    Set(s,"acceptanceCursor",Point(20,20));Move(s,20,20,1015);Move(s,30,30,1016);Need(t.Observations==2&&t.Last.X==.03f,"raw-move-commits-before-global-cursor-catches-up");
     var global=Get(s,"acceptanceCursor");var committed=Get(s,"committedPointerPoint");
     Need((bool)Get(s,"active")&&t.CaptureEpoch==epoch&&(bool)Get(s,"committedPointerValid")&&
      (ulong)Get(s,"committedPointerEpoch")==epoch&&(int)global.GetType().GetField("X").GetValue(global)==20&&
      (int)committed.GetType().GetField("X").GetValue(committed)==30,"lagging-cursor-precondition-active-same-epoch-distinct-positions");
    Beat(s);var h=Get(s,"heartbeat");((Task)h.GetType().GetProperty("Completion").GetValue(h)).GetAwaiter().GetResult();
    Need((int)Get(s,"acceptanceQueries")==0&&!(bool)Get(s,"movementPending")&&t.Observations==2,"lagging-global-cursor-cannot-start-old-point-heartbeat");
    if(kind=="cursor-lags-lease"){for(int i=0;i<4;i++)Beat(s);Clock(t,1300);Beat(s);Need(!t.Gate.Read(t.Now).Focused&&t.Observations==2&&(int)Get(s,"acceptanceQueries")==0,"lagging-cursor-never-renews-original-lease");return;}
    if(kind=="cursor-lags-epoch"||kind=="cursor-lags-fence"){Set(s,kind=="cursor-lags-epoch"?"committedPointerEpoch":"committedPointerFence",kind=="cursor-lags-epoch"?(object)(epoch+1):(object)(((InputCommitFence)Get(s,"fence")).Capture()+1));Beat(s);Need(!(bool)Get(s,"active")&&!(bool)Get(s,"committedPointerValid")&&(int)Get(s,"acceptanceQueries")==0,"pointer-from-other-identity-cannot-start-heartbeat");return;}
    if(kind=="cursor-lags-key"){Key(s,1020,false);Beat(s);Need(t.Observations==3&&(int)Get(s,"acceptanceQueries")==0,"keyboard-cannot-overwrite-committed-raw-pointer");}
    Set(s,"acceptanceCursor",Point(30,30));Beat(s);((Task)h.GetType().GetProperty("Completion").GetValue(h)).GetAwaiter().GetResult();Beat(s);
    Need(t.Observations==(kind=="cursor-lags-key"?4:3)&&t.Last.X==.03f&&t.CaptureEpoch==epoch&&!(bool)Get(s,"movementPending"),"cursor-catchup-without-extra-move-resumes-same-epoch");
   }
   else if(kind=="handoff"){Set(s,"acceptanceCursor",Point(20,20));Move(s,20,20,1015);Need(t.Observations==1,"handoff-catchup-published-once");Sample(s,t);Move(s,20,20,1016);Need(t.Observations==2,"native-move-invalidates-older-completed-heartbeat");Beat(s);Need(t.Observations==2,"obsolete-sample-not-published-after-native-move");var h=Get(s,"heartbeat");((Task)h.GetType().GetProperty("Completion").GetValue(h)).GetAwaiter().GetResult();Beat(s);Need(t.Observations==3&&t.Last.Time==1020&&t.Last.X==.02f&&t.CaptureEpoch==epoch,"new-heartbeat-resumes-only-after-catchup-and-keeps-new-point");}
  else if(kind=="release"){Set(s,"consumed",1u);Set(s,"routed",1u);int forwards=(int)Get(s,"acceptanceForwards");var result=Move(s,20,20,1015,0,0x202);Need(result==new IntPtr(1)&&(int)Get(s,"acceptanceForwards")==forwards,"owned-up-is-consumed-without-native-forward");Need((uint)Get(s,"consumed")==0&&!(bool)Get(s,"active")&&t.Observations==0,"owned-up-drains-even-when-waiting-for-move-rejects-publication");}
  else if(kind=="stale"){Clock(t,1300);Move(s,10,10,1015);Need(t.Observations==0&&!(bool)Get(s,"active"),"stale-raw-move-cannot-clear-wait");}
  else if(kind=="fence"){((InputCommitFence)Get(s,"fence")).Invalidate(t.Revoke,false);Beat(s);Need(!(bool)Get(s,"active"),"waiting-fence-change-revokes");}
  else if(kind=="focus"){Set(s,"acceptanceOwns",false);Beat(s);Need(!(bool)Get(s,"active"),"waiting-lost-focus-revokes");}
  else if(kind=="epoch"){t.Revoke();Beat(s);Need(!(bool)Get(s,"active")&&t.Observations==0,"waiting-changed-epoch-revokes");}
  else if(kind=="lease"){Clock(t,1251);Beat(s);Move(s,10,10,1251);Need(!t.Gate.Read(t.Now).Focused&&t.Observations==0,"waiting-no-events-original-250ms-lease-expires");}
  }finally{var h=Get(s,"heartbeat");h.GetType().GetMethod("Close").Invoke(h,null);((Task)h.GetType().GetProperty("Completion").GetValue(h)).GetAwaiter().GetResult();}
 }
  static int Main(string[] args){try{if(args.Length==1&&args[0]=="pipe-accepted-down-overflow"){Pipe(true,true);return 0;}foreach(var c in args.Length==0?new[]{"blank","uninjected","icon","boundary","focus","epoch","lease","stale","fence","release","keyboard-up","handoff","raw-move-mid-fence","cursor-ahead-start","cursor-lags-hook","cursor-lags-lease","cursor-lags-key","cursor-lags-epoch","cursor-lags-fence","keyboard-interleave","queued-newer-move","queued-between-move","queued-equal-move","queued-overflow","queued-lease","queued-stale-hit","queued-changed-hit","queued-icon","queued-epoch","queued-fence","queued-owner","queued-time-regression","queued-mouse-release","queued-mid-expiry","queued-mid-epoch","queued-mid-fence","queued-full","queued-same-time"}:args)Run(c);if(args.Length==0){Pipe();Pipe(true);Pipe(true,true);}return 0;}catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
