using System;using System.IO;using System.Reflection;using System.Threading;using System.Runtime.InteropServices;using SpatialWallpaper.Supervision.Interactive;using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Sdk.Capabilities;using Newtonsoft.Json.Linq;
static partial class NativeDesktopInputHostChecks {
 delegate IntPtr ActivationSubclass(IntPtr hwnd,uint message,IntPtr w,IntPtr l,UIntPtr id,UIntPtr data);
 [DllImport("comctl32.dll",EntryPoint="SetWindowSubclass")]static extern bool ActivationSetSubclass(IntPtr hwnd,ActivationSubclass callback,UIntPtr id,UIntPtr data);
 [DllImport("comctl32.dll",EntryPoint="RemoveWindowSubclass")]static extern bool ActivationRemoveSubclass(IntPtr hwnd,ActivationSubclass callback,UIntPtr id);
 [DllImport("comctl32.dll",EntryPoint="DefSubclassProc")]static extern IntPtr ActivationNext(IntPtr hwnd,uint message,IntPtr w,IntPtr l);
 [DllImport("user32.dll",EntryPoint="SetFocus")]static extern IntPtr ActivationNativeFocus(IntPtr hwnd);
 static int activationCalls;
  static Action activationValidationProbe;
  static int activationValidationCalls;static bool activationValidationValid;
  internal static bool ActivationAfterValidation(bool valid){if(activationValidationProbe!=null){activationValidationCalls++;activationValidationValid=valid;activationValidationProbe();}return valid;}
 internal static bool ActivationSetForeground(IntPtr hwnd){activationCalls++;return SetForegroundWindow(hwnd);}
 internal static IntPtr ActivationSetFocus(IntPtr hwnd){activationCalls++;return ActivationNativeFocus(hwnd);}
  static void CheckExpiredActivation(WindowsDesktopInput source,DesktopInputChannel channel,string output,string snapshot,bool afterValidation){
   activationValidationCalls=0;activationValidationValid=false;var fields=BindingFlags.Instance|BindingFlags.NonPublic;var type=typeof(WindowsDesktopInput);var owner=(OwnedInputThread)type.GetField("owner",fields).GetValue(source);
  var report=new JObject{{"scope","real click and fresh hit; fixture ages original click at UI activation boundary; owner held to isolate UI precondition"}};int callsBefore=0,callsAfter=0,visited=0;Exception failure=null;
   var entered=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);var release=new System.Threading.Tasks.TaskCompletionSource<bool>(System.Threading.Tasks.TaskCreationOptions.RunContinuationsAsynchronously);{
   ActivationSubclass callback=(hwnd,message,w,l,id,data)=>{
    if(message!=0x8000+319||visited++!=0)return ActivationNext(hwnd,message,w,l);
    try{
     object request=type.GetField("focusRequest",fields).GetValue(source);Need(request!=null,"expiry-real-focus-request-queued");
      owner.Post(()=>{entered.TrySetResult(true);if(!release.Task.Wait(1000))throw new TimeoutException("activation-probe-owner-release");});Need(entered.Task.Wait(100),"expiry-owner-held-before-ui-validation");
      Action expire=()=>{long stale=WindowsMachineClock.Milliseconds()-251;type.GetField("clickTime",fields).SetValue(source,stale);request.GetType().GetField("Time",fields)?.SetValue(request,stale);report["click_age_ms"]=WindowsMachineClock.Milliseconds()-stale;};if(afterValidation)activationValidationProbe=expire;else expire();
     var hit=(DesktopHitResult)request.GetType().GetField("Hit",fields).GetValue(request);Need(hit.IsFresh(WindowsMachineClock.Milliseconds()),"expiry-hit-remains-fresh");Need(GetForegroundWindow()!=hwnd,"expiry-receiver-not-already-foreground");
      report["after_validation"]=afterValidation;report["hit_age_ms"]=WindowsMachineClock.Milliseconds()-hit.ObservedAt;callsBefore=activationCalls;
      IntPtr result=ActivationNext(hwnd,message,w,l);callsAfter=activationCalls;report["native_activation_calls"]=callsAfter-callsBefore;report["receiver_became_foreground"]=GetForegroundWindow()==hwnd;report["validation_probe_calls"]=activationValidationCalls;report["validation_probe_valid"]=activationValidationValid;if(afterValidation)Need(activationValidationCalls==1&&activationValidationValid,"expiry-after-valid-checks-probe-executed-once");
     return result;
     }catch(Exception error){failure=error;return IntPtr.Zero;}finally{activationValidationProbe=null;release.TrySetResult(true);}
   };
   Need(ActivationSetSubclass(window.Handle,callback,new UIntPtr(0x53574558),UIntPtr.Zero),"expiry-probe-installed");
   try{BlankClick();var wait=System.Diagnostics.Stopwatch.StartNew();while(visited==0&&wait.ElapsedMilliseconds<1000)Pump(1);Need(visited==1,"expiry-ui-activation-message-observed-once");if(failure!=null)throw failure;Need(callsAfter==callsBefore,"expired-click-never-calls-native-activation");Pump(100);Need(!source.Focused&&!channel.GetFocusConfirmation().Focused&&Revision(snapshot)==0,"expired-click-never-reaches-author");}
    finally{activationValidationProbe=null;release.TrySetResult(true);Need(ActivationRemoveSubclass(window.Handle,callback,new UIntPtr(0x53574558)),"expiry-probe-removed");File.WriteAllText(Path.Combine(output,"expired-activation.json"),report.ToString());GC.KeepAlive(callback);}
  }
 }
}
