using System;using System.IO;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Windows;
static class UnityAttackChecks{
 static T Await<T>(Task<T> task){if(!task.Wait(5000))throw new Exception("journal-actor-timeout");return task.GetAwaiter().GetResult();}
 static int Main(string[] args){try{
  string healthyRoot=args[0],badRoot=args[1],output=args[2],healthyBuild=args[3];var cases=JArray.Parse(File.ReadAllText(args[4]));var clock=Stopwatch.StartNew();var results=new JArray();Guid install=Guid.NewGuid();var journal=new JournalActor(Path.Combine(output,"journal"),32);
  try{using(var station=new PrivateStation()){
   UnityIsolationChecks.Running healthy=null;
   try{
    healthy=new UnityIsolationChecks.Running(station,healthyRoot,"continuous",healthyBuild,install,journal,clock);
    while(healthy.Count<5&&clock.ElapsedMilliseconds<16000){if(healthy.Poll()==GuardAction.Kill)throw new Exception("healthy-start-failed:"+healthy.Reason);Thread.Sleep(10);}
    if(healthy.Count<5)throw new Exception("healthy-missing-real-frames");
    foreach(JObject test in cases){
     string name=(string)test["name"],build=(string)test["build_id"];File.Copy((string)test["package"],Path.Combine(badRoot,"scene.spatial"),true);UnityIsolationChecks.Running bad=null;
     try{
      long began=clock.ElapsedMilliseconds;bad=new UnityIsolationChecks.Running(station,badRoot,"continuous",build,install,journal,clock);
      while(clock.ElapsedMilliseconds-began<20000&&bad.Poll()!=GuardAction.Kill&&bad.Child.Wait(0)==258){if(healthy.Poll()==GuardAction.Kill)throw new Exception("healthy-fault:"+healthy.Reason);Thread.Sleep(10);}
      if(bad.Child.Wait(1000)!=0)throw new Exception("attack-did-not-stop-cooperatively:"+name);
      if(bad.Child.ExitCode!=(uint)(int)test["exit"])throw new Exception("wrong-attack-exit:"+name+":"+bad.Child.ExitCode);
      string log=bad.DrainLog();
      if(!log.Contains((string)test["required_log"]))throw new Exception("missing-attack-diagnostic:"+name);
      bool before=(bool)test["before_callbacks"];
      if(before){
       bool admitted=(bool)test["structural_admitted"];
       if(log.Contains("SDK_D_NATIVE_HOST")||log.Contains("SDK_D_RUNNING")||log.Contains("SDK_D_ADMITTED")!=admitted)throw new Exception("invalid-or-unbound-package-entered-native-host:"+name);
      }
      if(!before&&!log.Contains("SDK_D_RUNNING"))throw new Exception("valid-attack-never-ran:"+name);
      Await(journal.Fail(bad.Receipt,before?FaultReason.Admission:FaultReason.ScriptFault));
      bool denied=false;try{Await(journal.Begin(install,build,Guid.NewGuid(),2,StartIntent.Resume));}catch(AggregateException error){denied=error.GetBaseException().Message=="build-quarantined";}if(!denied)throw new Exception("attack-auto-resumed");
      int atExit=healthy.Count;long until=clock.ElapsedMilliseconds+500;
      while(clock.ElapsedMilliseconds<until){if(healthy.Poll()==GuardAction.Kill)throw new Exception("healthy-fault-after-attack:"+healthy.Reason);Thread.Sleep(10);}
      if(healthy.Count<atExit+3||healthy.Child.Wait(0)!=258)throw new Exception("attack-harmed-other-Unity-instance");
      results.Add(new JObject{{"name",name},{"build_id",build},{"exit_code",bad.Child.ExitCode},{"cooperative_exit",true},{"before_callbacks",before},{"healthy_frames_after_exit",healthy.Count-atExit},{"elapsed_ms",clock.ElapsedMilliseconds-began},{"automatic_resume_denied",true}});
      Console.WriteLine("PASS Unity attack "+name+", healthy frames after exit="+(healthy.Count-atExit));
     }finally{bad?.Dispose();bad?.Save(Path.Combine(output,name+".log"));}
    }
    Await(journal.MarkStopping(healthy.Receipt));healthy.Child.Kill(0);if(healthy.Child.Wait(1000)!=0)throw new Exception("healthy-stop-timeout");Await(journal.Complete(healthy.Receipt));
   }finally{healthy?.Dispose();healthy?.Save(Path.Combine(output,"healthy.log"));}
  }}finally{journal.Close();journal.Completion.Wait(5000);}
  File.WriteAllText(Path.Combine(output,"results.json"),results.ToString());return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
