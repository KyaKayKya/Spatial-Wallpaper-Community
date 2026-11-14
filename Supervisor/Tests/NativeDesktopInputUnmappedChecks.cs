using System;using System.Diagnostics;using System.Reflection;using System.IO;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision.Interactive;using SpatialWallpaper.Sdk.Capabilities;
static partial class NativeDesktopInputHostChecks {
 static void CheckContinuousUnmapped(WindowsDesktopInput source,string output){
  Need(backgroundInjection&&source.Focused,"unmapped-starts-focused-with-owned-injector");Need((GetAsyncKeyState(65)&0x8000)==0,"unmapped-no-preheld-key");
  var fields=BindingFlags.Instance|BindingFlags.NonPublic;Func<string,long> read=name=>Convert.ToInt64(typeof(WindowsDesktopInput).GetField(name,fields).GetValue(source));
  var target=(IDesktopInputTarget)typeof(WindowsDesktopInput).GetField("target",fields).GetValue(source);var helper=(DesktopHitClient)typeof(WindowsDesktopInput).GetField("helper",fields).GetValue(source);
  long commits=read("heartbeatCommits"),queries=helper.LastQuerySequence,downs=read("unknownKeyDowns"),ups=read("unknownKeyUps");ulong epoch=target.CaptureEpoch;uint sent=0;long began=0,ended=0;
  unmapped=true;PersistObligations(); // Persist before the child can emit any down.
  try{sent=ProcessInject(new[]{new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=65}}},new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=65,Flags=2}}}},out began,out ended,"repeat-unmapped");}
  finally{ReleaseInjected();} // Run proved its child exited before this release.
  double duration=(ended-began)*1000.0/Stopwatch.Frequency;
  File.WriteAllText(Path.Combine(output,"unmapped-hook.json"),new JObject{{"injected_pairs",sent/2},{"hook_downs",read("unknownKeyDowns")-downs},{"hook_ups",read("unknownKeyUps")-ups},{"extra_release_ups",1}}.ToString());
  ulong after=0;string epochError=null;try{after=target.CaptureEpoch;}catch(ObjectDisposedException error){epochError=error.GetType().Name;}
  File.WriteAllText(Path.Combine(output,"continuous-unmapped.json"),new JObject{{"events",sent},{"duration_ms",duration},{"focused",source.Focused},{"heartbeat_commits",read("heartbeatCommits")-commits},{"event_helper_queries",helper.LastQuerySequence-queries},{"epoch_before",epoch},{"epoch_after",after},{"epoch_error",epochError},{"source",SourceDiagnostic(source,null)},{"held",read("keyHeld")},{"committed_held",read("committedHeld")},{"key_released",(GetAsyncKeyState(65)&0x8000)==0}}.ToString());
  Need(duration>=1500&&sent>=40&&sent%2==0,"unmapped-real-paired-events-span-multiple-leases");
  Need((GetAsyncKeyState(65)&0x8000)==0&&!unmapped,"unmapped-key-obligation-released");
  Need(read("unknownKeyDowns")-downs==sent/2&&read("unknownKeyUps")-ups==sent/2+1,"unmapped-production-hook-received-every-pair-and-final-release");
  Need(source.Focused&&epochError==null&&after==epoch&&read("heartbeatCommits")-commits>=10,"unmapped-real-events-preserve-fresh-heartbeats-and-epoch");
  Need(helper.LastQuerySequence==queries&&read("keyHeld")==0&&read("committedHeld")==0,"unmapped-events-do-not-query-or-become-author-held-state");
 }
}
