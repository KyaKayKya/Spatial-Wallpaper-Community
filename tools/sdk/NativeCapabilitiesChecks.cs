extern alias supervisor;
using System;using System.IO;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;using System.Security.Cryptography;using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Native;
using Windows=supervisor::SpatialWallpaper.Supervision.Windows;
using Broker=supervisor::SpatialWallpaper.Supervision.Broker;
using Supervision=supervisor::SpatialWallpaper.Supervision;
using WorkerIdentity=supervisor::SpatialWallpaper.Sdk.Hosting.WorkerIdentity;
static class NativeCapabilitiesChecks {
 static void Need(bool ok,string reason){if(!ok)throw new Exception(reason);}
 static string Hash(byte[] data){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(data)).Replace("-","").ToLowerInvariant();}
 static JObject Ins(string op,object value=null)=>new JObject{{"op",op},{"value",value==null?JValue.CreateNull():JToken.FromObject(value)}};
 static string Member(string name)=>"SpatialWallpaper.Sdk::Storage::"+name;
 static ScriptMessage Read(Stream reader,SessionIdentity id){var result=Task.Run(()=>ScriptWire.Read(reader,id,false));Need(result.Wait(10000),"worker deadline");return result.Result;}
 static SceneSnapshot Scene(long revision,long sequence)=>new SceneSnapshot(revision,sequence,new[]{new SceneObject(1,"node",0,new JObject{{"position",new JArray(0,0,0)}})});
 static void MainRun(string[] args){
  string worker=args[0],output=args[2];Directory.CreateDirectory(output);var seed=JObject.Parse(File.ReadAllText(args[1]));var inventory=JObject.Parse(File.ReadAllText(Path.Combine(worker,"build.json")));var files=((JObject)inventory["runtime_files"]).Properties().ToDictionary(p=>p.Name,p=>(string)p.Value);
  using(var journal=new Supervision.FaultJournal(Path.Combine(output,"journal")))using(var station=new Windows.PrivateStation()){
   var installation=Guid.NewGuid();var actor=new Broker.StorageBroker(Path.Combine(output,"store"),sharedProcess:true);var neighbor=new Broker.StorageBroker(Path.Combine(output,"store"),sharedProcess:true);actor.Initialized.Wait();neighbor.Initialized.Wait();
   try{
    var launcher=new Windows.ScriptProcessLauncher(station,new Windows.WorkerImage(worker,"ScriptWorker.exe",files),Path.Combine(output,"attempts"));
    foreach(bool view in new[]{true,false}){
     var program=(JObject)seed["program"].DeepClone();var content=(JObject)seed["content"].DeepClone();var cls=(JObject)program["classes"][0];
     if(view)cls["methods"][0]["body"]=new JArray(Ins("ldarg",0),Ins("ldfield","target"),Ins("call","SpatialWallpaper.Sdk::View::GetEye()->UnityEngine.Vector3"),Ins("call","UnityEngine.CoreModule::UnityEngine.Transform::set_localPosition(UnityEngine.Vector3)->System.Void"),Ins("ret"));
     else{
      ((JArray)cls["fields"]).Add(new JObject{{"id","request"},{"type","i32"}});content["nodes"][0]["components"][1]["fields"]["request"]=0;
      cls["methods"][0]["body"]=new JArray(Ins("ldarg",0),Ins("ldfield","request"),Ins("brtrue",10),Ins("ldarg",0),Ins("ldstr","score"),Ins("ldstr","42"),Ins("ldstr","0"),Ins("call",Member("BeginWriteText(System.String,System.String,System.String)->System.Int32")),Ins("stfield","request"),Ins("ret"),Ins("ldarg",0),Ins("ldfield","target"),Ins("ldarg",0),Ins("ldfield","request"),Ins("call",Member("State(System.Int32)->System.Int32")),Ins("conv.r4"),Ins("ldc.r4",0f),Ins("ldc.r4",0f),Ins("newvector3"),Ins("call","UnityEngine.CoreModule::UnityEngine.Transform::set_localPosition(UnityEngine.Vector3)->System.Void"),Ins("ret"));
     }
     byte[] bytes=NativeScriptDefinition.Encode(program,content);var admitted=NativeScriptDefinition.Admit(bytes);string path=Path.Combine(output,view?"view.json":"storage.json");File.WriteAllBytes(path,bytes);
     var identity=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);var id=new SessionIdentity(identity.Instance,1,SceneCommandCatalog.Digest);var receipt=journal.Begin(installation,admitted.BuildId,identity.Instance,1,Supervision.StartIntent.Manual);Need(receipt.Attempt!=identity.Attempt,"fixture receipt generated");identity=new WorkerIdentity(receipt.Attempt,identity.Instance,1);
     var session=new Broker.BrokerSession(receipt,false,new[]{"private-storage"});
     using(var process=launcher.Start(new Windows.ScriptLaunchRequest(identity,path,Hash(bytes),admitted.BuildId),session,actor))using(var reader=process.OpenReader())using(var writer=process.OpenWriter()){
      Need(Read(reader,id).Type=="hello","pure CLR handshake");ScriptWire.Write(writer,id,"start",new JObject{{"snapshot",ScriptWire.Snapshot(Scene(1,1))},{"root_nodes",new JArray(1)},{"resources",new JObject()}},true);
      long revision=1;bool ready=false;
      for(int tick=1;tick<=60;tick++){
       ScriptWire.Write(writer,id,"tick",ScriptWire.Tick(new TickInput(tick,Scene(revision,tick*2),view:new NativeViewSnapshot(.25f,.3f,-.8f,.6f,.3375f))),true);var message=Read(reader,id);Need(message.Type=="batch","author fault: "+message.Payload);var batch=ScriptWire.Batch(message.Payload,id);
       if(view){Need(batch.Commands.Count==1&&(float)batch.Commands[0].Arguments[0]==.25f&&(float)batch.Commands[0].Arguments[2]==-.8f,"real view snapshot delivered");ready=true;}
       else if(batch.Commands.Count>0){int state=(int)batch.Commands[0].Arguments[0];Need(state>=1&&state<=3,"storage async state "+state);ready=state==3;}
       if(SceneCommandProtocol.AdvancesRevision(batch))revision++;ScriptWire.Write(writer,id,"ack",ScriptWire.Ack(new BatchAck(id,tick,tick,true,Scene(revision,tick*2+1))),true);if(ready)break;Thread.Sleep(5);
      }
      Need(ready,"asynchronous capability did not finish");process.RequestStop();Need(process.Wait(2000)&&process.ExitCode==0,"worker normal stop");
     }
     if(!view){var fresh=new Broker.BrokerSession(journal.Begin(installation,admitted.BuildId,Guid.NewGuid(),2,Supervision.StartIntent.Manual),false,new[]{"private-storage"});var saved=neighbor.Read(fresh,"score").Result;Need(saved.Version==1&&System.Text.Encoding.UTF8.GetString(saved.Value)=="42","persisted actual script write across independent actors");Need(neighbor.Commit(fresh,0,new Dictionary<string,byte[]>{{"score",new byte[]{1}}}).Result.Failure==Broker.StorageFailure.Conflict,"CAS stale write rejected");fresh.Close();Need(neighbor.Commit(fresh,1,new Dictionary<string,byte[]>{{"score",new byte[]{1}}}).Result.Failure==Broker.StorageFailure.Closed,"revoked generation write rejected");var preview=new Broker.BrokerSession(journal.Begin(installation,admitted.BuildId,Guid.NewGuid(),1,Supervision.StartIntent.Manual),true,new[]{"private-storage"});Need(neighbor.Read(preview,"score").Result.Value==null,"preview isolation");}
    }
   }finally{actor.Close();neighbor.Close();actor.Completion.Wait();neighbor.Completion.Wait();}
  }
  Console.WriteLine("PASS actual LPAC native View, async script write, cross-actor persistence, CAS, revocation and preview isolation");
 }
 static int Main(string[] args){try{MainRun(args);return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
