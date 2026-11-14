using System;

using SpatialWallpaper.Sdk.Capabilities;

static class InputGateChecks {
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static InputObservation Good(long sequence,long time,uint held=0,bool click=false,ulong epoch=1)=>
  new InputObservation(sequence,time,true,true,true,true,true,false,click,held,0.25f,0.75f,0,epoch);
 static InputObservation Heartbeat(long sequence,long time,uint held,ulong epoch){
  var constructor=typeof(InputObservation).GetConstructor(new[]{typeof(long),typeof(long),typeof(bool),typeof(bool),typeof(bool),typeof(bool),typeof(bool),typeof(bool),typeof(bool),typeof(uint),typeof(float),typeof(float),typeof(int),typeof(ulong),typeof(bool)});
  // Before the fix the real collector sends an ordinary observation. Keep
  // that behavior as the red control, rather than failing at compilation.
  return constructor==null?Good(sequence,time,held,false,epoch):(InputObservation)constructor.Invoke(new object[]{sequence,time,true,true,true,true,true,false,false,held,.25f,.75f,0,epoch,true});
 }
 static int Main(){try{
  var gate=new InputGate();
  Need(!gate.Observe(Good(1,0)).Focused,"hover-granted-focus");
  var first=gate.Observe(Good(2,1,1,true));
  Need(first.Focused&&first.Held==1&&first.Pressed==1,"blank-click-not-dispatched");
  Need(gate.Observe(Good(3,2,1,false,gate.CaptureEpoch)).Pressed==0,"repeat-down");
  Need(gate.Observe(Good(4,3,0,false,gate.CaptureEpoch)).Released==1,"release-missing");
  Need(gate.Observe(Good(5,4,4,false,gate.CaptureEpoch)).Pressed==4,"focused-space-missing");
  var lost=gate.Observe(new InputObservation(6,5,false,true,true,true,true,false,false,4,0,0,0,gate.CaptureEpoch));
  Need(!lost.Focused&&lost.Held==0&&lost.Pressed==0&&lost.Released==0&&lost.Epoch>first.Epoch,"loss-leaked-input");
  Need(!gate.Observe(Good(5,4,4,true)).Focused,"late-event-restored-focus");
  var next=gate.Observe(Good(7,6,5,true,gate.CaptureEpoch));
  Need(next.Focused&&next.Held==1&&next.Pressed==1,"preheld-key-imported");
  Need(gate.Observe(Good(8,7,4,false,gate.CaptureEpoch)).Held==0,"preheld-key-resumed");
  gate.Observe(Good(9,8,0,false,gate.CaptureEpoch));
  Need(gate.Observe(Good(10,9,4,false,gate.CaptureEpoch)).Pressed==4,"fresh-key-blocked");
  Need(!gate.Read(260).Focused,"stale-adapter-retained-focus");
  Need(!gate.Observe(Good(11,10,1,true)).Focused,"stale-proof-restored-focus");
  var safe=gate.Observe(Good(12,261,1,true,gate.CaptureEpoch));Need(safe.Focused,"new-focus-failed");
  Need(!gate.Observe(new InputObservation(13,262,true,true,true,true,true,true,false,4,0,0,0,gate.CaptureEpoch)).Focused,"modifier-not-revoked");
  // Each native proof is independently necessary: target, foreground, shell focus,
  // unlocked session, fresh adapter. Unknown evidence is represented as false.
  for(int i=0;i<5;i++){
   var g=new InputGate();bool[] proof={true,true,true,true,true};proof[i]=false;
   Need(!g.Observe(new InputObservation(1,0,proof[0],proof[1],proof[2],proof[3],proof[4],false,true,1,0,0,0,g.CaptureEpoch)).Focused,"missing-proof:"+i);
  }
  var invalid=new InputGate();Need(!invalid.Observe(Good(1,0,128,true)).Focused,"unsupported-key-mask");
  var overflow=new InputGate();overflow.Observe(Good(1,0,1,true));overflow.Revoke(1);
  Need(!overflow.Read(1).Focused,"overflow-not-cleared");
  Need(!overflow.Observe(Good(2,1,1,true)).Focused,"queued-click-after-revoke");
  var repeat=new InputGate();repeat.Observe(Good(1,0,1,true));
  Need(repeat.Read(1).Pressed==0,"heartbeat-replayed-edge");
  // Acquisition confirms a click intent after asynchronous native checks. That
  // click and every already held key belong to the shell, not to the author.
  var acquire=new InputGate(true);
  var acquired=acquire.AcquireFocus(Good(1,10,5,true),20);
  Need(acquired.Focused&&acquired.Held==0&&acquired.Pressed==0&&acquired.Released==0,"focus-request-replayed-click");
  Need(acquire.Observe(Good(2,21,5,false,acquire.CaptureEpoch)).Held==0,"acquire-imported-preheld");
  acquire.Observe(Good(3,22,0,false,acquire.CaptureEpoch));
  Need(acquire.Observe(Good(4,23,1,true,acquire.CaptureEpoch)).Pressed==1,"fresh-click-after-acquire-blocked");
  var pending=new InputGate(true);ulong pendingEpoch=pending.CaptureEpoch;pending.Revoke(15);
  Need(!pending.AcquireFocus(Good(1,10,1,true,pendingEpoch),20).Focused,"revoked-acquire-restored-focus");
  Need(!new InputGate(true).AcquireFocus(Good(1,10,1,true),261).Focused,"expired-acquire-restored-focus");
  Need(!new InputGate(true).AcquireFocus(Good(1,20,1,true),19).Focused,"future-acquire-restored-focus");
  Need(!new InputGate(true).AcquireFocus(Good(1,10),20).Focused,"hover-acquired-focus");
  for(int i=0;i<5;i++){
   var g=new InputGate(true);bool[] proof={true,true,true,true,true};proof[i]=false;
   Need(!g.AcquireFocus(new InputObservation(1,10,proof[0],proof[1],proof[2],proof[3],proof[4],false,true,1,0,0,0,g.CaptureEpoch),20).Focused,"acquire-missing-proof:"+i);
  }
  Need(!new InputGate(true).Observe(Good(1,10,1,true)).Focused,"desktop-observe-bypassed-acquire");
  Need(!new InputGate().AcquireFocus(Good(1,10,1,true),20).Focused,"preview-accepted-desktop-acquire");
  var oldRelease=new InputGate(true);oldRelease.AcquireFocus(Good(1,10,9,true),20);
  Need(!oldRelease.Observe(Good(2,19,0,false,oldRelease.CaptureEpoch)).Focused,"old-release-cleared-suppression");
  Need(!oldRelease.Observe(Good(3,21,8,false,oldRelease.CaptureEpoch)).Focused,"repeat-after-old-release-restored-focus");
  var duplicate=new InputGate(true);var originalRequest=Good(1,10,0,true,duplicate.CaptureEpoch);duplicate.AcquireFocus(originalRequest,20);
  duplicate.Observe(Good(2,21,4,false,duplicate.CaptureEpoch));
  var repeated=duplicate.AcquireFocus(Good(3,22,0,true,duplicate.CaptureEpoch),23);
  Need(repeated.Held==4&&repeated.Pressed==0,"duplicate-acquire-reset-held");
  var sameConfirmation=duplicate.AcquireFocus(Good(1,10,0,true,duplicate.CaptureEpoch),24);
  Need(sameConfirmation.Focused&&sameConfirmation.Held==4,"same-confirmation-reset-held");
  var originalConfirmation=duplicate.AcquireFocus(originalRequest,25);
  Need(originalConfirmation.Focused&&originalConfirmation.Held==4&&originalConfirmation.Pressed==0&&originalConfirmation.Released==0,"original-confirmation-reset-held");
  Need(!duplicate.Read(272).Focused,"duplicate-acquire-renewed-lease");
  Need(!duplicate.AcquireFocus(originalRequest,273).Focused,"original-confirmation-revived-expired-focus");
  var queued=new InputGate(true);ulong beforeConfirm=queued.CaptureEpoch;
  var confirmed=queued.AcquireFocus(Good(1,10,4,true,beforeConfirm),20);
  Need(!queued.Observe(Good(2,20,0,false,beforeConfirm)).Focused,"same-millisecond-old-release-accepted");
  Need(!queued.Observe(Good(3,20,4,false,beforeConfirm)).Focused,"same-millisecond-old-down-accepted");
  var preview=new InputGate();var receiver=new InputFrames();
  receiver.Advance(InputSnapshotCodec.Encode(preview.CaptureEpoch,0,false,0,0,0,0,new uint[7],new uint[7]),0,true);
  var clicked=preview.Observe(Good(1,1,1,true,preview.CaptureEpoch));
  receiver.Advance(InputSnapshotCodec.Encode(clicked.Epoch,1,true,clicked.Held,100,200,0,new uint[]{1,0,0,0,0,0,0},new uint[7]),1,true);
  Need(receiver.Focused&&receiver.GetButtonDown("Primary"),"idle-snapshot-blocked-new-preview-focus");
  var pendingRead=new InputGate(true);pendingRead.Read(10);
  Need(pendingRead.AcquireFocus(Good(1,5,0,true,pendingRead.CaptureEpoch),11).Focused,"heartbeat-rejected-live-focus-request");
  pendingRead.Read(13);
  Need(pendingRead.Observe(Good(2,12,4,false,pendingRead.CaptureEpoch),14).Pressed==4,"heartbeat-rejected-live-queued-key");
  Need(!pendingRead.Observe(Good(3,15,0,false,pendingRead.CaptureEpoch),266).Focused,"queued-old-evidence-renewed-focus");
  var heartbeatOrder=new InputGate(true);heartbeatOrder.AcquireFocus(Good(1,400,0,true,heartbeatOrder.CaptureEpoch),406);ulong liveEpoch=heartbeatOrder.CaptureEpoch;
  var heartbeat=heartbeatOrder.Observe(Heartbeat(2,843,0,liveEpoch),843);
  // Keep the lease live before reproducing the observed 31ms dispatch gap.
  // This first heartbeat must not refresh an already expired focus.
  Need(!heartbeat.Focused,"heartbeat-revived-expired-focus");
  heartbeatOrder=new InputGate(true);heartbeatOrder.AcquireFocus(Good(1,700,0,true,heartbeatOrder.CaptureEpoch),706);liveEpoch=heartbeatOrder.CaptureEpoch;
  heartbeat=heartbeatOrder.Observe(Heartbeat(2,843,0,liveEpoch),843);Need(heartbeat.Focused&&heartbeat.Pressed==0&&heartbeat.Released==0,"live-heartbeat-made-edges");
  var delayedDown=heartbeatOrder.Observe(Good(3,812,1,false,liveEpoch),843);
  Need(delayedDown.Focused&&delayedDown.Pressed==1&&delayedDown.Held==1,"newer-heartbeat-rejected-delayed-native-down");
  Need(delayedDown.EvidenceTime==843,"delayed-native-down-regressed-authority-time");
  var delayedUp=heartbeatOrder.Observe(Good(4,820,0,false,liveEpoch),844);Need(delayedUp.Focused&&delayedUp.Released==1&&delayedUp.EvidenceTime==843,"delayed-native-up-lost-or-time-regressed");
  Need(!heartbeatOrder.Observe(Good(5,819,1,false,liveEpoch),845).Focused,"real-native-time-regression-accepted");
  Need(!heartbeatOrder.Observe(Heartbeat(6,846,0,liveEpoch),846).Focused,"heartbeat-revived-revoked-epoch");
  Console.WriteLine("PASS focus click/hover, edges, revoke, preheld keys, monotonic sequence/time, timeout, evidence and mask bounds, heartbeat/native ordering");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
