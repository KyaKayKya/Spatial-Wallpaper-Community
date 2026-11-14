// Original MIT driver. The content Worker executes only the converted C# package.
using System;using System.IO;using System.Text;using System.Diagnostics;using System.Threading;using System.Collections.Generic;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Windows;using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Sdk.Hosting;
static class StorageAuthorChecks {
 static void Need(bool value,string code){if(!value)throw new Exception(code);}
 static WorkerImage Image(string root,string executable,string inventory,string field){var files=new Dictionary<string,string>();foreach(var item in ((JObject)JObject.Parse(File.ReadAllText(inventory))[field]).Properties())files.Add(item.Name,(string)item.Value);return new WorkerImage(root,executable,files);}
 static int Main(string[] args){try{
  string output=args[0];var clock=Stopwatch.StartNew();Guid installation=Guid.NewGuid();var observations=new JArray();
  using(var station=new PrivateStation())using(var journal=new FaultJournal(Path.Combine(output,"journal"))){
   var storage=new StorageBroker(Path.Combine(output,"storage"));storage.Initialized.GetAwaiter().GetResult();
   try{
    var validator=new ValidatorLauncher(station,Image(args[3],"Validator.exe",Path.Combine(args[3],"build.json"),"files"),args[4]);
    var launcher=new WindowsWorkerLauncher(station,Image(Path.Combine(args[1],"player"),"Worker.exe",Path.Combine(args[1],"report.json"),"runtime_files"),args[4],()=>clock.ElapsedMilliseconds,640,360,admission:validator,persistence:storage);
    string build=(string)JObject.Parse(File.ReadAllText(Path.Combine(args[2],"author.json")))["build"];
    foreach(bool preview in new[]{false,true}){
     var receipt=journal.Begin(installation,build,Guid.NewGuid(),1,StartIntent.Manual);var request=new LaunchRequest(installation,build,receipt.Instance,1,StartIntent.Manual,Path.Combine(args[2],"scene.spatial"),RuntimeLimits.ProcessBytes,preview);
     using(var worker=(WindowsManagedWorker)launcher.Start(request,receipt,pid=>journal.RecordProcess(receipt,pid))){
      var duration=Stopwatch.StartNew();StorageResult value=null;
      while(duration.ElapsedMilliseconds<20000){
       var sample=worker.Sample();if(!sample.Alive||sample.ProtocolFault)break;
       value=storage.Read(worker.Session,"beta").GetAwaiter().GetResult();if(value.Failure==StorageFailure.None&&value.Value!=null&&worker.Sequence>0)break;Thread.Sleep(20);
      }
      File.WriteAllBytes(Path.Combine(output,preview?"preview.log":"live.log"),worker.ReadLog());
      Need(value!=null&&value.Failure==StorageFailure.None&&value.Version==2&&value.Value!=null&&Encoding.UTF8.GetString(value.Value)=="author-written"&&worker.Sequence>0,"CSharp-Worker-storage-roundtrip-failed");
      var alpha=storage.Read(worker.Session,"alpha").GetAwaiter().GetResult();Need(Encoding.UTF8.GetString(alpha.Value)=="author-written","CSharp-author-write-failed");
      observations.Add(new JObject{{"preview",preview},{"version",value.Version},{"value",Encoding.UTF8.GetString(value.Value)},{"frames",worker.Sequence},{"roundtrip_ms",duration.ElapsedMilliseconds}});
      journal.MarkStopping(receipt);worker.Kill();bool closed=false;try{worker.Session.Demand("private-storage");}catch(InvalidOperationException){closed=true;}Need(closed,"stopped-author-session-not-closed");
     }
     journal.Complete(receipt);
    }
    var live=new BrokerSession(journal.Begin(installation,build,Guid.NewGuid(),2,StartIntent.Manual),false,new[]{"private-storage"});
    Need(storage.Read(live,"beta").GetAwaiter().GetResult().Version==2,"preview-contaminated-live-save");
    var foreign=new BrokerSession(journal.Begin(Guid.NewGuid(),build,Guid.NewGuid(),1,StartIntent.Manual),false,new[]{"private-storage"});Need(storage.Read(foreign,"beta").GetAwaiter().GetResult().Value==null,"author-save-crossed-installation");
    Need(station.ChildCount==0,"author-Worker-child-leak");
   }finally{storage.Close();storage.Completion.GetAwaiter().GetResult();}
  }
  File.WriteAllText(Path.Combine(output,"results.json"),observations.ToString());Console.WriteLine("PASS real converted C# author storage read/write/read/copy in LPAC Worker; preview and installation isolation; stop and cleanup");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
