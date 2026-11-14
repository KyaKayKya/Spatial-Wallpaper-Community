using System;using Newtonsoft.Json.Linq;
static class NativeHostLifecycleChecks {
 static void Need(bool ok,string why){if(!ok)throw new Exception(why);}
 static JObject Running(string attempt,int pid)=>new JObject{{"state","Running"},{"attempt",attempt},{"worker_pid",pid},{"worker_creation_filetime",123456789L}};
 static JObject Reply(string instance,string attempt,int pid,string state="Stopped"){
  var status=Running(attempt,pid);status["state"]=state;status["stop_evidence"]=new JObject{{"cleanup_completed",true},{"graceful_stop",true},{"forced_stop",false}};
  return new JObject{{"instances",new JObject()},{"recent",new JArray(new JObject{{"instance",instance},{"result",status}})}};
 }
 static void Reject(Action work,string why){bool failed=false;try{work();}catch(InvalidOperationException){failed=true;}Need(failed,why);}
 static int Main(){try{
  string instance=Guid.NewGuid().ToString("N"),attempt=Guid.NewGuid().ToString("N");var bound=NativeHostLifecycle.Bind(instance,Running(attempt,123));
  Need(NativeHostLifecycle.FindTerminal(Reply(instance,Guid.NewGuid().ToString("N"),123),bound)==null,"foreign-attempt-accepted");
  Need(NativeHostLifecycle.FindTerminal(Reply(Guid.NewGuid().ToString("N"),attempt,123),bound)==null,"foreign-instance-accepted");
  Reject(()=>NativeHostLifecycle.FindTerminal(Reply(instance,attempt,124),bound),"pid-mismatch-accepted");
  Reject(()=>NativeHostLifecycle.Bind(instance,Running(attempt,0)),"zero-pid-accepted");
  var reused=Reply(instance,attempt,123);reused["recent"][0]["result"]["worker_creation_filetime"]=123456790L;
  Reject(()=>NativeHostLifecycle.FindTerminal(reused,bound),"same-pid-reused-process-accepted");
  var unknown=Running(attempt,123);unknown.Remove("worker_creation_filetime");Reject(()=>NativeHostLifecycle.Bind(instance,unknown),"unknown-creation-accepted");
  NativeHostLifecycle.BindRetainedHandle(bound,123456789L);
  Reject(()=>NativeHostLifecycle.BindRetainedHandle(bound,123456790L),"retained-reused-pid-accepted");
  Reject(()=>NativeHostLifecycle.BindRetainedHandle(bound,0),"unknown-retained-creation-accepted");
  long now=0;int calls=0;JObject saved=null;
  var life=NativeHostLifecycle.Stop(bound,(method,args)=>{if(method=="Stop")return new JObject{{"ok",true}};calls++;return calls==1?new JObject{{"instances",new JObject{{instance,Running(attempt,123)}}},{"recent",new JArray()}}:Reply(instance,attempt,123);},()=>true,ms=>now+=ms,()=>now,value=>saved=(JObject)value.DeepClone(),100);
  Need(calls==2&&(bool)life["worker_kernel_exited"]&&(string)life["attempt"]==attempt,"did-not-wait-for-terminal");
  Need((string)saved["status"]=="observed-terminal","terminal-not-persisted");
  now=0;Reject(()=>NativeHostLifecycle.Stop(bound,(method,args)=>method=="Stop"?new JObject{{"ok",true}}:Reply(instance,attempt,123),()=>false,ms=>now+=ms,()=>now,value=>saved=(JObject)value.DeepClone(),100),"status-alone-counted-kernel-exit");
  Need((string)saved["status"]=="failed"&&(bool)saved["lifecycle"]["worker_kernel_exited"]==false,"failed-observation-lost");
  now=0;Reject(()=>NativeHostLifecycle.Stop(bound,(method,args)=>method=="Stop"?new JObject{{"ok",true}}:new JObject{{"instances",new JObject()},{"recent",new JArray()}},()=>true,ms=>now+=ms,()=>now,value=>saved=(JObject)value.DeepClone(),30),"ack-alone-counted-exit");
  Need(saved["lifecycle"].Type==JTokenType.Null,"timeout-fabricated-evidence");
  now=0;Reject(()=>NativeHostLifecycle.Stop(bound,(method,args)=>{if(method=="Stop")return new JObject{{"ok",true}};now=101;return Reply(instance,attempt,123);},()=>true,ms=>now+=ms,()=>now,value=>saved=(JObject)value.DeepClone(),100),"late-terminal-counted-before-deadline");
  var incomplete=Reply(instance,attempt,123,"CleanupFailed");incomplete["recent"][0]["result"]["stop_evidence"]["cleanup_completed"]=false;
  now=0;Reject(()=>NativeHostLifecycle.Stop(bound,(method,args)=>method=="Stop"?new JObject{{"ok",true}}:incomplete,()=>true,ms=>now+=ms,()=>now,value=>saved=(JObject)value.DeepClone(),100),"cleanup-failure-accepted");
  Need((bool)saved["lifecycle"]["cleanup_completed"]==false,"cleanup-failure-hidden");
  var primary=new InvalidOperationException("primary-stop-error");Exception observed=null;int saves=0;
  try{NativeHostLifecycle.Stop(bound,(method,args)=>{throw primary;},()=>true,ms=>{},()=>0,value=>{if(++saves>1)throw new System.IO.IOException("secondary-save-error");});}catch(Exception error){observed=error;}
  Need(object.ReferenceEquals(primary,observed)&&observed.Data.Contains("lifecycle-save-error"),"save-failure-replaced-primary");
  Console.WriteLine("PASS native lifecycle identity, terminal wait, kernel handle and failure preservation");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
