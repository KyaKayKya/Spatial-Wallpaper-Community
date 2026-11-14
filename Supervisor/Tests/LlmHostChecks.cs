using System;using System.IO;using System.IO.Pipes;using System.Text;using System.Threading;using System.Diagnostics;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Broker;
static class LlmHostChecks {
 static JObject descriptor;
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static void Exact(Stream stream,byte[] bytes){int at=0;while(at<bytes.Length){var task=stream.ReadAsync(bytes,at,bytes.Length-at);if(!task.Wait(3000))throw new TimeoutException("manager-read");int n=task.Result;if(n==0)throw new EndOfStreamException();at+=n;}}
 static JObject Call(string operation,JObject payload){using(var pipe=new NamedPipeClientStream(".",(string)descriptor["pipe"],PipeDirection.InOut,PipeOptions.Asynchronous)){pipe.Connect(3000);byte[] body=Encoding.UTF8.GetBytes(new JObject{{"auth",descriptor["auth"]},{"operation",operation},{"payload",payload}}.ToString(Newtonsoft.Json.Formatting.None));pipe.Write(BitConverter.GetBytes(body.Length),0,4);pipe.Write(body,0,body.Length);var header=new byte[4];Exact(pipe,header);int count=BitConverter.ToInt32(header,0);Need(count>0&&count<=32768,"reply-bound");var result=new byte[count];Exact(pipe,result);return JObject.Parse(Encoding.UTF8.GetString(result));}}
 static int Main(string[] args){try{
  string output=args[0],state=Path.Combine(output,"state"),build=args[2];descriptor=JObject.Parse(File.ReadAllText(Path.Combine(state,"endpoint.json")));Guid installation=Guid.NewGuid();string instance=Guid.NewGuid().ToString("N");
   string snapshot=Path.Combine(state,"storage","live-"+installation.ToString("N"),"state.bin");var clock=Stopwatch.StartNew();long revision=0;bool running=false;var rounds=new JArray();
   for(int generation=1;generation<=2;generation++){
   Need((bool?)Call("Start",new JObject{{"installation",installation.ToString("N")},{"build",build},{"instance",instance},{"generation",generation},{"intent","Manual"},{"package",args[1]}})["ok"]==true,"host-LLM-start-"+generation);
   var attempt=Stopwatch.StartNew();
   while(attempt.ElapsedMilliseconds<20000){
   var status=Call("Status",new JObject());running=(string)status["instances"]?[instance]?["state"]=="Running";
   try{using(var file=new FileStream(snapshot,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new BinaryReader(file)){reader.ReadUInt64();revision=reader.ReadInt64();}}catch(FileNotFoundException){}catch(DirectoryNotFoundException){}
    if(revision>=2*generation&&running)break;Thread.Sleep(20);
  }
   Need(revision==2*generation&&running,"configured-independent-Host-LLM-author-not-complete-"+generation);
   Need((bool?)Call("Stop",new JObject{{"instance",instance}})["ok"]==true,"Host-LLM-stop");
   Need((bool?)Call("Stop",new JObject{{"instance",instance}})["ok"]==true,"Host-LLM-repeat-stop");
   var stopping=Stopwatch.StartNew();bool removed=false;while(stopping.ElapsedMilliseconds<5000){removed=Call("Status",new JObject())["instances"]?[instance]==null;if(removed)break;Thread.Sleep(20);}Need(removed,"Host-LLM-stopped-before-restart");
   rounds.Add(new JObject{{"generation",generation},{"revision",revision},{"stopped",true},{"elapsed_ms",attempt.ElapsedMilliseconds}});
   }
   Need((bool?)Call("Exit",new JObject())["ok"]==true,"Host-LLM-exit");using(var host=Process.GetProcessById(int.Parse(args[3])))Need(host.WaitForExit(5000),"Host-LLM-process-exit");
   using(var journal=new FaultJournal(Path.Combine(output,"observer-journal")))using(var store=new PrivateStore(Path.Combine(state,"storage"))){var session=new BrokerSession(journal.Begin(installation,build,Guid.NewGuid(),3,StartIntent.Manual),false,new[]{"private-storage"});var saved=store.Read(session);Need(saved.Version==4&&Encoding.UTF8.GetString(saved.Get("llm-result"))=="reply-as-data"&&Encoding.UTF8.GetString(saved.Get("llm-cancel"))=="Cancelled","Host-LLM-durable-results");}
   File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"configured_independent_host",true},{"running_and_saved",true},{"revision",revision},{"rounds",rounds},{"reply","reply-as-data"},{"cancellation","Cancelled"},{"stop_exit",true},{"elapsed_ms",clock.ElapsedMilliseconds}}.ToString());Console.WriteLine("PASS configured independent Host, local fixed LLM HTTP, two generations, author saturation/cancel, durable results and stop/exit");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
