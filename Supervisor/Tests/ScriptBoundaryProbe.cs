// Adversarial test image only. Never compiled into production ScriptWorker.
using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Net;
using System.Net.Sockets;
using System.Linq;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Native;
using SpatialWallpaper.Sdk.Hosting;

static class ScriptBoundaryProbe {
 static bool Denied(Action action){try{action();return false;}catch(UnauthorizedAccessException){return true;}}
 static int Main(string[] args){
  if(args.Length==1&&args[0]=="--child"){System.Threading.Thread.Sleep(10000);return 0;}
  var values=Enumerable.Range(0,args.Length/2).ToDictionary(i=>args[i*2],i=>args[i*2+1]);
  IntPtr writer=new IntPtr(long.Parse(values["--output-handle"],CultureInfo.InvariantCulture));
  WindowsWorkerBoundary.Require(writer,values["--lpac-canary"],values["--lpac-control"]);
  Console.WriteLine("probe: production boundary verified");
  var identity=new SessionIdentity(Guid.ParseExact(values["--session"],"N"),uint.Parse(values["--generation"],CultureInfo.InvariantCulture),SceneCommandCatalog.Digest);
  var config=JObject.Parse(File.ReadAllText(values["--program"]));
  bool read=Denied(()=>File.ReadAllBytes((string)config["outside_read"]));
  bool write=Denied(()=>File.WriteAllText((string)config["outside_write"],"synthetic negative"));
  bool scratch=Denied(()=>File.WriteAllText(Path.Combine(Path.GetDirectoryName(values["--program"]),"scratch/probe.txt"),"synthetic negative"));
  bool authorityRead=true,authorityWrite=true;
  if(config["authority_section"]!=null){
   string name=(string)config["authority_section"];
   authorityRead=Denied(()=>{using(var map=MemoryMappedFile.OpenExisting(name,MemoryMappedFileRights.Read))using(var view=map.CreateViewAccessor(0,96,MemoryMappedFileAccess.Read))view.ReadInt64(80);});
   authorityWrite=Denied(()=>{using(var map=MemoryMappedFile.OpenExisting(name,MemoryMappedFileRights.Write))using(var view=map.CreateViewAccessor(0,96,MemoryMappedFileAccess.Write))view.Write(80,123L);});
  }
  Console.WriteLine("probe: file attempts completed");
  bool network=false;int networkError=0;
  try{using(var socket=new Socket(AddressFamily.InterNetwork,SocketType.Stream,ProtocolType.Tcp))socket.Connect(IPAddress.Loopback,(int)config["port"]);}
  catch(SocketException failure){networkError=failure.NativeErrorCode;network=failure.SocketErrorCode==SocketError.AccessDenied;}
  Console.WriteLine("probe: socket attempt completed");
  bool child=false;int childError=0;
  try{using(var process=Process.Start(new ProcessStartInfo(Path.Combine(Path.GetDirectoryName(values["--program"]),"ScriptWorker.exe"),"--child"){UseShellExecute=false,CreateNoWindow=true})){if(process!=null)process.Kill();}}
  catch(Win32Exception failure){childError=failure.NativeErrorCode;child=childError==5||childError==1816;}
  using(var output=new FileStream(new SafeFileHandle(writer,true),FileAccess.Write,4096,false))ScriptWire.Write(output,identity,"fault",new JObject{
   {"error","synthetic-boundary-probe"},{"outside_read_denied",read},{"outside_write_denied",write},{"scratch_write_denied",scratch},
   {"network_denied",network},{"network_error",networkError},{"child_denied",child},{"child_error",childError},{"authority_read_denied",authorityRead},{"authority_write_denied",authorityWrite}},false);
  return 0;
 }
}
