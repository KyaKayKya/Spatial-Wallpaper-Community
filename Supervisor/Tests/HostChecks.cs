// Original MIT integration client. Disconnect must leave the independently running Supervisor intact.
using System;using System.IO;using System.IO.Pipes;using System.Text;using System.Threading;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision.Windows;
static class HostChecks {
 static JObject descriptor;static JObject Call(string operation,JObject payload){
  using(var pipe=new NamedPipeClientStream(".",(string)descriptor["pipe"],PipeDirection.InOut)){pipe.Connect(2000);var bytes=Encoding.UTF8.GetBytes(new JObject{{"auth",(string)descriptor["auth"]},{"operation",operation},{"payload",payload}}.ToString(Newtonsoft.Json.Formatting.None));var size=BitConverter.GetBytes(bytes.Length);pipe.Write(size,0,4);pipe.Write(bytes,0,bytes.Length);pipe.Flush();
   var header=new byte[4];Exact(pipe,header);int length=BitConverter.ToInt32(header,0);if(length<1||length>32768)throw new Exception("host-reply-size");var reply=new byte[length];Exact(pipe,reply);return JObject.Parse(Encoding.UTF8.GetString(reply));}
 }
 static void Exact(Stream stream,byte[] bytes){int at=0;while(at<bytes.Length){int n=stream.Read(bytes,at,bytes.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}}
 static void Require(bool value,string code){if(!value)throw new Exception(code);}
 static int Main(string[] args){try{
  descriptor=JObject.Parse(File.ReadAllText(args[0]));
  if(args.Length==2){foreach(JObject prior in JArray.Parse(File.ReadAllText(args[1]))){var record=Call("Diagnostic",new JObject{{"attempt",(string)prior["attempt"]}})["diagnostic"];Require(record!=null&&(string)record["event"]==(string)prior["reason"],"restart-diagnostic-history-lost");}Require((bool?)Call("Exit",new JObject())["ok"]==true,"restart-exit");return 0;}
  string instance=Guid.NewGuid().ToString("N");
  string probeRoot=args[4];WorkerWritePipe probeOutput=null;var probeArgs=new[]{@"\\.\pipe\"+(string)descriptor["pipe"],args[0],"0"};JObject boundary;
  try{using(var station=new PrivateStation())using(var probe=RestrictedProcess.Start(station,Path.Combine(probeRoot,"Probe.exe"),probeArgs,probeRoot,128L*1024*1024,sid=>{probeOutput=new WorkerWritePipe(sid);probeArgs[2]=probeOutput.ChildWriter.ToInt64().ToString();return new[]{probeOutput.ChildWriter};},null)){
   probeOutput.CloseChildCopy();Require(probe.Wait(5000)==0&&probe.ExitCode==0,"native-manager-probe-exit");var bytes=new byte[4096];int used=0,n;while((n=probeOutput.Read(bytes,used,bytes.Length-used))>0){used+=n;if(used==bytes.Length)throw new Exception("native-probe-size");}boundary=JObject.Parse(Encoding.UTF8.GetString(bytes,0,used));
  }}finally{probeOutput?.Dispose();}
  Require((int?)boundary["manager_pipe_open_error"]==5&&(int?)boundary["endpoint_file_open_error"]==5,"Worker-OS-access-to-manager-or-authentication-not-denied:"+boundary);
  var response=Call("Start",new JObject{{"installation",Guid.NewGuid().ToString("N")},{"build",args[2]},{"instance",instance},{"generation",1},{"intent","Manual"},{"package",args[1]}});Require((bool?)response["ok"]==true,"host-start-rejected:"+response);
  JObject state=null;var timer=System.Diagnostics.Stopwatch.StartNew();while(timer.ElapsedMilliseconds<20000){state=Call("Status",new JObject());if((string)state["instances"]?[instance]?["state"]=="Running")break;Thread.Sleep(50);}
  Require((string)state["instances"]?[instance]?["state"]=="Running","host-never-ready:"+state);
  Thread.Sleep(1200);state=Call("Status",new JObject());Require((string)state["instances"]?[instance]?["state"]=="Running","UI-disconnect-stopped-wallpaper:"+state);
  var idle=new NamedPipeClientStream[4];try{for(int i=0;i<idle.Length;i++){idle[i]=new NamedPipeClientStream(".",(string)descriptor["pipe"],PipeDirection.InOut);idle[i].Connect(2000);}Thread.Sleep(150);}finally{foreach(var connection in idle)connection?.Dispose();}
  state=Call("Status",new JObject());Require((string)state["instances"]?[instance]?["state"]=="Running","connection-capacity-stopped-background:"+state);
  long maximumStatus=0;for(int i=0;i<25;i++){var watch=System.Diagnostics.Stopwatch.StartNew();Require((bool?)Call("Status",new JObject())["ok"]==true,"status-failed");maximumStatus=Math.Max(maximumStatus,watch.ElapsedMilliseconds);}
  var badAuth=(string)descriptor["auth"];descriptor["auth"]=Convert.ToBase64String(new byte[32]);Require((bool?)Call("Exit",new JObject())["ok"]==false,"unauthenticated-exit-accepted");descriptor["auth"]=badAuth;
  Require((bool?)Call("Stop",new JObject{{"instance",instance}})["ok"]==true,"host-stop-rejected");
  timer.Restart();while(timer.ElapsedMilliseconds<5000){state=Call("Status",new JObject());if(state["instances"]?[instance]==null)break;Thread.Sleep(25);}Require(state["instances"]?[instance]==null,"host-stop-not-complete");
  string pending=Guid.NewGuid().ToString("N");Require((bool?)Call("Start",new JObject{{"installation",Guid.NewGuid().ToString("N")},{"build",args[2]},{"instance",pending},{"generation",1},{"intent","Manual"},{"package",args[1]}})["ok"]==true,"pending-start-rejected");Require((bool?)Call("Stop",new JObject{{"instance",pending}})["ok"]==true,"pending-start-could-not-stop");
  timer.Restart();while(timer.ElapsedMilliseconds<5000){state=Call("Status",new JObject());if(state["instances"]?[pending]==null)break;Thread.Sleep(25);}Require(state["instances"]?[pending]==null,"pending-stop-not-complete");
  var diagnosticResults=new JArray();string directory=Path.GetDirectoryName(args[3]);
  foreach(JObject check in JArray.Parse(File.ReadAllText(Path.Combine(directory,"diagnostic-cases.json")))){
   string failedInstance=Guid.NewGuid().ToString("N");
   Require((bool?)Call("Start",new JObject{{"installation",Guid.NewGuid().ToString("N")},{"build",(string)check["build"]},{"instance",failedInstance},{"generation",1},{"intent","Manual"},{"package",Path.Combine(directory,(string)check["file"])}})["ok"]==true,"diagnostic-start");
   string attempt=null;JObject result=null;timer.Restart();bool killed=false;
   while(timer.ElapsedMilliseconds<20000){
    state=Call("Status",new JObject());var current=state["instances"]?[failedInstance];if(current?["attempt"]!=null)attempt=(string)current["attempt"];
    if((string)check["reason"]=="ProcessExit"&&!killed&&(string)current?["state"]=="Running"){
     var journal=JObject.Parse(File.ReadAllText(Path.Combine(Path.GetDirectoryName(args[0]),"journal","state.json")));int pid=(int)journal["active"][attempt]["process"];System.Diagnostics.Process.GetProcessById(pid).Kill();killed=true;
    }
    foreach(JObject prior in (JArray)state["recent"])if((string)prior["instance"]==failedInstance)result=(JObject)prior["result"];
    if(result!=null)break;Thread.Sleep(25);
   }
   Require(result!=null&&(string)result["reason"]==(string)check["reason"],"diagnostic-reason:"+result);attempt=(string)result["attempt"];
   var diagnostic=Call("Diagnostic",new JObject{{"attempt",attempt}})["diagnostic"];Require(diagnostic!=null&&(string)diagnostic["instance"]==failedInstance&&(long)diagnostic["generation"]==1,"diagnostic-attempt-identity");
   var detail=JObject.Parse((string)diagnostic["diagnostic"]);Require((string)detail["worker_reported_reason"]==(string)check["reported"]&&((string)detail["log_tail"]).Length<=512,"diagnostic-report-or-bound");
   check["attempt"]=attempt;diagnosticResults.Add(check);
  }
  File.WriteAllText(Path.Combine(directory,"diagnostic-results.json"),diagnosticResults.ToString());
  Require((bool?)Call("Exit",new JObject())["ok"]==true,"host-exit-rejected");
  File.WriteAllText(args[3],new JObject{{"native_manager_boundary",boundary},{"ui_disconnect_reconnect",true},{"connection_capacity_preserved_workers",true},{"authentication_rejected",true},{"pending_stop",true},{"status_samples",25},{"maximum_status_roundtrip_ms",maximumStatus},{"normal_stop_and_exit",true}}.ToString());Console.WriteLine("PASS independent Supervisor start, durable-ready, UI disconnect/reconnect, authentication, stop and exit");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
