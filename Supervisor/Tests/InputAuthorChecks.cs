// Trusted synthetic source; production admission, launcher, LPAC Unity and converted author.
using System;using System.IO;using System.Text;using System.Diagnostics;using System.Threading;using System.Collections.Generic;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Windows;using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Capabilities;
static class InputAuthorChecks {
 static void Need(bool value,string code){if(!value)throw new Exception(code);}
 static WorkerImage Image(string root,string exe,string inventory,string field){var files=new Dictionary<string,string>();foreach(var p in ((JObject)JObject.Parse(File.ReadAllText(inventory))[field]).Properties())files.Add(p.Name,(string)p.Value);return new WorkerImage(root,exe,files);}
 static InputObservation Event(InputTarget target,long sequence,uint held,int wheel,bool click=false)=>new InputObservation(sequence,WindowsMachineClock.Milliseconds(),true,true,true,true,true,false,click,held,.25f,.75f,wheel,target.CaptureEpoch);
 static bool Stored(StorageBroker storage,BrokerSession session,string key){var r=storage.Read(session,key).GetAwaiter().GetResult();return r.Failure==StorageFailure.None&&r.Value!=null&&Encoding.UTF8.GetString(r.Value)=="observed";}
 static int Main(string[] args){try{
  var observations=new JArray();var timer=Stopwatch.StartNew();
  using(var station=new PrivateStation())using(var journal=new FaultJournal(Path.Combine(args[0],"journal")))using(var input=new InputBroker()){
   var storage=new StorageBroker(Path.Combine(args[0],"storage"));storage.Initialized.GetAwaiter().GetResult();try{
    var validator=new ValidatorLauncher(station,Image(args[3],"Validator.exe",Path.Combine(args[3],"build.json"),"files"),args[4]);
    var launcher=new WindowsWorkerLauncher(station,Image(Path.Combine(args[1],"player"),"Worker.exe",Path.Combine(args[1],"report.json"),"runtime_files"),args[4],()=>timer.ElapsedMilliseconds,640,360,admission:validator,persistence:storage,inputFeatures:input);
    string build=(string)JObject.Parse(File.ReadAllText(Path.Combine(args[2],"author.json")))["build"];
    var failed=journal.Begin(Guid.NewGuid(),build,Guid.NewGuid(),1,StartIntent.Manual);bool rolledBack=false;
    try{using(var worker=launcher.Start(new LaunchRequest(failed.Installation,build,failed.Instance,1,StartIntent.Manual,Path.Combine(args[2],"scene.spatial"),RuntimeLimits.ProcessBytes),failed,pid=>{throw new InvalidOperationException("fixture-created-failure");})){} }
    catch(InvalidOperationException e){rolledBack=e.Message=="fixture-created-failure";}
    Need(rolledBack&&station.ChildCount==0,"input-launch-rollback-child-leak");
    using(var first=input.Register(new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),true))using(var second=input.Register(new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),true)){}
    observations.Add(new JObject{{"created_callback_failure_rolled_back",true},{"both_input_slots_available",true}});
    foreach(bool preview in new[]{false,true}){
     var receipt=journal.Begin(Guid.NewGuid(),build,Guid.NewGuid(),1,StartIntent.Manual);var request=new LaunchRequest(receipt.Installation,build,receipt.Instance,1,StartIntent.Manual,Path.Combine(args[2],"scene.spatial"),RuntimeLimits.ProcessBytes,preview);
     InputTarget target;
     using(var worker=(WindowsManagedWorker)launcher.Start(request,receipt,pid=>journal.RecordProcess(receipt,pid))){
      target=worker.Input;Need(target!=null&&target.Matches(new WorkerIdentity(receipt.Attempt,receipt.Instance,1)),"launcher-input-target-missing");
      var wait=Stopwatch.StartNew();while(worker.Sequence==0&&worker.Sample().Alive&&wait.ElapsedMilliseconds<20000)Thread.Sleep(10);
      Need(worker.Sequence>0,"input-author-no-frame");long sequence=1;
      if(preview)target.Observe(Event(target,sequence++,1,0,true));else target.AcquireFocus(Event(target,sequence++,0,0,true));
      target.Observe(Event(target,sequence++,0,0));target.Observe(Event(target,sequence++,4,120));
      wait.Restart();while(!Stored(storage,worker.Session,"input-held")&&wait.ElapsedMilliseconds<5000){target.Observe(Event(target,sequence++,4,0));Thread.Sleep(10);}
      File.WriteAllBytes(Path.Combine(args[0],preview?"preview.log":"live.log"),worker.ReadLog());Need(Stored(storage,worker.Session,"input-held"),"author-input-seven-member-observation-failed");
      target.Observe(Event(target,sequence++,0,0));wait.Restart();while(!Stored(storage,worker.Session,"input-released")&&wait.ElapsedMilliseconds<5000){target.Observe(Event(target,sequence++,0,0));Thread.Sleep(10);}
      Need(Stored(storage,worker.Session,"input-released"),"author-input-release-edge-failed");
      target.Revoke();wait.Restart();while(!Stored(storage,worker.Session,"input-cleared")&&wait.ElapsedMilliseconds<5000)Thread.Sleep(10);
      Need(Stored(storage,worker.Session,"input-cleared"),"author-input-revocation-failed");
      observations.Add(new JObject{{"preview",preview},{"frames",worker.Sequence},{"revoke_to_storage_ms",wait.ElapsedMilliseconds}});
      journal.MarkStopping(receipt);worker.Kill();Need(target.Closed,"stopped-worker-input-target-open");target.Observe(Event(target,sequence++,1,0,true));Need(InputSnapshotCodec.U32(target.Snapshot(),4)==0,"stopped-input-revived");
     }
     journal.Complete(receipt);
    }
    Need(station.ChildCount==0,"input-author-child-leak");
   }finally{storage.Close();storage.Completion.GetAwaiter().GetResult();}
  }
  File.WriteAllText(Path.Combine(args[0],"results.json"),observations.ToString());Console.WriteLine("PASS converted input author in formal LPAC Worker: seven members, same-frame reads, revoke, live/preview, target cleanup; trusted fixture source only");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
