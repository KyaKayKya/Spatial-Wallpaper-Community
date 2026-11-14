using System;using System.IO;using System.Text;using System.Diagnostics;using System.Threading;using System.Collections.Generic;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Windows;using SpatialWallpaper.Supervision.Broker;using SpatialWallpaper.Sdk.Hosting;
static class LlmAuthorChecks {
 static void Need(bool value,string code){if(!value)throw new Exception(code);}
 static WorkerImage Image(string root,string executable,string inventory,string field){var files=new Dictionary<string,string>();foreach(var item in ((JObject)JObject.Parse(File.ReadAllText(inventory))[field]).Properties())files.Add(item.Name,(string)item.Value);return new WorkerImage(root,executable,files);}
 static int Main(string[] args){try{
  string output=args[0];var clock=Stopwatch.StartNew();Guid installation=Guid.NewGuid();
  using(var station=new PrivateStation())using(var journal=new FaultJournal(Path.Combine(output,"journal")))using(var llm=new LlmBroker(LlmConfiguration.Parse(JObject.Parse(File.ReadAllText(args[5]))))){
   var storage=new StorageBroker(Path.Combine(output,"storage"));storage.Initialized.GetAwaiter().GetResult();
   try{
    var validator=new ValidatorLauncher(station,Image(args[3],"Validator.exe",Path.Combine(args[3],"build.json"),"files"),args[4]);
    var launcher=new WindowsWorkerLauncher(station,Image(Path.Combine(args[1],"player"),"Worker.exe",Path.Combine(args[1],"report.json"),"runtime_files"),args[4],()=>clock.ElapsedMilliseconds,640,360,admission:validator,persistence:storage,language:llm);
    string build=(string)JObject.Parse(File.ReadAllText(Path.Combine(args[2],"author.json")))["build"];var receipt=journal.Begin(installation,build,Guid.NewGuid(),1,StartIntent.Manual);
    var request=new LaunchRequest(installation,build,receipt.Instance,1,StartIntent.Manual,Path.Combine(args[2],"scene.spatial"),RuntimeLimits.ProcessBytes);
    using(var worker=(WindowsManagedWorker)launcher.Start(request,receipt,pid=>journal.RecordProcess(receipt,pid))){
     var duration=Stopwatch.StartNew();StorageResult cancellation=null;
     while(duration.ElapsedMilliseconds<20000){var sample=worker.Sample();if(!sample.Alive||sample.ProtocolFault)break;cancellation=storage.Read(worker.Session,"llm-cancel").GetAwaiter().GetResult();if(cancellation.Value!=null&&worker.Sequence>0)break;Thread.Sleep(20);}
     File.WriteAllBytes(Path.Combine(output,"worker.log"),worker.ReadLog());
     Need(cancellation?.Value!=null&&Encoding.UTF8.GetString(cancellation.Value)=="Cancelled","LPAC-author-saturated-LLM-cancel-failed");
     var result=storage.Read(worker.Session,"llm-result").GetAwaiter().GetResult();Need(result.Value!=null&&Encoding.UTF8.GetString(result.Value)=="reply-as-data"&&worker.Sequence>0,"LPAC-author-LLM-response-failed");
     File.WriteAllText(Path.Combine(output,"results.json"),new JObject{{"value",Encoding.UTF8.GetString(result.Value)},{"cancellation",Encoding.UTF8.GetString(cancellation.Value)},{"version",cancellation.Version},{"frames",worker.Sequence},{"roundtrip_ms",duration.ElapsedMilliseconds}}.ToString());
     journal.MarkStopping(receipt);worker.Kill();
    }
    journal.Complete(receipt);Need(station.ChildCount==0,"LLM-Worker-child-leak");llm.Dispose();Need(llm.Completion.Wait(1000),"LLM-requests-survived-Worker-stop");
   }finally{storage.Close();storage.Completion.GetAwaiter().GetResult();}
  }
  Console.WriteLine("PASS converted C# LLM author in LPAC Worker, real HTTP, saturated cancellation, response-as-data, persistence and stop");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
