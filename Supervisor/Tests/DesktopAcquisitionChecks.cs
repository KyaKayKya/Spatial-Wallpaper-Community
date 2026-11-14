using System;using System.Reflection;using System.Runtime.Serialization;using System.Threading.Tasks;
using SpatialWallpaper.Supervision.Interactive;using SpatialWallpaper.Sdk.Capabilities;
// State-only negatives run the production Acquire method without hooks or a
// window. They must reject before any OS focus or cursor-dependent operation.
static class DesktopAcquisitionChecks {
 sealed class Target:IDesktopInputTarget {
  public ulong CaptureEpoch{get;set;}=7;public int Revocations,Grants;
  public void Observe(InputObservation value){throw new Exception("unexpected-observation");}
  public void Revoke(){Revocations++;}
  public void AcquireFocus(InputObservation value){Grants++;throw new Exception("unexpected-grant");}
  public InputFocusConfirmation GetFocusConfirmation()=>new InputFocusConfirmation(CaptureEpoch,false);
 }
 static readonly Type Type=typeof(WindowsDesktopInput);
 static void Set(object source,string name,object value)=>Type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(source,value);
 static object Get(object source,string name)=>Type.GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(source);
 static void Need(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
  static void Bind(WindowsDesktopInput source){var fence=new InputCommitFence();Set(source,"fence",fence);Set(source,"clickFence",fence.Capture());Set(source,"clickSerial",1L);var trace=new long[256];Set(source,"mouseTrace",trace);Set(source,"mouseRawTrace",new WindowsDesktopInput.MouseRawTrace(trace));}
 static void Acquire(object source)=>Type.GetMethod("Acquire",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(source,null);
 static void Mouse(object source,int message,int x,int y){
  var pointType=Type.GetNestedType("Point",BindingFlags.NonPublic);object point=Activator.CreateInstance(pointType);pointType.GetField("X").SetValue(point,x);pointType.GetField("Y").SetValue(point,y);
  var dataType=Type.GetNestedType("MouseData",BindingFlags.NonPublic);object data=Activator.CreateInstance(dataType);dataType.GetField("Point").SetValue(data,point);dataType.GetField("Time").SetValue(data,unchecked((uint)WindowsMachineClock.Milliseconds()));
  IntPtr memory=System.Runtime.InteropServices.Marshal.AllocHGlobal(System.Runtime.InteropServices.Marshal.SizeOf(dataType));try{System.Runtime.InteropServices.Marshal.StructureToPtr(data,memory,false);Type.GetMethod("Mouse",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(source,new object[]{0,new IntPtr(message),memory});}finally{System.Runtime.InteropServices.Marshal.FreeHGlobal(memory);}
 }
 static int Main(){try{
  foreach(bool completed in new[]{false,true})foreach(bool wheel in new[]{false,true}){
   var source=(WindowsDesktopInput)FormatterServices.GetUninitializedObject(Type);Bind(source);var target=new Target();Set(source,"target",target);Set(source,"pending",true);Set(source,"completeClick",completed);
   var reply=new TaskCompletionSource<DesktopHitResult>();Set(source,"acquisition",reply.Task);Set(source,"clickEpoch",7UL);Set(source,"clickTime",WindowsMachineClock.Milliseconds());
   Mouse(source,wheel?0x20a:0x200,wheel?0:5,0);Mouse(source,0x200,0,0);
   Need(target.Revocations==1&&!(bool)Get(source,"pending")&&!(bool)Get(source,"completeClick"),"pending-"+(wheel?"wheel":"leave-and-return")+"-revoked-"+completed);
   Need((string)Get(source,"lastRevocation")== (wheel?"pending-wheel":"pending-pointer-left-click")&&Get(source,"lastMouseFailure")==null&&!(bool)Get(source,"closed"),"pending-revocation-is-not-exception-close");
   reply.SetResult(new DesktopHitResult(1,1,1,new IntPtr(1),WindowsMachineClock.Milliseconds()));Acquire(source);Need(target.Grants==0&&target.Revocations==1,"late-query-cannot-revive-interrupted-click-"+wheel+"-"+completed);
  }
  {var source=(WindowsDesktopInput)FormatterServices.GetUninitializedObject(Type);Bind(source);var target=new Target();Set(source,"target",target);Set(source,"pending",true);Mouse(source,0x200,4,-4);Need(target.Revocations==0&&(bool)Get(source,"pending"),"pending-pointer-tolerance-boundary-retained");}
  foreach(bool expired in new[]{false,true}){
   var source=(WindowsDesktopInput)FormatterServices.GetUninitializedObject(Type);Bind(source);var target=new Target();
   var pending=new TaskCompletionSource<DesktopHitResult>(TaskCreationOptions.RunContinuationsAsynchronously);
   Set(source,"target",target);Set(source,"pending",true);Set(source,"completeClick",true);Set(source,"clickEpoch",expired?7UL:6UL);
   Set(source,"clickTime",WindowsMachineClock.Milliseconds()-(expired?1000:0));Set(source,"acquisition",pending.Task);
   Acquire(source);
   string reason=(string)Get(source,"lastRevocation");
   Need(target.Grants==0&&target.Revocations==1&&!(bool)Get(source,"pending")&&!(bool)Get(source,"completeClick")&&reason.StartsWith(expired?"acquire-expired-age-":"acquire-epoch-"),expired?"original-click-expires-while-query-pending":"epoch-change-revokes-pending-query");
   pending.SetResult(new DesktopHitResult(1,1,1,new IntPtr(1),WindowsMachineClock.Milliseconds()));
   Acquire(source);
   Need(target.Grants==0&&target.Revocations==1&&!(bool)Get(source,"awaiting")&&!(bool)Get(source,"active"),expired?"late-fresh-reply-does-not-renew-expired-click":"late-fresh-reply-does-not-restore-old-epoch");
  }
  {
   var source=(WindowsDesktopInput)FormatterServices.GetUninitializedObject(Type);Bind(source);var target=new Target();Set(source,"target",target);
   var done=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var owner=new OwnedInputThread(()=>{},()=>{},()=>true);Set(source,"owner",owner);
   try{Need(owner.Started.Wait(2000),"expiry-diagnostic-owner-started");owner.Post(()=>{try{
    Set(source,"pending",true);Type.GetMethod("Revoke",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(source,new object[]{"ordinary-focus-loss"});
    Need(owner.ReadDiagnostic().Failure==null,"ordinary-revoke-does-not-freeze-failure");
    Set(source,"clickFence",((InputCommitFence)Get(source,"fence")).Capture());Set(source,"pending",true);Set(source,"completeClick",true);Set(source,"clickEpoch",target.CaptureEpoch);Set(source,"clickTime",WindowsMachineClock.Milliseconds()-1000);
    System.Threading.Thread.Sleep(120);Acquire(source);done.SetResult(true);
   }catch(Exception error){done.SetException(error);}});Need(done.Task.Wait(2000),"actual-expired-acquire-executed");System.Threading.Thread.Sleep(3000);
    var wait=System.Diagnostics.Stopwatch.StartNew();var live=owner.ReadDiagnostic();while(!live.Available&&wait.ElapsedMilliseconds<1000){System.Threading.Thread.Sleep(1);live=owner.ReadDiagnostic();}var frozen=live.Failure;Need(frozen!=null&&frozen.Available&&frozen.Reason=="acquire-expired"&&frozen.CapturedAtQpc>0,"actual-acquire-freeze-survives-delayed-capture");
    Need(live.Total-frozen.Total>512&&target.Grants==0&&target.Revocations==2,"actual-acquire-evidence-survives-live-overwrite-with-no-grant");
   }finally{owner.Dispose();Need(owner.Completion.Wait(3000),"expiry-diagnostic-owner-exited");}
  }
  {
   var source=(WindowsDesktopInput)FormatterServices.GetUninitializedObject(Type);var record=Type.GetMethod("RecordHit",BindingFlags.Instance|BindingFlags.NonPublic);
   var helper=(DesktopHitClient)FormatterServices.GetUninitializedObject(typeof(DesktopHitClient));Set(source,"helper",helper);
   Action<string,long> helperValue=(name,value)=>typeof(DesktopHitClient).GetField("<"+name+">k__BackingField",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(helper,value);
   helperValue("LastQuerySequence",17);helperValue("LastQueryBeganAt",100);helperValue("LastQueryCompletedAt",200);
   long at=WindowsMachineClock.Milliseconds();record.Invoke(source,new object[]{new DesktopHitResult(2,19,1,new IntPtr(23),at)});
   Need((bool)Get(source,"lastHitPresent")&&(uint)Get(source,"lastHitKind")==2&&(uint)Get(source,"lastHitPid")==19&&(long)Get(source,"lastHitWindow")==23&&(long)Get(source,"lastHitObservedAt")==at&&(long)Get(source,"lastHitConsumedQpc")>0&&(long)Get(source,"lastHitConsumedTime")>=at,"raw-rejected-hit-telemetry-retained");
   helperValue("LastQuerySequence",18);helperValue("LastQueryBeganAt",300);helperValue("LastQueryCompletedAt",400);
   Need((long)Get(source,"lastHitQuerySequence")==17&&(long)Get(source,"lastHitQueryBeganQpc")==100&&(long)Get(source,"lastHitQueryEndedQpc")==200,"consumed-query-telemetry-survives-later-helper-query");
   record.Invoke(source,new object[]{null});
   Need(!(bool)Get(source,"lastHitPresent")&&(uint)Get(source,"lastHitKind")==0&&(uint)Get(source,"lastHitPid")==0&&(long)Get(source,"lastHitWindow")==0&&(long)Get(source,"lastHitObservedAt")==0,"null-hit-does-not-reuse-previous-telemetry");
  }
  {
   var wakeType=Type.Assembly.GetType("SpatialWallpaper.Supervision.Interactive.AcquisitionWakeup");Need(wakeType!=null,"completion-wakeup-available");
   int owner=0,request=0,calls=0;var sent=new System.Threading.ManualResetEventSlim();
   Func<int,int,bool> post=(o,r)=>{owner=o;request=r;System.Threading.Interlocked.Increment(ref calls);sent.Set();return true;};
   object wake=Activator.CreateInstance(wakeType,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,new object[]{post},null);
   var arm=wakeType.GetMethod("Arm");var matches=wakeType.GetMethod("Matches");var close=wakeType.GetMethod("Dispose");
   Func<int,int,bool> valid=(o,r)=>(bool)matches.Invoke(wake,new object[]{(long)o,(long)r});
   var old=new TaskCompletionSource<bool>();int oldId=(int)arm.Invoke(wake,new object[]{old.Task});
   int current=(int)arm.Invoke(wake,new object[]{Task.CompletedTask});Need(sent.Wait(1000)&&calls==1&&request==current&&valid(owner,current),"already-complete-task-posts-matching-notification");
   old.SetResult(true);Need(calls==1&&!valid(owner,oldId),"superseded-request-cannot-post-or-match");
   Need(!valid(owner+1,current)&&!valid(owner,current+1),"foreign-owner-and-request-rejected");
   object newer=Activator.CreateInstance(wakeType,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,new object[]{new Func<int,int,bool>((o,r)=>true)},null);
   int newId=(int)arm.Invoke(newer,new object[]{Task.CompletedTask});Need(!(bool)matches.Invoke(newer,new object[]{(long)owner,(long)newId}),"reused-window-new-owner-rejects-old-notification");close.Invoke(newer,null);
   var invalidated=new TaskCompletionSource<bool>();int invalidId=(int)arm.Invoke(wake,new object[]{invalidated.Task});wakeType.GetMethod("Invalidate").Invoke(wake,null);invalidated.SetResult(true);
   Need(calls==1&&!valid(owner,invalidId),"revoke-invalidates-late-and-queued-notification");
   object failed=Activator.CreateInstance(wakeType,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,new object[]{new Func<int,int,bool>((o,r)=>false)},null);
   arm.Invoke(failed,new object[]{Task.CompletedTask});Need((int)wakeType.GetProperty("Failures").GetValue(failed)==1,"failed-post-is-recorded-without-faulted-continuation");close.Invoke(failed,null);
   object throwing=Activator.CreateInstance(wakeType,BindingFlags.Instance|BindingFlags.NonPublic|BindingFlags.Public,null,new object[]{new Func<int,int,bool>((o,r)=>{throw new Exception("post-fault");})},null);
   arm.Invoke(throwing,new object[]{Task.CompletedTask});Need((int)wakeType.GetProperty("Failures").GetValue(throwing)==1,"post-exception-is-observed");close.Invoke(throwing,null);
   var later=new TaskCompletionSource<bool>();arm.Invoke(wake,new object[]{later.Task});close.Invoke(wake,null);later.SetResult(true);
   Need(calls==1&&!valid(owner,current),"dispose-prevents-late-post-and-queued-notification");
  }
  return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
