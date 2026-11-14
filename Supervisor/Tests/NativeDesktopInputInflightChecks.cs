using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Capabilities;
using SpatialWallpaper.Supervision.Interactive;
using SpatialWallpaper.Supervision.Broker;

static partial class NativeDesktopInputHostChecks {
 sealed class GrantCounter:IDesktopInputTarget {
  readonly IDesktopInputTarget inner;public int Grants;
  public GrantCounter(IDesktopInputTarget inner){this.inner=inner;}
  public ulong CaptureEpoch=>inner.CaptureEpoch;
  public void Observe(InputObservation value)=>inner.Observe(value);
  public void Revoke()=>inner.Revoke();
  public void AcquireFocus(InputObservation value){Grants++;inner.AcquireFocus(value);}
  public InputFocusConfirmation GetFocusConfirmation()=>inner.GetFocusConfirmation();
 }
 static GrantCounter inflightTarget;
 static IDesktopInputTarget TrackInflightTarget(IDesktopInputTarget target,string[] args){foreach(string arg in args)if(arg.StartsWith("--inflight-",StringComparison.Ordinal)||arg.StartsWith("--queued-",StringComparison.Ordinal))return inflightTarget=new GrantCounter(target);return target;}
 // Test-only delivery barrier. The actual helper has already produced these
 // bytes; this tests a pending query Task, not an in-progress COM call.
 sealed class FirstReplyBarrier:Stream {
  readonly Stream inner;
  readonly TaskCompletionSource<bool> ready=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  readonly TaskCompletionSource<bool> release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  int remaining=48;
  public FirstReplyBarrier(Stream inner){this.inner=inner;}
  public bool Ready=>ready.Task.IsCompleted;
  public void Release()=>release.TrySetResult(true);
  public override async Task<int> ReadAsync(byte[] buffer,int offset,int count,CancellationToken token){
   int read=await inner.ReadAsync(buffer,offset,count,token).ConfigureAwait(false);
   if(read>0&&remaining>0){remaining-=read;if(remaining==0){ready.TrySetResult(true);await release.Task.ConfigureAwait(false);}}
   return read;
  }
  // The original pipe remains owned by DesktopHitClient.input. Never close it here.
  protected override void Dispose(bool disposing){Release();base.Dispose(disposing);}
  public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;
  public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
  public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();public override void Flush()=>throw new NotSupportedException();
  public override long Seek(long o,SeekOrigin s)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();
 }
 static void CheckInflightReply(WindowsDesktopInput source,DesktopInputChannel channel,string output,string snapshot,string mode){
  const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
  Func<string,object> get=name=>typeof(WindowsDesktopInput).GetField(name,fields).GetValue(source);
  Need(source.Ready&&!source.Focused&&get("acquisition")==null,"inflight-clean-query-baseline");
  var helper=(DesktopHitClient)get("helper");var outputField=typeof(DesktopHitClient).GetField("output",fields);
  var barrier=new FirstReplyBarrier((Stream)outputField.GetValue(helper));outputField.SetValue(helper,barrier);
  Task<DesktopHitResult> query=null;bool observed=false,returned=false,interrupted=false,hookObserved=false;long clickAge=-1,completionClickAge=-1,resultAge=-1;uint sent=0;ulong before=channel.CaptureEpoch;int grantsBefore=inflightTarget.Grants;
  string reason=null;var evidence=new JObject{{"mode",mode},{"scope","real-helper-reply-buffered-query-task-pending-not-COM-pending"}};
  try{
   replyGateTick=()=>{
    if(!observed&&barrier.Ready){
     query=get("acquisition") as Task<DesktopHitResult>;
     if(!(bool)get("pending")||!(bool)get("completeClick")||query==null||query.IsCompleted)throw new Exception("inflight-required-state-not-reached");
     observed=true;
     if(mode!="control"){
      GetCursorPos(out var original);var events=new[]{new Event{Data=new Payload{Mouse=mode=="wheel"?new Mouse{Flags=0x800,Data=120}:new Mouse{X=8,Flags=0x2001}}}};
      uint expectedMessage=mode=="wheel"?0x20au:0x200u;if((uint)get("lastMouseMessage")==expectedMessage)throw new Exception("inflight-hook-observation-baseline-not-distinct");
      sent=SendInput(1,events,Marshal.SizeOf(typeof(Event)));
      var wait=System.Diagnostics.Stopwatch.StartNew();do{Application.DoEvents();hookObserved=(uint)get("lastMouseMessage")==expectedMessage;}while(!hookObserved&&wait.ElapsedMilliseconds<20);
      reason=(string)get("lastRevocation");interrupted=!(bool)get("pending")&&reason==(mode=="wheel"?"pending-wheel":"pending-pointer-left-click");
      if(mode=="move")SetCursorPos(original.X,original.Y);
     }
     clickAge=WindowsMachineClock.Milliseconds()-(long)get("clickTime");barrier.Release();
    }
    if(observed&&!returned&&query.IsCompleted){
     var raw=query.GetAwaiter().GetResult();long observedAt=WindowsMachineClock.Milliseconds();resultAge=raw==null?-1:observedAt-raw.ObservedAt;completionClickAge=observedAt-(long)get("clickTime");
     returned=true;evidence["classification"]=raw==null?JValue.CreateNull():new JValue(raw.Classification);replyGateTick=null;
    }
   };
   BlankClick();var wait=System.Diagnostics.Stopwatch.StartNew();while(!returned&&wait.ElapsedMilliseconds<500)Pump(1);
   evidence["observed_pending_complete_click"]=observed;evidence["query_returned"]=returned;evidence["result_age_at_ui_observation_ms"]=resultAge;evidence["click_age_at_ui_observation_ms"]=completionClickAge;evidence["click_age_at_release_ms"]=clickAge;evidence["hook_observed"]=hookObserved;evidence["interruption_observed"]=interrupted;evidence["sent"]=sent;evidence["reason"]=reason;evidence["helper_failure"]=helper.Failure;evidence["grant_calls"]=inflightTarget.Grants-grantsBefore;
   File.WriteAllText(Path.Combine(output,"inflight-reply.json"),evidence.ToString());
   Need(observed&&returned&&helper.Failure==null&&resultAge>=0&&resultAge<=50&&clickAge>=0&&clickAge<=250&&completionClickAge>=0&&completionClickAge<=250&&(int?)evidence["classification"]==1,"inflight-real-reply-succeeds-with-original-freshness");
   if(mode=="control"){
    var focus=System.Diagnostics.Stopwatch.StartNew();while(!source.Focused&&focus.ElapsedMilliseconds<300)Pump(1);
    Need(source.Focused&&channel.GetFocusConfirmation().Focused&&inflightTarget.Grants-grantsBefore==1,"inflight-barrier-control-grants-focus");window.Hide();Pump(30);window.Show();
   }else{
    Need(sent==1&&hookObserved&&interrupted,"inflight-native-interruption-observed-before-reply");Pump(60);
    Need(!source.Focused&&!channel.GetFocusConfirmation().Focused&&Revision(snapshot)==0&&inflightTarget.Grants==grantsBefore,"inflight-late-fresh-reply-cannot-restore-interrupted-click");
   }
   var baseline=System.Diagnostics.Stopwatch.StartNew();while(!source.Ready&&baseline.ElapsedMilliseconds<1000)Pump(1);
   Need(source.Ready&&!source.Focused&&channel.CaptureEpoch>before&&Revision(snapshot)==0,"inflight-new-click-baseline");evidence["grant_calls"]=inflightTarget.Grants-grantsBefore;File.WriteAllText(Path.Combine(output,"inflight-reply.json"),evidence.ToString());
  }finally{replyGateTick=null;barrier.Release();}
 }
}
