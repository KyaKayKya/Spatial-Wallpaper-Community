using System;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Supervision.Broker;

namespace SpatialWallpaper.Supervision.Windows {
 // An explicitly selected trusted launch role, with no desktop,
 // Tracker, tray, GPU sampler or framebuffer. The bounded storage actor uses
 // the original private store, with installation/preview identity from the owner.
 internal static class ScriptLaunchHost {
  [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool DuplicateHandle(IntPtr sourceProcess,IntPtr source,IntPtr destinationProcess,out IntPtr destination,uint access,bool inherit,uint options);
  static long Duplicate(IntPtr source,Process destination){IntPtr handle;if(!DuplicateHandle(GetCurrentProcess(),source,destination.Handle,out handle,0,false,2))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());return handle.ToInt64();}
  internal static int Run(string path){
   var info=new FileInfo(path);if(info.Length<1||info.Length>16384||(info.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("script-launch-config");
   var config=NativeScriptControl.Parse(File.ReadAllText(path));int pid=(int)config["parent_pid"];long creation=(long)config["parent_creation"];
   using(var parent=Process.GetProcessById(pid)){
    if(parent.HasExited||PinnedProcessOwner.ReadCreationFileTime(parent.Handle)!=creation)throw new InvalidDataException("script-launch-parent");
    using(var pipe=new NamedPipeClientStream(".",(string)config["pipe"],PipeDirection.InOut,PipeOptions.Asynchronous)){
     pipe.Connect(5000);if(NativeScriptControl.Peer(pipe,false)!=pid)throw new InvalidDataException("script-launch-server");
     var inventory=(JObject)config["worker_files"];var files=inventory.Properties().ToDictionary(p=>p.Name,p=>(string)p.Value);
     var identity=new WorkerIdentity(Guid.ParseExact((string)config["attempt"],"N"),Guid.ParseExact((string)config["session"],"N"),(uint)config["generation"]);
     // This process has not created any windows or input collectors.
     using(var station=new PrivateStation()){
      var launcher=new ScriptProcessLauncher(station,new WorkerImage((string)config["worker_image"],"ScriptWorker.exe",files),(string)config["attempts"]);
      var store=config["storage"] as JObject;StorageBroker storage=null;BrokerSession session=null;
      if(store!=null){
       if(store.Count!=3||store["root"]?.Type!=JTokenType.String||store["installation"]?.Type!=JTokenType.String||store["preview"]?.Type!=JTokenType.Boolean)throw new InvalidDataException("script-storage-contract");
       Guid installation;if(!Guid.TryParseExact((string)store["installation"],"N",out installation)||installation==Guid.Empty)throw new InvalidDataException("script-storage-installation");
       // This specification is created by the trusted manager from the admitted
       // artwork identity. It is never read from author capability messages.
       var receipt=new AttemptReceipt(Guid.NewGuid(),identity.Attempt,installation,(string)config["build"],identity.Instance,identity.Generation);
       session=new BrokerSession(receipt,(bool)store["preview"],new[]{"private-storage"});
       storage=new StorageBroker((string)store["root"],sharedProcess:true);storage.Initialized.GetAwaiter().GetResult();
      }
      try{using(var script=launcher.Start(new ScriptLaunchRequest(identity,(string)config["program"],(string)config["program_sha256"],(string)config["build"]),session,storage)){
       long read=Duplicate(script.OutputHandle,parent),write=Duplicate(script.InputHandle,parent);
       var ready=new JObject{{"nonce",config["nonce"]},{"reader",read},{"writer",write},{"script_pid",script.ProcessId},{"script_creation",script.CreationFileTime}};
       NativeScriptControl.Deadline(()=>{NativeScriptControl.Write(pipe,ready);return true;},5000);
       var command=Task.Run(()=>NativeScriptControl.Read(pipe));
       while(!command.Wait(50)){
        if(parent.HasExited||!script.Alive)return 0;
       }
       var stop=command.GetAwaiter().GetResult();if((string)stop["nonce"]!=(string)config["nonce"]||(string)stop["operation"]!="stop")throw new InvalidDataException("script-launch-stop");
       return 0; // Dispose performs independent stop, deadline, kernel exit proof.
      }}finally{session?.Close();if(storage!=null){storage.Close();storage.Completion.GetAwaiter().GetResult();}}
     }
    }
   }
  }
 }
}
