using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Globalization;
using System.Diagnostics;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk;
using SpatialWallpaper.Sdk.Native;
using SpatialWallpaper.Sdk.Native.Windows;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Sdk.Execution;
using SpatialWallpaper.Sdk.Capabilities;

namespace SpatialWallpaper.Script {
 static class ScriptWorkerMain {
  [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr process,out long creation,out long exit,out long kernel,out long user);
  static Dictionary<string,string> Arguments(string[] args){
   var names=new HashSet<string>(new[]{"--program","--build","--session","--attempt","--generation","--input-handle","--output-handle","--stop-handle","--cap-request-handle","--cap-reply-handle","--lpac-canary","--lpac-control"});
   var values=new Dictionary<string,string>(StringComparer.Ordinal);if(args.Length!=names.Count*2)throw new ArgumentException("script-launch-contract");
   for(int i=0;i<args.Length;i+=2){if(!names.Contains(args[i])||values.ContainsKey(args[i]))throw new ArgumentException("script-launch-argument");values.Add(args[i],args[i+1]);}return values;
  }
  static IntPtr Handle(string text){long value=long.Parse(text,CultureInfo.InvariantCulture);if(value==0||value==-1)throw new ArgumentException("script-pipe-handle");return new IntPtr(value);}
  static byte[] ProgramBytes(string path){
   using(var file=new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.Read)){
    if(file.Length<1||file.Length>SceneCommandCatalog.MaxScriptBytes)throw new InvalidDataException("script-program-size");var bytes=new byte[(int)file.Length];int at=0;
    while(at<bytes.Length){int count=file.Read(bytes,at,bytes.Length-at);if(count==0)throw new EndOfStreamException();at+=count;}return bytes;
   }
  }
  static int Main(string[] args){
   SessionIdentity identity=null;FileStream output=null;int done=0;Thread stopMonitor=null;
   try{
    var values=Arguments(args);identity=new SessionIdentity(Guid.ParseExact(values["--session"],"N"),uint.Parse(values["--generation"],CultureInfo.InvariantCulture),SceneCommandCatalog.Digest);
    IntPtr writer=Handle(values["--output-handle"]);WindowsWorkerBoundary.Require(writer,values["--lpac-canary"],values["--lpac-control"]);
    output=new FileStream(new SafeFileHandle(writer,true),FileAccess.Write,4096,false);
    var workerIdentity=new WorkerIdentity(Guid.ParseExact(values["--attempt"],"N"),identity.Instance,identity.Generation);
    using(var stop=new WindowsWorkerStopReader(Handle(values["--stop-handle"]),workerIdentity)){
     stopMonitor=new Thread(()=>{while(Volatile.Read(ref done)==0){if(stop.Requested)Environment.Exit(0);if(stop.Error!=null)Environment.Exit(22);Thread.Sleep(2);}}){IsBackground=true,Name="SpatialWallpaper script stop monitor"};stopMonitor.Start();
     var build=NativeScriptDefinition.Admit(ProgramBytes(values["--program"]));if(build.BuildId!=values["--build"]||!build.IsNativeScript)throw new InvalidDataException("script-build-identity");
     long creation,exit,kernel,user;if(!GetProcessTimes(GetCurrentProcess(),out creation,out exit,out kernel,out user))throw new InvalidOperationException("script-process-identity");
     ScriptWire.Write(output,identity,"hello",new JObject{{"pid",Process.GetCurrentProcess().Id},{"creation_filetime",creation},{"build",build.BuildId},{"role","pure-script"},
      {"assemblies",new JArray(AppDomain.CurrentDomain.GetAssemblies().Select(a=>a.GetName().Name))}},false);
     using(var input=new FileStream(new SafeFileHandle(Handle(values["--input-handle"]),true),FileAccess.Read,4096,false)){
      var start=ScriptWire.Read(input,identity,true);if(start.Type!="start")throw new InvalidDataException("script-start-required");var data=start.Payload;JsonData.Fields(data,"snapshot","root_nodes","resources");
      var snapshot=ScriptWire.Snapshot(data["snapshot"] as JObject);var rootNodes=JsonData.Array(data["root_nodes"],SceneCommandCatalog.MaxObjects).Select(t=>(long)JsonData.Integer(t,1,int.MaxValue)).ToArray();
      if(rootNodes.Length!=((JArray)build.Content["nodes"]).Count)throw new InvalidDataException("script-root-layout");
      using(var domain=new OwnedDomain(identity.Instance,identity.Generation)){
      using(var capabilities=values["--cap-request-handle"]=="0"&&values["--cap-reply-handle"]=="0"?null:new WindowsCapabilityClient(domain.Ledger,workerIdentity,Handle(values["--cap-request-handle"]),Handle(values["--cap-reply-handle"]))) {
      var calls=capabilities==null?null:new CapabilityCalls(domain.Ledger,capabilities);
      var inputs=new NativeCapabilityBindings(domain,WindowsMachineClock.Milliseconds,calls==null?null:new NativeStorageBindings(calls));
      using(var mirror=new MirrorVmHost(domain,identity,snapshot,inputs)){
       mirror.BindRootGraph(rootNodes);var map=data["resources"] as JObject;if(map==null)throw new InvalidDataException("script-resource-map");
       var content=build.Content;var definitions=((JArray)content["resources"]).Cast<JObject>().Select(r=>new{Id=(string)r["id"],Kind=(string)r["kind"]}).ToList();
       definitions.AddRange(((JArray)content["prefabs"]).Cast<JObject>().Select(p=>new{Id=(string)p["id"],Kind="prefab"}));
       if(map.Count!=definitions.Count)throw new InvalidDataException("script-resource-map-size");
       foreach(var definition in definitions){long id=JsonData.Integer(map[definition.Id],1,int.MaxValue);if(snapshot.Find(id)?.Supports(definition.Kind)!=true)throw new InvalidDataException("script-resource-map-kind");mirror.BindResource(definition.Id,id);}
       foreach(JObject prefab in (JArray)content["prefabs"])mirror.BindPrefabGraph((long)map[(string)prefab["id"]],(JArray)prefab["nodes"]);
       var runtime=new VmRuntime(build,domain,mirror,new ExecutionQuota(RuntimeLimits.Instructions,RuntimeLimits.Depth,RuntimeLimits.ExecutionMs));mirror.BindRuntime(runtime);
       while(true){
        var message=ScriptWire.Read(input,identity,true);if(message.Type=="stop")break;if(message.Type!="tick")throw new InvalidDataException("script-tick-required");
        var tick=ScriptWire.Tick(message.Payload);inputs.Advance(tick);mirror.BeginTick(tick);if(runtime.State==VmState.Created)runtime.Start();runtime.Update();var batch=mirror.EndTick();ScriptWire.Write(output,identity,"batch",ScriptWire.Batch(batch),false);
        message=ScriptWire.Read(input,identity,true);if(message.Type=="stop")break;if(message.Type!="ack")throw new InvalidDataException("script-ack-required");
        mirror.ApplyAck(ScriptWire.Ack(message.Payload,identity));if(mirror.Stopped)return 20;
       }
      }
      }
      }
     }
    }
    return 0;
   }catch(Exception failure){
    if(output!=null&&identity!=null)try{string reason=failure.GetType().Name+":"+failure.Message;if(reason.Length>256)reason=reason.Substring(0,256);ScriptWire.Write(output,identity,"fault",new JObject{{"error",reason}},false);}catch{}
    return 20;
   }finally{
    Volatile.Write(ref done,1);if(stopMonitor!=null)stopMonitor.Join(500);output?.Dispose();
   }
  }
 }
}
