using System;
using System.IO;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using SpatialTracking;
using SpatialWallpaper.Desktop;
using SpatialWallpaper.Library;
namespace SpatialWallpaper.Desktop {static class DesktopRuntime{public static string Descriptor;}}
static class DesktopClientChecks {
 static int Main(string[] args)=>Run(args[0]).GetAwaiter().GetResult();
 static async Task<int> Run(string root){
  string directory=Path.Combine(root,"client-"+Guid.NewGuid().ToString("N"));int reads=0,contentReads=0;string active="pool";
  TaskCompletionSource<JObject> completion=null;TaskCompletionSource<bool> entered=null;
  Func<bool,JObject> snapshot=success=>new JObject{["ready"]=true,["revision"]=++reads,["active_artwork"]=active,["succeeded"]=success,["message"]=success?null:"native-presentation-failed"};
  using(var host=new RuntimeTrackingHost()){
   host.StartManagerControl(directory,async command=>{
    string type=(string)command["type"];
    if(type=="GetContentState"){contentReads++;return new JObject{{"state",new JObject{{"works",new JObject()},{"builds",new JObject()},{"revisions",new JObject()},{"applied",new JObject()}}},{"page",new JObject{{"snapshot",new string('a',32)},{"next",null}}},{"sound_mixes",new JObject()},{"bundled_catalog",new JObject()}};}
    if(type=="ApplyDesktop"){entered.SetResult(true);return await completion.Task;}
    if(type=="StopDesktop"){active=null;return snapshot(true);}
    var state=snapshot(true);state["ready"]=reads>1;return state;
   });DesktopRuntime.Descriptor=Path.Combine(directory,"connection.json");
   using(var client=new DesktopHostClient()){
    await client.Connect();if(client.Available)throw new Exception("initial-not-ready-not-exercised");
    if(contentReads!=0)throw new Exception("content-commands-sent-before-original-runtime-ready");
    int readyNotifications=0;client.Changed+=()=>{if(client.Available)readyNotifications++;};
    await client.Connect();if(!client.Available)throw new Exception("connected-runtime-never-refreshed-readiness");
    if(readyNotifications!=1)throw new Exception("runtime-ready-change-not-notified");
    client.Disconnect();await client.Connect();
    if(!client.Available||readyNotifications!=2)throw new Exception("same-state-reconnect-not-notified");
    Console.WriteLine("PASS connected-but-initializing-runtime-eventually-ready");
    foreach(bool succeed in new[]{false,true}){
     completion=new TaskCompletionSource<JObject>();entered=new TaskCompletionSource<bool>();
     var applying=client.ApplyAsync(new WallpaperRequest("train","screen","preset",7));await entered.Task;
     if(applying.IsCompleted||client.ActiveArtworkId!="pool")throw new Exception("accepted-was-treated-as-applied");
     if(succeed)active="train";completion.SetResult(snapshot(succeed));var result=await applying;
     if(result.Succeeded!=succeed||client.ActiveArtworkId!=(succeed?"train":"pool"))throw new Exception("runtime-failure-or-confirmed-state-lost");
    }
    var stopped=await client.StopAsync();if(!stopped.Succeeded||client.ActiveArtworkId!=null)throw new Exception("confirmed-stop-did-not-clear-active");
    Console.WriteLine("PASS accepted-stays-pending; apply-failure-preserves-old-state; confirmed-apply-and-stop-follow-runtime");
   }
  }return 0;
 }
}
