using System;using System.IO;using System.Diagnostics;using Newtonsoft.Json.Linq;
static class InjectorPreflightChecks {
 static void Need(bool ok,string name){if(!ok)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static int Main(string[] args){try{
  string root=args[0];int calls=0,saves=0;
  OwnedInputInjector.CursorRead good=(out int x,out int y)=>{calls++;x=10;y=20;return true;};
  Need(OwnedInputInjector.Preflight(5,10,20,"send","unused",()=>5,good,(p,s)=>saves++)&&calls==1&&saves==0,"accepted-input-has-original-checks-and-no-diagnostic-write");
  foreach(string mode in new[]{"foreground","cursor","cursor-failed"}){
   string path=Path.Combine(root,mode+".json");calls=0;
   OwnedInputInjector.CursorRead read=(out int x,out int y)=>{calls++;x=10;y=20;return mode!="cursor-failed";};
   bool result=OwnedInputInjector.Preflight(5,mode=="cursor"?11:10,20,"send",path,()=>mode=="foreground"?6:5,read);
   Need(!result&&calls==1&&File.Exists(path),mode+"-rejects-and-saves");var record=JObject.Parse(File.ReadAllText(path));
   Need((int)record["exit_code"]==3&&(long)record["expected_foreground"]==5&&(long)record["actual_foreground"]==(mode=="foreground"?6:5),mode+"-original-rejection-evidence");
   Need((bool)record["cursor_succeeded"]==(mode!="cursor-failed")&&(string)record["cursor_phase"]==(mode=="foreground"?"diagnostic-after-rejection":"validation"),mode+"-cursor-observation-distinguished");
   Need((long)record["rejected_qpc"]>=(long)record["check_began_qpc"]&&(long)record["cursor_observed_qpc"]>=(long)record["check_began_qpc"]&&(mode=="foreground"?(long)record["cursor_observed_qpc"]>=(long)record["rejected_qpc"]:(long)record["cursor_observed_qpc"]<=(long)record["rejected_qpc"])&&(long)record["qpc_frequency"]==Stopwatch.Frequency&&(long)record["machine_time_ms"]>0,mode+"-clock-anchors");
   using(var self=Process.GetCurrentProcess())Need((int)record["child_pid"]==self.Id&&(long)record["child_creation_filetime"]==self.StartTime.ToUniversalTime().ToFileTimeUtc(),mode+"-actual-child-process-identity");
   Need(mode!="cursor-failed"||record["actual_x"].Type==JTokenType.Null&&record["actual_y"].Type==JTokenType.Null,"failed-cursor-does-not-invent-zero-position");
  }
  Need(!OwnedInputInjector.Preflight(5,10,20,"send","unused",()=>6,good,(p,s)=>{throw new IOException("controlled-write-failure");}),"write-failure-keeps-rejection");
  string existing=Path.Combine(root,"foreground.json"),before=File.ReadAllText(existing);
  Need(!OwnedInputInjector.Preflight(5,10,20,"send",existing,()=>6,good)&&File.ReadAllText(existing)==before,"diagnostic-collision-keeps-first-file-and-rejection");
  Need(!OwnedInputInjector.Preflight(5,10,20,"send","unused",()=>6,(out int x,out int y)=>{x=y=0;throw new IOException();}),"diagnostic-cursor-exception-keeps-foreground-rejection");
  return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
