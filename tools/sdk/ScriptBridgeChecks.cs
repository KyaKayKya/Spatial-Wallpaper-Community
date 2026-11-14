using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Linq;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Native;
using SpatialWallpaper.Sdk.Execution;
using SpatialWallpaper.Sdk.Capabilities;

static class ScriptBridgeChecks {
 static int checks;
 static void Yes(bool value,string label){if(!value)throw new Exception(label);checks++;}
 static void No(Action action,string label){try{action();}catch(Exception failure)when(failure is InvalidOperationException||failure is InvalidDataException||failure is ArgumentException){checks++;return;}throw new Exception("accepted:"+label);}
 sealed class Target:INativeCommandTarget {
  public int RenderFrames,Commits;public bool Ready=true;public Action Preparing;long revision=1,snapshotSequence;
  public SceneSnapshot Snapshot=>new SceneSnapshot(revision,++snapshotSequence,new[]{new SceneObject(1,"node")});
  public bool TryPrepare(CommandBatch batch,out object prepared){Preparing?.Invoke();prepared=batch;return Ready;}
  public void CancelPreparation(){}
  public BatchAck Commit(CommandBatch batch,object prepared){Commits++;if(SceneCommandProtocol.AdvancesRevision(batch))revision++;return new BatchAck(batch.Identity,batch.Tick,batch.Sequence,true,Snapshot);}
 }
 static void ScriptDefinition(string input,string legacy){
  var root=JObject.Parse(File.ReadAllText(input));byte[] bytes=NativeScriptDefinition.Encode((JObject)root["program"],(JObject)root["content"]);
  var admitted=NativeScriptDefinition.Admit(bytes);Yes(admitted.IsNativeScript,"program segment has explicit native origin");
  Yes(admitted.Content["resources"] is JArray resources&&resources.Count==0,"script segment contains no native art payloads");
  No(()=>NativeScriptDefinition.Admit(File.ReadAllBytes(legacy)),"old schema4 archive is not reinterpreted");
  No(()=>SpatialWallpaper.Sdk.Security.Admission.Admit(bytes,"windows-x86_64"),"native program segment is not a legacy archive");
  var packet=JObject.Parse(Encoding.UTF8.GetString(bytes));packet["command_abi"]=999;
  No(()=>NativeScriptDefinition.Admit(Encoding.UTF8.GetBytes(packet.ToString())),"script command ABI mismatch");
  var content=(JObject)root["content"].DeepClone();content["resources"]=new JArray(new JObject{{"id","art"},{"kind","mesh"},{"file","full-scene.smesh"}});
  No(()=>NativeScriptDefinition.Encode((JObject)root["program"],content),"script metadata cannot carry resource files");
  var showcase=(JObject)root["program"].DeepClone();var scene=(JObject)root["content"].DeepClone();
  ((JArray)showcase["classes"][0]["fields"]).Add(new JObject{{"id","lamp"},{"type","light"}});
  var second=(JObject)scene["nodes"][0].DeepClone();second["id"]="n1";((JArray)second["components"]).RemoveAt(1);((JArray)scene["nodes"]).Add(second);scene["nodes"][0]["components"][1]["fields"]["lamp"]=1;
  ((JArray)second["components"]).Add(new JObject{{"kind","light"},{"config",new JObject{{"type",2},{"color",new JArray(1,1,1,1)},{"intensity",1f},{"range",10f},{"spot",30f},{"innerSpot",20f},{"shadows",0},{"shadowStrength",1f},{"bias",.05f},{"normalBias",.4f},{"near",.2f},{"usePipelineSettings",true}}},{"fields",new JObject()},{"callbacks",new JArray()}});
  Func<string,object,JObject> ins=(op,value)=>new JObject{{"op",op},{"value",value==null?JValue.CreateNull():JToken.FromObject(value)}};
  showcase["classes"][0]["methods"][0]["body"]=new JArray(ins("ldarg",0),ins("ldfield","target"),ins("ldc.r4",0f),ins("ldc.r4",1f),ins("ldc.r4",0f),ins("call","UnityEngine.CoreModule::UnityEngine.Transform::Rotate(System.Single,System.Single,System.Single)->System.Void"),ins("ldarg",0),ins("ldfield","lamp"),ins("ldc.r4",2.5f),ins("call","UnityEngine.CoreModule::UnityEngine.Light::set_intensity(System.Single)->System.Void"),ins("ret",null));
  byte[] showcaseBytes=NativeScriptDefinition.Encode(showcase,scene);Yes(NativeScriptDefinition.Admit(showcaseBytes).IsNativeScript,"Showcase original light/object program uses existing admitted members");File.WriteAllBytes(Path.Combine(Path.GetDirectoryName(input),"showcase-light-object.native-script.json"),showcaseBytes);
 }
 static void OrderedInputs(){
  var downs=new uint[]{1,1,0,0,0,0,0};var ups=(uint[])downs.Clone();
  var records=new[]{new OrderedInputEvent(1,1000,20,2,2,0,0),new OrderedInputEvent(2,1000,20,2,0,0,120),new OrderedInputEvent(3,1000,20,3,1,0,0),new OrderedInputEvent(4,1000,20,3,0,0,-120),new OrderedInputEvent(5,1000,20,1,0,2,0),new OrderedInputEvent(6,1000,20,0,0,1,0)};
  var batch=new OrderedInputBatch(InputSnapshotCodec.Encode(20,1000,true,0,100,200,0,downs,ups),records,6);
  var ordered=new NativeInputQueue();ordered.PublishOrdered(batch);ordered.PublishOrdered(batch);
  var capacity=new NativeInputQueue();var full=Enumerable.Range(1,256).Select(index=>new OrderedInputEvent(index,1000,40,(index&1)==1?4u:0u,(index&1)==1?4u:0u,(index&1)==0?4u:0u,0)).ToArray();var fullDown=new uint[]{0,0,128,0,0,0,0};
  capacity.PublishOrdered(new OrderedInputBatch(InputSnapshotCodec.Encode(40,1000,true,0,100,200,0,fullDown,fullDown),full,256));Yes(capacity.PendingOrderedEventCount==256,"actual native ordered queue retains all 256 events");
  var overDown=(uint[])fullDown.Clone();overDown[2]++;No(()=>capacity.PublishOrdered(new OrderedInputBatch(InputSnapshotCodec.Encode(40,1001,true,4,100,200,0,overDown,fullDown),new[]{new OrderedInputEvent(257,1001,40,4,4,0,0)},257)),"257th ordered event rejects before any queue mutation");Yes(capacity.PendingOrderedEventCount==256&&capacity.RejectedOrderedEventCount==257&&capacity.Take().Events.Single().Button=="Space","ordered overflow diagnostics prove capacity not cursor gap and preserve old queue");
  string[] kinds={"down","wheel","down","wheel","up","up"};string[] buttons={"Secondary",null,"Primary",null,"Secondary","Primary"};float[] wheels={0,1,0,-1,0,0};
  for(int i=0;i<6;i++){var next=ordered.Take();Yes(next.Events.Length==1&&next.Events[0].Id==i+1&&next.Events[0].Kind==kinds[i]&&next.Events[0].Button==buttons[i]&&next.Events[0].Value==wheels[i],"ordered cross-button and opposite wheel event "+i);}
  Yes(ordered.Take().Events.Length==0,"duplicate ordered packet cannot replay edges");
  No(()=>ordered.Publish(InputSnapshotCodec.Encode(21,1000,false,0,0,0,0,new uint[7],new uint[7])),"cannot mix cumulative and ordered input modes");
  var cancelled=new NativeInputQueue();cancelled.PublishOrdered(batch);cancelled.Take();cancelled.Take();
  cancelled.PublishOrdered(new OrderedInputBatch(InputSnapshotCodec.Encode(21,1000,false,0,0,0,0,new uint[7],new uint[7]),new OrderedInputEvent[0],6));Yes(cancelled.Take().Events.Length==0,"epoch revocation cancels unassigned ordered events");
  var nextDown=new uint[]{1,0,0,0,0,0,0};cancelled.PublishOrdered(new OrderedInputBatch(InputSnapshotCodec.Encode(22,1000,true,1,100,200,0,nextDown,new uint[7]),new[]{new OrderedInputEvent(7,1000,22,1,1,0,0)},7));
  var fresh=cancelled.Take();Yes(fresh.Events.Length==1&&fresh.Events[0].Id==3&&fresh.Events[0].Button=="Primary","cancelled source IDs do not make gaps in assigned Tick IDs");
  var intact=new NativeInputQueue();intact.PublishOrdered(batch);var corrupt=(uint[])downs.Clone();corrupt[2]=1;
  No(()=>intact.PublishOrdered(new OrderedInputBatch(InputSnapshotCodec.Encode(20,1001,true,0,100,200,0,corrupt,ups),new OrderedInputEvent[0],6)),"missing ordered counter edge rejected");
  Yes(intact.Take().Events.Single().Button=="Secondary","rejected ordered packet has zero partial queue effect");
  // Event capture time and current authority evidence are different. Continuous
  // real source heartbeats may retain accepted edges across VM backpressure;
  // reading/dequeueing without such evidence must never manufacture freshness.
  var oldEdge=new[]{new OrderedInputEvent(1,100,30,1,1,0,0)};var held=new uint[]{1,0,0,0,0,0,0};
  var retained=new NativeInputQueue();retained.PublishOrdered(new OrderedInputBatch(InputSnapshotCodec.Encode(30,1000,true,1,100,200,0,held,new uint[7]),oldEdge,1));
  var delivery=retained.Take();var frames=new InputFrames();frames.Advance(delivery.Snapshot,1000,true);
  Yes(frames.GetButtonDown("Primary")&&oldEdge[0].Time==100&&BitConverter.ToInt64(delivery.Snapshot,16)==1000,"current real authority can authorize previously accepted queued edge without rewriting its capture time");
  var stale=new NativeInputQueue();stale.PublishOrdered(new OrderedInputBatch(InputSnapshotCodec.Encode(30,100,true,1,100,200,0,held,new uint[7]),oldEdge,1));var expired=stale.Take();frames=new InputFrames();frames.Advance(expired.Snapshot,1000,true);
  Yes(!frames.Focused&&!frames.GetButtonDown("Primary")&&BitConverter.ToInt64(expired.Snapshot,16)==100,"dequeue cannot renew stale source evidence");
  var paused=new NativeInputQueue();paused.PublishOrdered(batch);paused.Revoke();Yes(paused.CurrentSnapshot==null&&paused.Take().Events.Length==0,"local pause immediately removes authority and unsent events");
  paused.PublishOrdered(batch);Yes(paused.CurrentSnapshot==null&&paused.Take().Events.Length==0,"late same-epoch source packet cannot undo local pause");
  paused.PublishOrdered(new OrderedInputBatch(InputSnapshotCodec.Encode(22,1000,true,1,100,200,0,nextDown,new uint[7]),new[]{new OrderedInputEvent(7,1000,22,1,1,0,0)},7));Yes(paused.Take().Events.Length==1,"new broker epoch restores input after local pause");
 }
 static void DisconnectedInputs(){
  using(var domain=new OwnedDomain(Guid.NewGuid(),1)){
   var binding=new NativeInputBindings(domain,()=>1000);var scene=new SceneSnapshot(1,1,new[]{new SceneObject(1,"node")});
   string held="SpatialWallpaper.Sdk::Input::GetButton(System.String)->System.Boolean";
   byte[] live=InputSnapshotCodec.Encode(1,1000,true,1,100,200,0,new uint[]{1,0,0,0,0,0,0},new uint[7]);
   binding.Advance(new TickInput(1,scene,inputs:live));Yes((bool)binding.Call(held,null,new object[]{"Primary"}),"held fixture initially focused");
   binding.Advance(new TickInput(2,scene));Yes(!(bool)binding.Call(held,null,new object[]{"Primary"}),"empty native input tick immediately clears held authority");
   binding.Advance(new TickInput(3,scene,inputs:live));Yes(!(bool)binding.Call(held,null,new object[]{"Primary"}),"late old epoch cannot restore held after empty tick");
   binding.Advance(new TickInput(4,scene,inputs:InputSnapshotCodec.Encode(2,1000,true,1,100,200,0,new uint[]{1,0,0,0,0,0,0},new uint[7])));Yes((bool)binding.Call(held,null,new object[]{"Primary"}),"fresh broker epoch restores held after empty tick");
  }
 }
 static int Main(string[] args){
  ScriptDefinition(args[0],args[1]);
  OrderedInputs();
  DisconnectedInputs();
  using(var domain=new OwnedDomain(Guid.NewGuid(),1)){
   long now=1000;var binding=new NativeInputBindings(domain,()=>now);var scene=new SceneSnapshot(1,1,new[]{new SceneObject(1,"node")});var downs=new uint[7];var ups=new uint[7];
   string down="SpatialWallpaper.Sdk::Input::GetButtonDown(System.String)->System.Boolean",focused="SpatialWallpaper.Sdk::Input::IsFocused()->System.Boolean";
   int delivered=0;for(int i=1;i<=10;i++){downs[0]++;var packet=InputSnapshotCodec.Encode(1,now,true,0,100,200,0,downs,ups);binding.Advance(new TickInput(i,scene,inputs:packet));if((bool)binding.Call(down,null,new object[]{"Primary"}))delivered++;}
   Yes(delivered==10,"ten separately delivered cumulative prefixes remain ten input edges");
   binding.Advance(new TickInput(11,scene,inputs:InputSnapshotCodec.Encode(2,now,false,0,0,0,0,new uint[7],new uint[7])));Yes(!(bool)binding.Call(focused,null,new object[0]),"native script input revocation");
   binding.Advance(new TickInput(12,scene,inputs:InputSnapshotCodec.Encode(1,now,true,0,100,200,0,downs,ups)));Yes(!(bool)binding.Call(down,null,new object[]{"Primary"}),"native input old epoch cannot replay edges");
   binding.Advance(new TickInput(13,scene,inputs:InputSnapshotCodec.Encode(3,now-300,true,0,100,200,0,downs,ups)));Yes(!(bool)binding.Call(focused,null,new object[0]),"native input stale derived sample clears focus");
   var queue=new NativeInputQueue();downs[0]=ups[0]=10;queue.Publish(InputSnapshotCodec.Encode(4,now,true,0,100,200,0,downs,ups));int drained=0;long lastId=0;
   for(int i=0;i<10;i++){var delivery=queue.Take();foreach(var edge in delivery.Events)Yes(edge.Id==++lastId,"queued event IDs contiguous");binding.Advance(new TickInput(14+i,scene,inputs:delivery.Snapshot,events:delivery.Events));if((bool)binding.Call(down,null,new object[]{"Primary"}))drained++;}
   Yes(drained==10&&queue.Take().Events.Length==0,"ten edges received while waiting for ack drain exactly once");
   var canceled=new NativeInputQueue();canceled.Publish(InputSnapshotCodec.Encode(5,now,true,0,100,200,0,downs,ups));canceled.Publish(InputSnapshotCodec.Encode(6,now,false,0,0,0,0,new uint[7],new uint[7]));Yes(canceled.Take().Events.Length==0,"revocation cancels old unsent event prefixes");
   downs[0]=257;No(()=>new NativeInputQueue().Publish(InputSnapshotCodec.Encode(7,now,true,0,100,200,0,downs,new uint[7])),"input handoff event capacity");
  }
  var mappingIdentity=new SessionIdentity(Guid.NewGuid(),1,SceneCommandCatalog.Digest);
  var template=Enumerable.Range(1,256).Select(i=>new SceneObject(i,"node",i==1?0:1)).ToArray();
  var initial=new SceneSnapshot(1,1,new[]{new SceneObject(1,"prefab",0,null,template),new SceneObject(2,"prefab",0,null,template)});
  var two=new CommandBatch(mappingIdentity,1,1,1,new[]{new SceneCommand("Instantiate",1,new JArray(Enumerable.Range(1,256).Select(i=>-i))),new SceneCommand("Instantiate",2,new JArray(Enumerable.Range(257,256).Select(i=>-i)))});
  SceneCommandProtocol.Validate(two,mappingIdentity,initial,1,1);
  var map=Enumerable.Range(1,512).ToDictionary(i=>-(long)i,i=>(long)i+2);
  var confirmed=new BatchAck(mappingIdentity,1,1,true,new SceneSnapshot(2,2,new[]{new SceneObject(1,"prefab",0,null,template),new SceneObject(2,"prefab",0,null,template)}.Concat(Enumerable.Range(3,512).Select(i=>new SceneObject(i,"node")))),map);
  using(var wire=new MemoryStream()){ScriptWire.Write(wire,mappingIdentity,"ack",ScriptWire.Ack(confirmed),true);wire.Position=0;var message=ScriptWire.Read(wire,mappingIdentity,true);Yes(ScriptWire.Ack(message.Payload,mappingIdentity).Mapping.Count==512,"two legal prefab operations may acknowledge more than 256 nodes");}
  var identity=new SessionIdentity(Guid.NewGuid(),1,SceneCommandCatalog.Digest);var target=new Target();
  var preparingTarget=new Target();using(var preparingPump=new ScriptBridgePump(identity,preparingTarget)){
   var live=InputSnapshotCodec.Encode(1,1000,true,1,100,200,0,new uint[7],new uint[7]);
   preparingPump.PublishInputAuthority(live,1000);
   preparingPump.MarkTickIssued(new TickInput(1,preparingTarget.Snapshot,inputs:live));
   preparingPump.TryEnqueue(new CommandBatch(identity,1,1,1,new[]{new SceneCommand("SetPosition",1,new JArray(3,0,0))}));
   preparingTarget.Preparing=()=>preparingPump.PublishInputAuthority(null,1000);
   preparingPump.Pump();Yes(preparingTarget.Commits==0&&preparingPump.Stopped,"final input authorization follows preparation and precedes native commit");
  }
  var unguardedTarget=new Target();using(var unguarded=new ScriptBridgePump(identity,unguardedTarget)){
   unguarded.MarkTickIssued(new TickInput(1,unguardedTarget.Snapshot,inputs:InputSnapshotCodec.Encode(1,1000,true,1,100,200,0,new uint[7],new uint[7])));
   unguarded.TryEnqueue(new CommandBatch(identity,1,1,1,new[]{new SceneCommand("SetPosition",1,new JArray(3,0,0))}));unguarded.Pump();
   Yes(unguardedTarget.Commits==0&&unguarded.Stopped,"issued focused-input batch requires current native authority before commit");
  }
  foreach(string mode in new[]{"current","revoked","new-epoch","expired-tick","expired-source","future-source","regressed-source"}){
   var guardedTarget=new Target{Ready=false};using(var guarded=new ScriptBridgePump(identity,guardedTarget)){
    var issuedInput=InputSnapshotCodec.Encode(1,1000,true,1,100,200,0,new uint[7],new uint[7]);guarded.PublishInputAuthority(issuedInput,1000);
    guarded.MarkTickIssued(new TickInput(1,guardedTarget.Snapshot,inputs:issuedInput));guarded.TryEnqueue(new CommandBatch(identity,1,1,1,new[]{new SceneCommand("SetPosition",1,new JArray(3,0,0))}));guarded.Pump();
    Yes(guardedTarget.Commits==0&&!guarded.Stopped,"input preparation can remain pending:"+mode);
    long now=mode=="expired-tick"?1300:1100,stamp=mode=="expired-tick"?1300:mode=="expired-source"?700:mode=="future-source"?1200:mode=="regressed-source"?999:1100;
    bool focused=mode!="revoked";ulong epoch=mode=="new-epoch"?2ul:1ul;
    guarded.PublishInputAuthority(InputSnapshotCodec.Encode(epoch,stamp,focused,focused?1u:0u,focused?(ushort)100:(ushort)0,focused?(ushort)200:(ushort)0,0,new uint[7],new uint[7]),now);
    guardedTarget.Ready=true;guarded.Pump();bool accepted=mode=="current";
    Yes(guardedTarget.Commits==(accepted?1:0)&&guarded.Stopped!=accepted,"pending issued batch checks current input authority:"+mode);
    Yes(guarded.TryTakeAck(out var guardedAck)&&guardedAck.Accepted==accepted,"input authorization result ack:"+mode);
   }
  }
  using(var pump=new ScriptBridgePump(identity,target)){
   for(int i=0;i<100;i++){target.RenderFrames++;pump.Pump();}
   Yes(target.RenderFrames==100&&target.Commits==0,"render does not wait for absent script");
   var batch=new CommandBatch(identity,1,1,1,new[]{new SceneCommand("SetPosition",1,new JArray(3,0,0))});
   Yes(pump.TryEnqueue(batch)==QueueResult.NoTick,"cannot submit before a tick is published");
   pump.MarkTickIssued(new TickInput(1,target.Snapshot));
   Yes(pump.TryEnqueue(batch)==QueueResult.Accepted,"one queued batch");
   Yes(pump.TryEnqueue(batch)==QueueResult.Full,"second batch cannot queue");
   target.Ready=false;
   for(int i=0;i<100;i++){target.RenderFrames++;pump.Pump();}
   Yes(target.Commits==0&&target.RenderFrames==200,"resource preparation never blocks rendering");
   target.Ready=true;pump.Pump();Yes(target.Commits==1,"prepared batch commits once");
   Yes(pump.TryTakeAck(out var ack)&&ack.Accepted,"actual ack returned asynchronously");
   Yes(pump.TryEnqueue(batch)==QueueResult.Full,"taking ack does not bypass delivery backpressure");pump.ConfirmAckDelivered(ack.Sequence);
   pump.Pump();Yes(target.Commits==1,"batch not replayed");
   pump.Stop();Yes(pump.TryEnqueue(batch)==QueueResult.Stopped,"stopped session rejects late commands");
  }
  Console.WriteLine("script bridge pure checks passed: "+checks);return 0;
 }
}
