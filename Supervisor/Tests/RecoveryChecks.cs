// Original MIT independent-process recovery client.
using System;using System.IO;using System.IO.Pipes;using System.Text;using System.Threading;using System.Linq;using Newtonsoft.Json.Linq;
static class RecoveryChecks {
 static JObject endpoint;
 static void Exact(Stream stream,byte[] bytes){int at=0;while(at<bytes.Length){int n=stream.Read(bytes,at,bytes.Length-at);if(n==0)throw new EndOfStreamException();at+=n;}}
 static JObject Call(string operation,JObject payload){using(var pipe=new NamedPipeClientStream(".",(string)endpoint["pipe"],PipeDirection.InOut)){pipe.Connect(2000);var bytes=Encoding.UTF8.GetBytes(new JObject{{"auth",(string)endpoint["auth"]},{"operation",operation},{"payload",payload}}.ToString(Newtonsoft.Json.Formatting.None));var header=BitConverter.GetBytes(bytes.Length);pipe.Write(header,0,4);pipe.Write(bytes,0,bytes.Length);pipe.Flush();Exact(pipe,header);int count=BitConverter.ToInt32(header,0);if(count<1||count>32768)throw new Exception("reply-size");bytes=new byte[count];Exact(pipe,bytes);return JObject.Parse(Encoding.UTF8.GetString(bytes));}}
 static void Require(bool value,string code){if(!value)throw new Exception(code);}
 static int Main(string[] args){try{
  string mode=args[0];endpoint=JObject.Parse(File.ReadAllText(args[1]));string installation=args[5];var results=new JObject();
  if(mode=="start"){
   var ids=new JArray();for(int i=0;i<2;i++){string instance=Guid.NewGuid().ToString("N");ids.Add(instance);Require((bool?)Call("Start",new JObject{{"installation",installation},{"build",args[3]},{"instance",instance},{"generation",1},{"intent","Manual"},{"package",args[2]}})["ok"]==true,"start-rejected");}
   var timer=System.Diagnostics.Stopwatch.StartNew();JObject status=null;while(timer.ElapsedMilliseconds<25000){status=Call("Status",new JObject());if(ids.All(id=>(string)status["instances"]?[(string)id]?["state"]=="Running"))break;Thread.Sleep(25);}Require(ids.All(id=>(string)status["instances"]?[(string)id]?["state"]=="Running"),"two-independent-Workers-not-ready:"+status);results["instances"]=ids;results["installation"]=installation;
  }else if(mode=="resume"){
   Require(!((JObject)Call("Status",new JObject())["instances"]).Properties().Any(),"backend-restarted-Workers-automatically");string instance=Guid.NewGuid().ToString("N");Require((bool?)Call("Start",new JObject{{"installation",installation},{"build",args[3]},{"instance",instance},{"generation",2},{"intent","Resume"},{"package",args[2]}})["ok"]==true,"resume-was-not-queued");
   var timer=System.Diagnostics.Stopwatch.StartNew();JObject status=null;while(timer.ElapsedMilliseconds<5000){status=Call("Status",new JObject());if(((JArray)status["recent"]).Any(item=>(string)item["instance"]==instance&&(string)item["result"]["state"]=="Failed"))break;Thread.Sleep(20);}Require(((JArray)status["recent"]).Any(item=>(string)item["instance"]==instance&&(string)item["result"]["state"]=="Failed"),"reimported-problem-build-resumed:"+status);Require(!((JObject)status["instances"]).Properties().Any(),"recovery-created-active-instance");results["automatic_start_absent"]=true;results["actual_reimport_resume_denied"]=true;
   Require((bool?)Call("Exit",new JObject())["ok"]==true,"exit-rejected");
  }else throw new Exception("mode");
  File.WriteAllText(args[4],results.ToString());return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
