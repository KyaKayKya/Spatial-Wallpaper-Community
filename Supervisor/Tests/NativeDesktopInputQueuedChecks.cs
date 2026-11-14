using System;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Capabilities;
using SpatialWallpaper.Supervision.Broker;
using SpatialWallpaper.Supervision.Interactive;

static partial class NativeDesktopInputHostChecks {
 sealed class OneQueuedMessage:IMessageFilter {
  readonly IntPtr receiver;readonly Action<Message> action;public bool Captured;public Exception Failure;
  public OneQueuedMessage(IntPtr receiver,Action<Message> action){this.receiver=receiver;this.action=action;}
  public bool PreFilterMessage(ref Message message){
   if(!Captured&&message.HWnd==receiver&&message.Msg==0x8000+318){Captured=true;try{action(message);}catch(Exception error){Failure=error;}}
   return false; // Preserve this exact native message, including its original tokens.
  }
 }
 static void CheckQueuedNotification(WindowsDesktopInput source,DesktopInputChannel channel,string output,string snapshot,string mode){
  const BindingFlags fields=BindingFlags.Instance|BindingFlags.NonPublic;
  Func<string,object> get=name=>typeof(WindowsDesktopInput).GetField(name,fields).GetValue(source);
  var wakeup=(AcquisitionWakeup)get("wakeup");var helper=(DesktopHitClient)get("helper");
  Need(source.Ready&&!source.Focused&&get("acquisition")==null,"queued-clean-query-baseline");
  int grantsBefore=inflightTarget.Grants;ulong epochBefore=channel.CaptureEpoch;
  long owner=0,request=0,originalClickTime=0,resultTime=0;bool hookSeen=false,revoked=false;uint sent=0;
  var evidence=new JObject{{"mode",mode},{"scope","actual-queued-completion-message-same-source-owner"}};
  var filter=new OneQueuedMessage(window.Handle,message=>{
   owner=message.WParam.ToInt64();request=message.LParam.ToInt64();
   var query=get("acquisition") as Task<DesktopHitResult>;
   if(!wakeup.Matches(owner,request)||!(bool)get("pending")||!(bool)get("completeClick")||query==null||!query.IsCompleted)throw new Exception("queued-notification-required-state-not-reached");
   var hit=query.GetAwaiter().GetResult();if(hit==null||hit.Classification!=1)throw new Exception("queued-notification-no-real-blank-reply");
   originalClickTime=(long)get("clickTime");resultTime=hit.ObservedAt;
   if(mode=="revoke"){
    if((uint)get("lastMouseMessage")==0x20a)throw new Exception("queued-hook-baseline-not-distinct");
    var events=new[]{new Event{Data=new Payload{Mouse=new Mouse{Flags=0x800,Data=120}}}};
    sent=SendInput(1,events,Marshal.SizeOf(typeof(Event)));
    var clock=System.Diagnostics.Stopwatch.StartNew();do{Application.DoEvents();hookSeen=(uint)get("lastMouseMessage")==0x20a;}while(!hookSeen&&clock.ElapsedMilliseconds<20);
    revoked=!(bool)get("pending")&&(string)get("lastRevocation")=="pending-wheel";
   }
  });
  Application.AddMessageFilter(filter);
  try{
   BlankClick();var clock=System.Diagnostics.Stopwatch.StartNew();while(!filter.Captured&&clock.ElapsedMilliseconds<300)Pump(1);
   if(filter.Failure!=null)throw new InvalidOperationException("queued-filter-failed",filter.Failure);
   long decision=(long)get("acceptanceDecisionAt");int decisions=(int)get("acceptanceDecisionCount"),accepted=(int)get("acceptanceAccepted"),rejected=(int)get("acceptanceRejected");
   evidence["captured"]=filter.Captured;evidence["owner"]=owner;evidence["request"]=request;evidence["hwnd"]=window.Handle.ToInt64();evidence["decision_owner"]=(long)get("acceptanceOwner");evidence["decision_request"]=(long)get("acceptanceRequest");evidence["decision_hwnd"]=(long)get("acceptanceWindow");evidence["decision_count"]=decisions;evidence["accepted"]=accepted;evidence["rejected"]=rejected;evidence["result_age_at_dispatch_ms"]=decision-resultTime;evidence["click_age_at_dispatch_ms"]=decision-originalClickTime;evidence["sent"]=sent;evidence["hook_observed"]=hookSeen;evidence["revoked"]=revoked;evidence["helper_failure"]=helper.Failure;evidence["grant_calls"]=inflightTarget.Grants-grantsBefore;
   File.WriteAllText(Path.Combine(output,"queued-notification.json"),evidence.ToString());
   Need(filter.Captured&&decisions==1&&owner==(long)get("acceptanceOwner")&&request==(long)get("acceptanceRequest")&&window.Handle.ToInt64()==(long)get("acceptanceWindow"),"queued-exact-message-reached-native-dispatch-once");
   Need(helper.Failure==null&&decision>=resultTime&&decision-resultTime<=50&&decision>=originalClickTime&&decision-originalClickTime<=250,"queued-dispatch-keeps-original-query-and-click-freshness");
   if(mode=="control"){
    Need(accepted==1&&rejected==0,"queued-control-notification-matched");var focus=System.Diagnostics.Stopwatch.StartNew();while(!source.Focused&&focus.ElapsedMilliseconds<300)Pump(1);
    Need(source.Focused&&channel.GetFocusConfirmation().Focused&&inflightTarget.Grants-grantsBefore==1,"queued-control-native-and-Host-focus");window.Hide();Pump(30);window.Show();
   }else{
    Need(sent==1&&hookSeen&&revoked,"queued-real-wheel-revoked-before-dispatch");
    Need(accepted==0&&rejected==1,"queued-revoked-message-rejected-by-token-gate");Pump(60);
    Need(inflightTarget.Grants==grantsBefore&&!source.Focused&&!channel.GetFocusConfirmation().Focused&&Revision(snapshot)==0,"queued-rejected-message-never-attempts-focus");
   }
   var baseline=System.Diagnostics.Stopwatch.StartNew();while(!source.Ready&&baseline.ElapsedMilliseconds<1000)Pump(1);
   Need(source.Ready&&!source.Focused&&channel.CaptureEpoch>epochBefore&&Revision(snapshot)==0,"queued-fresh-click-baseline");
  }finally{Application.RemoveMessageFilter(filter);}
 }
}
