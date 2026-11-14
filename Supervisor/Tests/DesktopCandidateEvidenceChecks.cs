using System;using System.IO;using Newtonsoft.Json.Linq;
static class DesktopCandidateEvidenceChecks {
 static void Need(bool value,string why){if(!value)throw new Exception(why);Console.WriteLine("PASS "+why);}
 static int Main(){try{var log=new DesktopCandidateEvidence();int calls=0;
  log.Probe(470,804,()=>{calls++;return new JObject{{"present",true},{"classification",2},{"window",17},{"pid",19}};},()=>new JObject{{"sequence",1},{"phase",null}},()=>123,()=>456);
  var row=(JObject)log.Snapshot()["candidates"][0];Need(calls==1&&(int?)row["hit"]?["classification"]==2&&(long)row["foreground_before"]==456&&(long)row["machine_time_before"]==123,"raw-nonblank-candidate-retained");Need((long)row["query_returned_qpc"]>=(long)row["query_began_qpc"]&&(long)row["probe"]["sequence"]==1,"candidate-query-time-and-sequence");
  var original=new InvalidOperationException("probe-primary");Exception seen=null;try{log.Probe(1,2,()=>{throw original;},()=>{throw new IOException("metadata-fault");},()=>123,()=>456);}catch(Exception e){seen=e;}
  Need(ReferenceEquals(original,seen)&&log.Snapshot()["candidates"][1]["query_error"]!=null&&log.Snapshot()["candidates"][1]["metadata_error"]!=null,"probe-error-not-replaced-by-diagnostic-error");
  seen=null;try{try{throw original;}finally{log.Save("unused",original,(p,s)=>{throw new IOException("save-secondary");});}}catch(Exception e){seen=e;}
  Need(ReferenceEquals(original,seen)&&original.Data.Contains("candidate-evidence-save-error"),"save-failure-retains-primary");
  bool failed=false;try{log.Save("unused",null,(p,s)=>{throw new IOException("save-secondary");});}catch(IOException){failed=true;}Need(failed,"save-failure-on-success-is-explicit");
  for(int i=0;i<520;i++)log.Probe(i,0,()=>null,()=>new JObject(),()=>123,()=>456);
  log.Found=false;var bounded=log.Snapshot();Need((long)bounded["total"]==522&&(int)bounded["dropped"]==10&&((JArray)bounded["candidates"]).Count==512,"candidate-evidence-bound-explicit");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
