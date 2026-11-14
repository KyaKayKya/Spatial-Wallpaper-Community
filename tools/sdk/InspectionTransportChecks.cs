using System;using System.IO;using System.IO.Pipes;using System.Linq;using System.Text;using System.Threading.Tasks;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;
static class InspectionTransportChecks {
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);Console.WriteLine("PASS "+reason);}
 static JObject Read(Stream stream){using(var reader=new BinaryReader(stream,Encoding.UTF8,true)){int n=reader.ReadInt32();if(n<1||n>4096)throw new InvalidDataException();var bytes=reader.ReadBytes(n);if(bytes.Length!=n)throw new EndOfStreamException();return JObject.Parse(Encoding.UTF8.GetString(bytes));}}
 static void Send(Stream stream,JObject value){var bytes=Encoding.UTF8.GetBytes(value.ToString(Newtonsoft.Json.Formatting.None));using(var writer=new BinaryWriter(stream,Encoding.UTF8,true)){writer.Write(bytes.Length);writer.Write(bytes);writer.Flush();}}
 public static void Run(string output){
  foreach(string mode in new[]{"wrong-host","wrong-ticket","short-digest","extra-field","duplicate-capability","pending-with-result","unknown-state","unknown-capability"}){
   Guid host=Guid.NewGuid();string name="SpatialWallpaper.Supervisor."+Guid.NewGuid().ToString("N"),descriptor=Path.Combine(output,"inspection-"+mode+".json");File.WriteAllText(descriptor,new JObject{{"version",1},{"pipe",name},{"auth",Convert.ToBase64String(new byte[32])},{"session_host",host.ToString("N")}}.ToString());
   using(var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous)){
    var server=Task.Run(()=>{pipe.WaitForConnection();var request=Read(pipe);Need((string)request["operation"]=="BeginPackageInspection"&&(string)request["payload"]["host"]==host.ToString("N"),"inspection-request-identity-"+mode);var reply=new JObject{{"ok",true},{"host",host.ToString("N")},{"ticket",request["payload"]["ticket"]},{"state","Succeeded"},{"build",new string('a',64)},{"archive_sha256",new string('b',64)},{"capabilities",new JArray("input")},{"error",null}};
     if(mode=="wrong-host")reply["host"]=Guid.NewGuid().ToString("N");if(mode=="wrong-ticket")reply["ticket"]=Guid.NewGuid().ToString("N");if(mode=="short-digest")reply["archive_sha256"]="bad";if(mode=="extra-field")reply["untrusted"]=true;if(mode=="duplicate-capability")reply["capabilities"]=new JArray("input","input");if(mode=="pending-with-result")reply["state"]="Pending";if(mode=="unknown-state")reply["state"]="AlmostDone";if(mode=="unknown-capability")reply["capabilities"]=new JArray("unknown-capability");Send(pipe,reply);
    });
    bool rejected=false;try{new SupervisorManagerConnection(descriptor).InspectPackage("candidate.spatial").GetAwaiter().GetResult();}catch(InvalidDataException){rejected=true;}Need(server.Wait(3000),"inspection-fake-server-exit-"+mode);server.GetAwaiter().GetResult();Need(rejected,"inspection-rejects-malformed-reply-"+mode);
   }
  }
 }
}
