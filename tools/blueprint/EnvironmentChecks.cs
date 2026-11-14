using System;
using System.Reflection;
static class EnvironmentChecks {
 static int passed,failed;
 static Type T(string name)=>Assembly.GetExecutingAssembly().GetType("SpatialWallpaper.Blueprint."+name)??throw new Exception(name+" is not implemented");
 static dynamic Settings()=>Activator.CreateInstance(T("BlueprintSettings"));
 static dynamic Clock()=>Activator.CreateInstance(T("BlueprintClock"));
 static void Check(string n,Action action){try{action();passed++;Console.WriteLine("PASS "+n);}catch(Exception e){failed++;Console.WriteLine("FAIL "+n+": "+e.GetBaseException().Message);}}
 static void Near(double a,double b){if(Math.Abs(a-b)>.00001)throw new Exception("expected "+b+", got "+a);}
 static void Reject(Action action){try{action();}catch(Exception e){if(e.GetBaseException().Message.Contains("setting"))return;throw;}throw new Exception("invalid setting accepted");}
 static DateTimeOffset Now(int hour,int minute=0)=>new DateTimeOffset(2026,11,10,hour,minute,0,TimeSpan.FromHours(8));
 static int Main(){
  Check("default-settings-valid",()=>{dynamic s=Settings();s.Validate();});
  Check("default-keeps-source-only-without-template-landscape",()=>{dynamic s=Settings();if((string)s.Terrain!="none")throw new Exception("new blueprints still add an unrelated landscape");s.Validate();});
  Check("source-only-roundtrips-and-old-terrain-still-loads",()=>{dynamic s=Settings();s.Terrain="none";dynamic copy=T("BlueprintSettings").GetMethod("ReadJson").Invoke(null,new object[]{(string)s.ToJson()});if((string)copy.Terrain!="none")throw new Exception("source-only mode lost");foreach(string old in new[]{"grass","waterfront","island","snow"}){s.Terrain=old;s.Validate();}});
  Check("fixed-hour-independent-of-system-clock",()=>{dynamic s=Settings(),c=Clock();s.Hour=17.5f;Near((float)c.Sample(s,Now(2),100f,false),17.5);Near((float)c.Sample(s,Now(8),100f,false),17.5);});
  Check("cycle-uses-active-time",()=>{dynamic s=Settings(),c=Clock();s.TimeMode="cycle";s.Hour=6f;s.CycleMinutes=1f;Near((float)c.Sample(s,Now(1),0f,false),6);Near((float)c.Sample(s,Now(1),2.5f,false),7);});
  Check("cycle-pause-does-not-catch-up",()=>{dynamic s=Settings(),c=Clock();s.TimeMode="cycle";s.Hour=6f;s.CycleMinutes=1f;c.Sample(s,Now(1),0f,false);Near((float)c.Sample(s,Now(5),100f,true),6);Near((float)c.Sample(s,Now(6),2.5f,false),7);});
  Check("cycle-phase-restores",()=>{dynamic s=Settings(),c=Clock();s.TimeMode="cycle";s.Hour=6f;c.RestoreCycle(22.25f);Near((float)c.Sample(s,Now(1),0f,false),22.25);});
  Check("system-hour-offset",()=>{dynamic s=Settings(),c=Clock();s.TimeMode="system";s.OffsetHours=-1f;Near((float)c.Sample(s,Now(18,45),0f,false),17.75);});
  Check("system-midnight-offset-wraps",()=>{dynamic s=Settings(),c=Clock();s.TimeMode="system";s.OffsetHours=2f;Near((float)c.Sample(s,Now(23,30),0f,false),1.5);});
  Check("system-pause-holds-last-visible-time",()=>{dynamic s=Settings(),c=Clock();s.TimeMode="system";c.Sample(s,Now(10),0f,false);Near((float)c.Sample(s,Now(15),100f,true),10);});
  Check("system-resume-takes-short-path-across-midnight",()=>{dynamic s=Settings(),c=Clock();s.TimeMode="system";c.Sample(s,Now(23,30),0f,false);c.Sample(s,Now(0,30),60f,true);float hour=c.Sample(s,Now(0,30),.1f,false);if(hour<23.5f||hour>23.6f)throw new Exception("resume jumped or went backwards: "+hour);});
  Check("clock-switch-to-fixed-is-explicit",()=>{dynamic s=Settings(),c=Clock();s.TimeMode="system";c.Sample(s,Now(8),0f,false);s.TimeMode="fixed";s.Hour=19f;Near((float)c.Sample(s,Now(8),.1f,false),19);});
  Check("nan-pose-refused",()=>{dynamic s=Settings();s.Scale=float.NaN;Reject(()=>s.Validate());});
  Check("non-uniform-pose-not-exposed",()=>{var t=T("BlueprintSettings");if(t.GetField("ScaleX")!=null||t.GetField("ScaleY")!=null||t.GetField("ScaleZ")!=null)throw new Exception("non-uniform scale exposed");});
  Check("unknown-weather-refused",()=>{dynamic s=Settings();s.Weather="hurricane";Reject(()=>s.Validate());});
  Check("invalid-cycle-refused",()=>{dynamic s=Settings();s.CycleMinutes=0f;Reject(()=>s.Validate());});
  Check("all-supported-environment-presets-validate",()=>{foreach(string w in new[]{"clear","cloudy","rain","snow"})foreach(string t in new[]{"grass","waterfront","island","snow"}){dynamic s=Settings();s.Weather=w;s.Terrain=t;s.Validate();}});
  Console.WriteLine("Blueprint environment: "+passed+" passed, "+failed+" failed");return failed==0?0:1;
 }
}
