using System;using System.IO;using System.Collections.Generic;using System.Diagnostics;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Windows;using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Sdk.Hosting;
static class LaunchAdmissionChecks {
 static int Main(string[] args){try{
  var files=new Dictionary<string,string>();foreach(var p in ((JObject)JObject.Parse(File.ReadAllText(args[1]))["runtime_files"]).Properties())files.Add(p.Name,(string)p.Value);
  using(var station=new PrivateStation())using(var journal=new FaultJournal(Path.Combine(args[2],"journal"))){
   var clock=Stopwatch.StartNew();var launcher=new WindowsWorkerLauncher(station,new WorkerImage(args[0],"Worker.exe",files),Path.Combine(args[2],"attempts"),()=>clock.ElapsedMilliseconds,640,360);
   var receipt=journal.Begin(Guid.NewGuid(),new string('a',64),Guid.NewGuid(),1,StartIntent.Manual);int created=0;bool denied=false;
   try{using(var worker=launcher.Start(new LaunchRequest(receipt.Installation,receipt.BuildId,receipt.Instance,receipt.Generation,StartIntent.Manual,args[3],RuntimeLimits.ProcessBytes),receipt,pid=>created++)){} }
   catch(InvalidOperationException){denied=true;}
   if(!denied||created!=0||station.ChildCount!=0)throw new Exception("launcher-without-admission-services-created-renderer");
   var validatorFiles=new Dictionary<string,string>();foreach(var p in ((JObject)JObject.Parse(File.ReadAllText(Path.Combine(args[4],"build.json")))["files"]).Properties())validatorFiles.Add(p.Name,(string)p.Value);
   var validator=new ValidatorLauncher(station,new WorkerImage(args[4],"Validator.exe",validatorFiles),Path.Combine(args[2],"validation"));
   var storage=new StorageBroker(Path.Combine(args[2],"store"));storage.Initialized.GetAwaiter().GetResult();
   try{
    launcher=new WindowsWorkerLauncher(station,new WorkerImage(args[0],"Worker.exe",files),Path.Combine(args[2],"attempts"),()=>clock.ElapsedMilliseconds,640,360,admission:validator,persistence:storage);
    foreach(string path in new[]{args[3],args[5]}){
     denied=false;try{using(var worker=launcher.Start(new LaunchRequest(receipt.Installation,receipt.BuildId,receipt.Instance,receipt.Generation,StartIntent.Manual,path,RuntimeLimits.ProcessBytes),receipt,pid=>created++)){} }catch(AdmissionRejectedException){denied=true;}
     if(!denied||created!=0||station.ChildCount!=0)throw new Exception("bad-package-or-build-created-renderer");
    }
    var valid=journal.Begin(Guid.NewGuid(),args[6],Guid.NewGuid(),1,StartIntent.Manual);
    using(var worker=(WindowsManagedWorker)launcher.Start(new LaunchRequest(valid.Installation,valid.BuildId,valid.Instance,valid.Generation,StartIntent.Manual,args[5],RuntimeLimits.ProcessBytes),valid,pid=>created++)){
     var timeout=Stopwatch.StartNew();while(worker.Sequence==0&&worker.Sample().Alive&&timeout.ElapsedMilliseconds<20000)System.Threading.Thread.Sleep(10);
     if(worker.Sequence==0||worker.Sample().ProtocolFault)throw new Exception("validated-production-worker-did-not-render:"+System.Text.Encoding.UTF8.GetString(worker.ReadLog()));
     worker.Kill(); // Stop must close the trusted session immediately.
     bool closed=false;try{worker.Session.Demand("private-storage");}catch(InvalidOperationException){closed=true;}if(!closed)throw new Exception("stopped-worker-kept-capability-session");
    }
    if(created!=1||station.ChildCount!=0)throw new Exception("launch-transaction-child-leak");
   }finally{storage.Close();storage.Completion.GetAwaiter().GetResult();}
  }
  Console.WriteLine("PASS missing admission services, malformed package and wrong build create no renderer; admitted LPAC Unity renders; stop closes capability session; no child leak");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
