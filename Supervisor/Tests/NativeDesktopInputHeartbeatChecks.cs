using System;using System.Diagnostics;using System.Reflection;using System.IO;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision.Interactive;
static partial class NativeDesktopInputHostChecks {
 static void CheckNativeEvidenceExpiry(WindowsDesktopInput source,string output,bool focusGrant){
  var type=typeof(WindowsDesktopInput);var flags=BindingFlags.Instance|BindingFlags.NonPublic;Func<string,long> read=name=>Convert.ToInt64(type.GetField(name,flags).GetValue(source));
  Need(source.Ready&&source.Focused!=focusGrant,"native-evidence-expiry-initial-state");type.GetField("acceptanceEvidenceArm",flags).SetValue(source,1);
  if(focusGrant)BlankClick();else{Send(new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=32}}});Send(new Event{Type=1,Data=new Payload{Key=new Keyboard{Key=32,Flags=2}}});}
  Pump(100);File.WriteAllText(Path.Combine(output,"native-evidence-expiry.json"),new JObject{{"focus_grant",focusGrant},{"holds",read("acceptanceEvidenceHolds")},{"hit_age_ms",read("acceptanceEvidenceAge")},{"stale_calls",read("acceptanceEvidenceCalls")},{"focused",source.Focused}}.ToString());
  Need(read("acceptanceEvidenceHolds")==1&&read("acceptanceEvidenceAge")>50&&read("acceptanceEvidenceAge")<250,"native-evidence-aged-after-valid-checks-once");
  Need(read("acceptanceEvidenceCalls")==0,"native-evidence-expired-hit-never-authorizes-target");Need(!source.Focused,"native-evidence-expiry-revokes-focus");
  var ready=Stopwatch.StartNew();while(!source.Ready&&ready.ElapsedMilliseconds<1000)Pump(1);Need(source.Ready,"native-evidence-expiry-new-click-baseline");
  if(!focusGrant){BlankClick();var focus=Stopwatch.StartNew();while(!source.Focused&&focus.ElapsedMilliseconds<1000)Pump(1);Need(source.Focused,"native-evidence-new-click-restores-focus");}
 }
 static void CheckHeartbeatExpiry(WindowsDesktopInput source,string output,bool continuous){
  var type=typeof(WindowsDesktopInput);var flags=BindingFlags.Instance|BindingFlags.NonPublic;
  Func<string,long> read=name=>Convert.ToInt64(type.GetField(name,flags).GetValue(source));
  Need(source.Focused,"heartbeat-expiry-starts-focused");long commits=read("heartbeatCommits");type.GetField("acceptanceHeartbeatArm",flags).SetValue(source,continuous?2:1);
  var wait=Stopwatch.StartNew();while(read("acceptanceHeartbeatHolds")==0&&wait.ElapsedMilliseconds<3000)Pump(1);
  Need(read("acceptanceHeartbeatHolds")>=1,"heartbeat-expiry-injected-after-successful-query-and-window-validation");
  if(continuous){while(source.Focused&&wait.ElapsedMilliseconds<2000)Pump(1);type.GetField("acceptanceHeartbeatArm",flags).SetValue(source,0);}else Pump(120);
  File.WriteAllText(Path.Combine(output,"heartbeat-expiry.json"),new JObject{{"continuous",continuous},{"holds",read("acceptanceHeartbeatHolds")},{"old_hit_time",read("acceptanceHeartbeatHitTime")},{"delayed_age_ms",read("acceptanceHeartbeatAge")},{"stale_publishes",read("acceptanceHeartbeatStalePublishes")},{"sequence_before_delay",read("acceptanceHeartbeatSequence")},{"sequence_after",read("sequence")},{"heartbeat_commits_before",commits},{"heartbeat_commits_after",read("heartbeatCommits")},{"focused",source.Focused}}.ToString());
  Need(read("acceptanceHeartbeatAge")>50&&read("acceptanceHeartbeatAge")<250,"heartbeat-expiry-between-hit-limit-and-focus-lease");
  Need(read("acceptanceHeartbeatStalePublishes")==0,"heartbeat-expired-sample-never-published");
  if(!continuous)Need(source.Focused&&read("heartbeatCommits")>commits,"fresh-heartbeat-replaces-expired-sample-without-revoking-valid-lease");
  else{Need(read("acceptanceHeartbeatHolds")>=2&&!source.Focused,"continuous-expired-heartbeats-do-not-renew-host-lease");Pump(400);Need(!source.Focused,"late-fresh-work-cannot-reacquire-expired-focus");BlankClick();var focus=Stopwatch.StartNew();while(!source.Focused&&focus.ElapsedMilliseconds<1000)Pump(1);Need(source.Focused,"expired-lease-requires-new-real-click");}
 }
}
