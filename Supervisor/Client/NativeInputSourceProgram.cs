using System;
using System.IO;
using System.IO.Pipes;
using System.Drawing;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Supervision.Broker;
using SpatialWallpaper.Supervision.Interactive;

namespace SpatialWallpaper.Supervision.Preview {
 // Minimal role of the existing trusted client image. No Library, Store,
 // Tracker, tray, audio device, render target or author execution is started.
 static class NativeInputSourceProgram {
  internal static int Run(string path){
   PinnedProcessOwner parent=null,renderer=null;WindowsNativeDesktopInput source=null;NamedPipeClientStream pipe=null;System.Windows.Forms.Timer timer=null;Task control=null,closing=null;
   var gate=new object();bool stop=false,enabled=true,retry=false;long requestedPause=0,pausedRevision=0;string error=null;JObject state=new JObject{{"ready",false},{"released",false},{"paused_revision",0},{"helpers",new JArray()}};
   try{
    var config=RuntimeDistribution.Read(path,16384);RuntimeDistribution.Fields(config,"version","pipe","nonce","parent","renderer","window","thread","region","descriptor","attempt","instance","generation");if((int?)config["version"]!=1)throw new InvalidDataException("native-input-role-version");
    var owner=(JObject)config["parent"];RuntimeDistribution.Fields(owner,"pid","creation_filetime");parent=PinnedProcessOwner.Open((int)owner["pid"],(long)owner["creation_filetime"]);
    owner=(JObject)config["renderer"];RuntimeDistribution.Fields(owner,"pid","creation_filetime");renderer=PinnedProcessOwner.Open((int)owner["pid"],(long)owner["creation_filetime"]);
    var region=(JArray)config["region"];if(region.Count!=4)throw new InvalidDataException("native-input-role-region");
    var identity=new WorkerIdentity(Guid.ParseExact((string)config["attempt"],"N"),Guid.ParseExact((string)config["instance"],"N"),(uint)config["generation"]);var connection=new SupervisorManagerConnection((string)config["descriptor"]);
    pipe=new NamedPipeClientStream(".",(string)config["pipe"],PipeDirection.InOut,PipeOptions.Asynchronous);pipe.Connect(5000);if(parent.HasExited||NativeScriptControl.Peer(pipe,false)!=parent.Id)throw new InvalidDataException("native-input-role-parent");
    source=new WindowsNativeDesktopInput(renderer,new IntPtr((long)config["window"]),(uint)config["thread"],new Rectangle((int)region[0],(int)region[1],(int)region[2],(int)region[3]),cancel=>new DesktopInputChannel(connection.OpenNativeDesktopInputStream(identity,renderer,cancel).GetAwaiter().GetResult()),Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"DesktopHitTester.exe"));
    string nonce=(string)config["nonce"];
    control=Task.Run(()=>{try{while(true){
     var request=NativeScriptControl.Read(pipe);RuntimeDistribution.Fields(request,"nonce","enabled","pause_revision","stop","retry");if((string)request["nonce"]!=nonce||request["enabled"]?.Type!=JTokenType.Boolean||request["pause_revision"]?.Type!=JTokenType.Integer||request["stop"]?.Type!=JTokenType.Boolean||request["retry"]?.Type!=JTokenType.Boolean)throw new InvalidDataException("native-input-role-request");
     JObject reply;lock(gate){long revision=(long)request["pause_revision"];if(revision<requestedPause)throw new InvalidDataException("native-input-pause-regressed");requestedPause=revision;enabled=(bool)request["enabled"];stop|=(bool)request["stop"];retry|=(bool)request["retry"];reply=(JObject)state.DeepClone();}reply["nonce"]=nonce;NativeScriptControl.Write(pipe,reply);if((bool?)reply["released"]==true)break;
    }}catch(EndOfStreamException){lock(gate)stop=true;}catch(Exception failure){lock(gate){error=failure.Message;stop=true;}}});
    bool applied=true;var retryClock=System.Diagnostics.Stopwatch.StartNew();timer=new System.Windows.Forms.Timer{Interval=10};timer.Tick+=(s,e)=>{
     try{
      bool active,halt,again;long revision;lock(gate){active=enabled;revision=requestedPause;halt=stop||parent.HasExited||renderer.HasExited;again=retry;retry=false;}
      if(halt){
       if(closing==null)closing=source.StopAsync();
       // EOF/parent loss still owns the original hooks. Keep driving their
       // cleanup even when no control connection remains to request a retry.
       if(closing.IsFaulted&&(again||retryClock.ElapsedMilliseconds>=1000)){closing=source.RetryCleanupAsync();retryClock.Restart();}
      }
      else if(active!=applied){source.SetEnabled(active);applied=active;}
      if(!halt&&!applied&&source.Idle)pausedRevision=revision;
      lock(gate){state=new JObject{{"ready",source.Ready},{"released",source.ResourcesReleased},{"paused_revision",pausedRevision},{"helpers",new JArray(source.HelperProcessIds)},{"error",error??source.Failure}};}
      if(source.ResourcesReleased&&(control.IsCompleted||!pipe.IsConnected))Application.ExitThread();
     }catch(Exception failure){lock(gate){error=failure.GetBaseException().Message;stop=true;}}
    };timer.Start();Application.Run();
    if(!source.ResourcesReleased)throw new InvalidOperationException("native-input-role-resources-unreleased");
    return error==null?0:1;
   }catch(Exception failure){Console.Error.WriteLine("Native input role: "+failure);return 1;}
   finally{
    timer?.Stop();timer?.Dispose();pipe?.Dispose();
    if(source!=null&&!source.ResourcesReleased){var cleanup=source.StopAsync();var clock=System.Diagnostics.Stopwatch.StartNew();while(!cleanup.IsCompleted&&clock.ElapsedMilliseconds<12000){Application.DoEvents();Thread.Sleep(5);}if(!source.ResourcesReleased)Console.Error.WriteLine("native-input-role-cleanup-unproven");}
    renderer?.Dispose();parent?.Dispose();
   }
  }
 }
}
