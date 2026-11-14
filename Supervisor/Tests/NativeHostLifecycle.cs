using System;using Newtonsoft.Json.Linq;
// Trusted acceptance bridge. It copies Host observations; it never invents Worker stop timing.
static class NativeHostLifecycle {
 static void Need(bool ok,string why){if(!ok)throw new InvalidOperationException(why);}
 internal static JObject Bind(string instance,JToken running){
  Guid parsed;Need(Guid.TryParseExact(instance,"N",out parsed),"lifecycle-instance");
  Need((string)running?["state"]=="Running","lifecycle-not-running");
  string attempt=(string)running["attempt"];Need(Guid.TryParseExact(attempt,"N",out parsed),"lifecycle-attempt");
  Need(running["worker_pid"]?.Type==JTokenType.Integer&&(long)running["worker_pid"]>0&&(long)running["worker_pid"]<=int.MaxValue,"lifecycle-worker-pid");
  Need(running["worker_creation_filetime"]?.Type==JTokenType.Integer&&(long)running["worker_creation_filetime"]>0,"lifecycle-worker-creation");
  return new JObject{{"instance",instance},{"attempt",attempt},{"worker_pid",running["worker_pid"].DeepClone()},{"worker_creation_filetime",running["worker_creation_filetime"].DeepClone()}};
 }
 internal static void BindRetainedHandle(JObject binding,long creation){Need(creation>0&&creation==(long)binding["worker_creation_filetime"],"lifecycle-retained-creation-mismatch");binding["retained_worker_creation_filetime"]=creation;}
 internal static JObject FindTerminal(JObject response,JObject binding){
  JObject found=null;string instance=(string)binding["instance"],attempt=(string)binding["attempt"];
  Action<JToken> consider=status=>{
   if(status==null||(string)status["attempt"]!=attempt)return;
   Need(status["worker_pid"]?.Type==JTokenType.Integer&&(long)status["worker_pid"]==(long)binding["worker_pid"],"lifecycle-pid-mismatch");
   Need(status["worker_creation_filetime"]?.Type==JTokenType.Integer&&(long)status["worker_creation_filetime"]==(long)binding["worker_creation_filetime"],"lifecycle-creation-mismatch");
   string state=(string)status["state"];if(state!="Stopped"&&state!="Faulted"&&state!="CleanupFailed")return;
   Need(found==null,"lifecycle-duplicate-terminal");found=(JObject)status;
  };
  consider(response["instances"]?[instance]);
  if(response["recent"] is JArray recent)foreach(var item in recent)if((string)item["instance"]==instance)consider(item["result"]);
  return found;
 }
 internal static JObject Stop(JObject binding,Func<string,JObject,JObject> call,Func<bool> kernelExited,
     Action<int> sleep,Func<long> now,Action<JObject> save,long timeout=10000){
  var record=(JObject)binding.DeepClone();record["status"]="pending";record["lifecycle"]=null;save(record);
  try{
   Need(binding["retained_worker_creation_filetime"]?.Type==JTokenType.Integer&&(long)binding["retained_worker_creation_filetime"]==(long)binding["worker_creation_filetime"],"lifecycle-retained-identity-unproven");
   long began=now();
   Need((bool?)call("Stop",new JObject{{"instance",binding["instance"].DeepClone()}})["ok"]==true,"lifecycle-stop-ack");
   record["stop_acknowledged"]=true;save(record);
   while(now()-began<=timeout){
    var response=call("Status",new JObject());Need(now()-began<=timeout,"lifecycle-terminal-deadline");var terminal=FindTerminal(response,binding);
    if(terminal!=null){
     Need(terminal["stop_evidence"] is JObject,"lifecycle-missing-stop-evidence");
     var life=(JObject)terminal["stop_evidence"].DeepClone();life["attempt"]=binding["attempt"].DeepClone();life["worker_pid"]=binding["worker_pid"].DeepClone();life["worker_started"]=true;life["worker_kernel_exited"]=kernelExited();
     life["worker_creation_filetime"]=binding["worker_creation_filetime"].DeepClone();life["retained_worker_creation_filetime"]=binding["retained_worker_creation_filetime"].DeepClone();
     record["terminal_state"]=terminal["state"].DeepClone();record["lifecycle"]=life;save(record);
     Need((bool)life["worker_kernel_exited"],"lifecycle-retained-kernel-handle-not-exited");
     Need((bool?)life["cleanup_completed"]==true,"lifecycle-worker-cleanup-unproven");
     record["status"]="observed-terminal";save(record);return life;
    }
    sleep(10);
   }
   throw new InvalidOperationException("lifecycle-terminal-deadline");
  }catch(Exception error){record["status"]="failed";record["bridge_error"]=error.ToString();try{save(record);}catch(Exception secondary){error.Data["lifecycle-save-error"]=secondary.ToString();}throw;}
 }
}
