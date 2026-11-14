using System;using System.IO;using System.IO.Pipes;using System.Text;using System.Threading;using System.Diagnostics;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Broker;
static class AudioHostChecks {
 static JObject descriptor;
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);}
 static void Exact(Stream stream,byte[] bytes){int at=0;while(at<bytes.Length){var task=stream.ReadAsync(bytes,at,bytes.Length-at);if(!task.Wait(3000))throw new TimeoutException("manager-read");int n=task.Result;if(n==0)throw new EndOfStreamException();at+=n;}}
 static JObject Call(string operation,JObject payload){using(var pipe=new NamedPipeClientStream(".",(string)descriptor["pipe"],PipeDirection.InOut,PipeOptions.Asynchronous)){pipe.Connect(3000);byte[] body=Encoding.UTF8.GetBytes(new JObject{{"auth",descriptor["auth"]},{"operation",operation},{"payload",payload}}.ToString(Newtonsoft.Json.Formatting.None));pipe.Write(BitConverter.GetBytes(body.Length),0,4);pipe.Write(body,0,body.Length);var header=new byte[4];Exact(pipe,header);int count=BitConverter.ToInt32(header,0);Need(count>0&&count<=32768,"reply-bound");var result=new byte[count];Exact(pipe,result);return JObject.Parse(Encoding.UTF8.GetString(result));}}
 static long Revision(string path){try{using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete))using(var reader=new BinaryReader(file)){reader.ReadUInt64();return reader.ReadInt64();}}catch(FileNotFoundException){return 0;}catch(DirectoryNotFoundException){return 0;}}
 static int Main(string[] args){try{
  string output=args[0],state=Path.Combine(output,"state"),build=args[2];descriptor=JObject.Parse(File.ReadAllText(Path.Combine(state,"endpoint.json")));Guid installation=Guid.NewGuid();string instance=Guid.NewGuid().ToString("N");
  Need((bool?)Call("Start",new JObject{{"installation",installation.ToString("N")},{"build",build},{"instance",instance},{"generation",1},{"intent","Manual"},{"package",args[1]}})["ok"]==true,"host-audio-start");
  string snapshot=Path.Combine(state,"storage","live-"+installation.ToString("N"),"state.bin");var clock=Stopwatch.StartNew();bool running=false;
  while(clock.ElapsedMilliseconds<20000){var status=Call("Status",new JObject());running=(string)status["instances"]?[instance]?["state"]=="Running";if(running&&Revision(snapshot)!=0)break;Thread.Sleep(20);}
  Need(running&&Revision(snapshot)==1,"author-not-heard-or-cleared-before-source-exit");File.WriteAllText(Path.Combine(output,"ready-to-end-source"),"author-heard");var ending=Stopwatch.StartNew();
  while(ending.ElapsedMilliseconds<5000&&Revision(snapshot)==1)Thread.Sleep(10);
  Need(Revision(snapshot)==2,"configured-independent-Host-audio-author-not-complete");long saveMs=ending.ElapsedMilliseconds;
  bool pause=Array.IndexOf(args,"--pause-sampler")>=0,stable=Array.IndexOf(args,"--require-stable-zero")>=0;
  if(stable){var stability=Stopwatch.StartNew();while(stability.ElapsedMilliseconds<10000&&Revision(snapshot)==2)Thread.Sleep(10);Need(Revision(snapshot)==3,"author-stable-zero-proof-missing");}
  if(pause){
   Need((string)Call("Status",new JObject())["instances"]?[instance]?["state"]=="Running","Host-not-running-after-sampler-pause");
   File.WriteAllText(Path.Combine(output,"author-cleared-before-stop"),"author-cleared");var proof=Stopwatch.StartNew();
   while(!File.Exists(Path.Combine(output,"allow-host-stop"))&&proof.ElapsedMilliseconds<10000)Thread.Sleep(10);
   Need(File.Exists(Path.Combine(output,"allow-host-stop")),"sampler-kernel-exit-proof-missing-before-stop");
  }
  Need((bool?)Call("Stop",new JObject{{"instance",instance}})["ok"]==true,"Host-audio-stop");Need((bool?)Call("Exit",new JObject())["ok"]==true,"Host-audio-exit");using(var host=Process.GetProcessById(int.Parse(args[3])))Need(host.WaitForExit(5000),"Host-audio-process-exit");
  using(var journal=new FaultJournal(Path.Combine(output,"observer-journal")))using(var store=new PrivateStore(Path.Combine(state,"storage"))){var session=new BrokerSession(journal.Begin(installation,build,Guid.NewGuid(),2,StartIntent.Manual),false,new[]{"private-storage"});var saved=store.Read(session);Need(saved.Version==(stable?3:2)&&Encoding.UTF8.GetString(saved.Get("audio-heard"))=="audio-heard"&&Encoding.UTF8.GetString(saved.Get("audio-result"))=="audio-cleared","Host-audio-durable-result");if(stable)Need(Encoding.UTF8.GetString(saved.Get("audio-stability"))=="120-updates-zero","author-stable-zero-proof-rejected");}
  File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"configured_independent_host",true},{"boundary",pause?"sampler-pause":"source-exit"},{"author_heard_before_boundary",true},{"revision",stable?3:2},{"stable_zero_author_updates",stable?120:0},{"author_observed_nonzero_then_zero",true},{"boundary_ready_to_saved_observed_ms",saveMs},{"stop_exit",true},{"elapsed_ms",clock.ElapsedMilliseconds}}.ToString());Console.WriteLine("PASS configured independent Host, selected process audio, C# LPAC author heard handshake, boundary clear, durable result and stop/exit");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
