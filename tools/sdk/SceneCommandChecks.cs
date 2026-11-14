using System;
using System.Linq;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Execution;
using SpatialWallpaper.Sdk.Native;
using SpatialWallpaper.Sdk.Security;

static class SceneCommandChecks {
 static int checks;
 static void Yes(bool value,string name){if(!value)throw new Exception(name);checks++;}
 static void No(Action action,string name){try{action();}catch(Exception e)when(e is InvalidOperationException||e is System.IO.InvalidDataException||e is ArgumentException){checks++;return;}throw new Exception("accepted:"+name);}
 static readonly SessionIdentity Identity=new SessionIdentity(Guid.NewGuid(),1,SceneCommandCatalog.Digest);
 static SceneSnapshot Snapshot(long sequence=1,long revision=1)=>new SceneSnapshot(revision,sequence,new[]{
  new SceneObject(1,"node",0,new JObject{{"position",new JArray(1,0,0)}}),
  new SceneObject(2,"light",1,new JObject{{"position",new JArray(0,2,0)},{"intensity",2}}),
  new SceneObject(3,"sound"),new SceneObject(4,"material"),
  new SceneObject(5,"prefab",0,null,new[]{new SceneObject(10,"node"),new SceneObject(11,"light",10)})});
 static SceneCommand Position(long id,float x)=>new SceneCommand("SetPosition",id,new JArray(x,0,0));
 static CommandBatch Batch(IEnumerable<SceneCommand> commands,SessionIdentity identity=null,long sequence=1,long revision=1)=>new CommandBatch(identity??Identity,1,revision,sequence,commands);
 static void Validate(CommandBatch batch,SceneSnapshot snapshot=null)=>SceneCommandProtocol.Validate(batch,Identity,snapshot??Snapshot(),1,1);
 static byte[] Pad(byte[] packet,int payload){var result=new byte[SceneCommandProtocol.HeaderBytes+payload];Array.Copy(packet,result,packet.Length);for(int i=packet.Length;i<result.Length;i++)result[i]=32;Array.Copy(BitConverter.GetBytes(payload),0,result,SceneCommandProtocol.HeaderBytes-4,4);return result;}
 static byte[] Payload(byte[] packet,string text){byte[] payload=Encoding.UTF8.GetBytes(text);var result=new byte[SceneCommandProtocol.HeaderBytes+payload.Length];Array.Copy(packet,result,SceneCommandProtocol.HeaderBytes);Array.Copy(BitConverter.GetBytes(payload.Length),0,result,SceneCommandProtocol.HeaderBytes-4,4);Array.Copy(payload,0,result,SceneCommandProtocol.HeaderBytes,payload.Length);return result;}
 static void Protocol(){
  var valid=Batch(new[]{Position(1,3)});Validate(SceneCommandProtocol.Decode(SceneCommandProtocol.Encode(valid),Identity));
  foreach(int count in new[]{255,256})Validate(Batch(Enumerable.Range(0,count).Select(i=>new SceneCommand("PlayEffect",3,new JArray(1)))));
  No(()=>SceneCommandProtocol.Encode(Batch(Enumerable.Range(0,257).Select(i=>Position(1,i)))),"257 operations");
  var packet=SceneCommandProtocol.Encode(valid);
  foreach(int size in new[]{65535,65536})Validate(SceneCommandProtocol.Decode(Pad(packet,size),Identity));
  No(()=>SceneCommandProtocol.Decode(Pad(packet,65537),Identity),"64KiB+1");
  var bad=(byte[])packet.Clone();bad[4]=99;No(()=>SceneCommandProtocol.Decode(bad,Identity),"ABI");
  No(()=>SceneCommandProtocol.Encode(Batch(new[]{new SceneCommand("Unknown",1,new JArray())})),"unknown operation");
  No(()=>SceneCommandProtocol.Encode(Batch(new[]{Position(1,float.NaN)})),"nonfinite");
  No(()=>SceneCommandProtocol.Decode(packet.Take(packet.Length-1).ToArray(),Identity),"truncated");
  No(()=>SceneCommandProtocol.Decode(packet.Concat(new byte[]{0}).ToArray(),Identity),"trailing bytes");
  No(()=>Validate(Batch(new[]{Position(999,1)})),"unknown reference");
  No(()=>Validate(Batch(new[]{Position(3,1)})),"reference kind");
  No(()=>Validate(Batch(new[]{Position(1,1)},new SessionIdentity(Guid.NewGuid(),1,SceneCommandCatalog.Digest))),"foreign instance");
  No(()=>Validate(Batch(new[]{Position(1,1)},new SessionIdentity(Identity.Instance,2,SceneCommandCatalog.Digest))),"generation");
  No(()=>Validate(Batch(new[]{Position(1,1)},new SessionIdentity(Identity.Instance,1,new string('0',64)))),"catalog");
  No(()=>Validate(Batch(new[]{Position(1,1)},sequence:2)),"out of order");
  No(()=>Validate(Batch(new[]{Position(1,1)},revision:2)),"scene revision conflict");
  Validate(valid,Snapshot(100)); // render/animation frame progress is not a structural conflict.
  Validate(Batch(new[]{new SceneCommand("Instantiate",5,new JArray(-1,-2)),Position(-2,4)}));
  No(()=>Validate(Batch(new[]{Position(-2,4),new SceneCommand("Instantiate",5,new JArray(-1,-2))})),"forward temporary ref");
  No(()=>Validate(Batch(new[]{new SceneCommand("Instantiate",5,new JArray(-1,-1))})),"duplicate temporary id");
  No(()=>Validate(Batch(new[]{new SceneCommand("Destroy",1,new JArray()),Position(2,3)})),"destroyed descendant");
  No(()=>SceneCommandProtocol.Validate(valid,Identity,Snapshot(),2,2),"duplicate/replayed batch");
  No(()=>SceneCommandProtocol.Decode(packet,new SessionIdentity(Guid.NewGuid(),1,SceneCommandCatalog.Digest)),"foreign wire session");
  No(()=>SceneCommandProtocol.Decode(Payload(packet,"{\"commands\":[],\"commands\":[]}"),Identity),"duplicate JSON key");
  No(()=>SceneCommandProtocol.Decode(Payload(packet,"{\"commands\":[{\"op\":999,\"target\":1,\"args\":[]}]}"),Identity),"unknown wire opcode");
  No(()=>SceneCommandProtocol.Decode(Payload(packet,"{\"commands\":[{\"op\":1,\"target\":1,\"args\":[NaN,0,0]}]}"),Identity),"wire NaN");
  No(()=>SceneCommandProtocol.Encode(Batch(new[]{new SceneCommand("SetRotation",1,new JArray(0,0,0,0))})),"unnormalized quaternion");
  No(()=>SceneCommandProtocol.Encode(Batch(new[]{new SceneCommand("SetScale",1,new JArray(20000,1,1))})),"scale range");
  var args=new JArray(1,2,3);var immutable=new SceneCommand("SetPosition",1,args);args[0]=999;immutable.Arguments[0]=999;
  Yes((int)immutable.Arguments[0]==1,"command args are immutable values");
  var source=Snapshot();source.Objects[0].State["position"]=new JArray(999,0,0);Yes((int)source.Objects[0].State["position"][0]==1,"snapshot state immutable");
  var full=new SceneSnapshot(1,1,Enumerable.Range(1,SceneCommandCatalog.MaxObjects-1).Select(i=>new SceneObject(i,"node")).Concat(new[]{new SceneObject(20000,"prefab",0,null,new[]{new SceneObject(30000,"node")})}));
  No(()=>Validate(Batch(new[]{new SceneCommand("Instantiate",20000,new JArray(-1))}),full),"wire instantiate cannot exceed total object limit");
  No(()=>Validate(new CommandBatch(Identity,1,1,1,new SceneCommand[0],10)),"event watermark cannot consume unpublished input");
 }
 static void Mirror(){
  var domain=new OwnedDomain(Identity.Instance,Identity.Generation);
  using(var host=new MirrorVmHost(domain,Identity,Snapshot())){
   var node=(OwnedHandle)host.Resolve("node",new JValue(0),0);
   var child=(OwnedHandle)host.Resolve("node",new JValue(1),0);
   var sound=(OwnedHandle)host.ResolveId(3,"sound");
   host.BeginTick(new TickInput(1,Snapshot()));
   for(int i=0;i<10;i++)host.SetPosition(node,new VectorValue("vector3",i,0,0));
   Yes(host.GetPosition(node).X==9,"same tick read writes");
   Yes(host.GetWorldPosition(child).X==9&&host.GetWorldPosition(child).Y==2,"parent child transform");
   host.ObserveSnapshot(Snapshot(20));
   Yes(host.GetPosition(node).X==9,"pending snapshot cannot clobber local writes");
   for(int i=0;i<10;i++)host.PlayEffect(sound,1);
   var first=host.EndTick();
   Yes(first.Commands.Count==11&&first.Commands[0].Operation=="SetPosition","10 states coalesce; 10 events retained");
   No(()=>host.BeginTick(new TickInput(2,Snapshot(21))),"one unacked tick");
   No(()=>host.EndTick(),"one unacked batch");
   var fresh=Snapshot(21);fresh=new SceneSnapshot(2,21,fresh.Objects.Select(o=>o.Id==1?new SceneObject(1,"node",0,new JObject{{"position",new JArray(9,0,0)}}):o));
   host.ApplyAck(new BatchAck(Identity,1,1,true,fresh));
   No(()=>host.ApplyAck(new BatchAck(Identity,1,1,true,fresh)),"duplicate ack");
   No(()=>host.BeginTick(new TickInput(2,Snapshot(19))),"old snapshot cannot start next tick");
   host.BeginTick(new TickInput(2,fresh));
   var template=(OwnedHandle)host.ResolveId(5,"prefab");
   var born=host.Instantiate(template);
   host.SetPosition(born,new VectorValue("vector3",7,0,0));
   Yes(host.GetPosition(born).X==7,"tentative read writes");
   var second=host.EndTick();Validate(new CommandBatch(Identity,1,2,1,second.Commands),fresh);
   var ids=(JArray)second.Commands[0].Arguments;
   var mapping=new Dictionary<long,long>{{(long)ids[0],101},{(long)ids[1],102}};
   var committed=new SceneSnapshot(3,22,fresh.Objects.Concat(new[]{new SceneObject(101,"node",0,new JObject{{"position",new JArray(7,0,0)}}),new SceneObject(102,"light",101)}));
   host.ApplyAck(new BatchAck(Identity,2,2,true,committed,mapping));
   host.BeginTick(new TickInput(3,committed));Yes(host.GetPosition(born).X==7,"ack maps tentative handle");
   host.Destroy(born);No(()=>host.GetPosition(born),"destroyed read");
   var third=host.EndTick();
   host.ApplyAck(new BatchAck(Identity,3,3,false,Snapshot(23,2)));
   Yes(host.Stopped,"revision rejection stops session");
   No(()=>host.BeginTick(new TickInput(4,Snapshot(24,2))),"no replay after conflict");
   host.ApplyAck(new BatchAck(Identity,3,3,true,committed,mapping));Yes(host.Stopped,"late ack cannot resurrect");
   No(()=>domain.List("i32",new object[0]),"VM fields and collections domain stopped");
  }
  using(var foreignDomain=new OwnedDomain(Guid.NewGuid(),1))using(var host=new MirrorVmHost(new OwnedDomain(Identity.Instance,1),Identity,Snapshot())){
   host.BeginTick(new TickInput(1,Snapshot()));
   No(()=>host.GetPosition(foreignDomain.Register("node",new object())),"foreign owned VM handle");
   var node=(OwnedHandle)host.ResolveId(1,"node");host.Rotate(node,0,0,90);
   var child=(OwnedHandle)host.ResolveId(2,"node");var point=host.GetWorldPosition(child);
   Yes(Math.Abs(point.X+1)<.0001&&Math.Abs(point.Y)<.0001,"parent quaternion transforms child");
   host.PlayEffect((OwnedHandle)host.ResolveId(3,"sound"),1);host.SetPosition(node,new VectorValue("vector3",5,0,0));
   var batch=host.EndTick();Yes(batch.Commands.Count==3&&batch.Commands[1].Operation=="PlayEffect","event is a state coalescing barrier");
   No(()=>host.ApplyAck(new BatchAck(Identity,1,99,true,Snapshot(2))),"out-of-order ack stops session");Yes(host.Stopped,"bad ack cannot continue VM");
  }
  using(var host=new MirrorVmHost(new OwnedDomain(Identity.Instance,1),Identity,Snapshot())){
   host.BeginTick(new TickInput(1,Snapshot()));var prefab=(OwnedHandle)host.ResolveId(5,"prefab");var born=host.Instantiate(prefab);host.EndTick();
   No(()=>host.ApplyAck(new BatchAck(Identity,1,1,true,Snapshot(2,2))),"missing tentative mapping");
   Yes(host.Stopped,"failed tentative mapping stops all references");
  }
  var program=new JObject{{"classes",new JArray(new JObject{{"methods",new JArray(new JObject{{"body",new JArray(new JObject{{"op","call"},{"value","UnityEngine.CoreModule::UnityEngine.Mesh::Clear()->System.Void"}})}})}})}};
  No(()=>NativeProgramBindings.Require(program,member=>true),"native export rejects uncovered rendering even with broad capability delegate");
  program["classes"][0]["methods"][0]["body"][0]["value"]="UnityEngine.CoreModule::UnityEngine.Transform::get_localPosition()->UnityEngine.Vector3";
  NativeProgramBindings.Require(program);Yes(true,"native export accepts covered member");
  using(var host=new MirrorVmHost(new OwnedDomain(Identity.Instance,1),Identity,Snapshot())){
   host.BeginTick(new TickInput(1,Snapshot()));
   var node=(OwnedHandle)host.ResolveId(1,"node");
   long query=host.BeginQuery(node,"bounds");Yes(!host.TryTakeQuery(query,out var value),"native query is explicitly pending");
   var batch=host.EndTick();host.ApplyAck(new BatchAck(Identity,1,1,true,Snapshot(2),null,new Dictionary<long,JToken>{{query,new JObject{{"status","ok"},{"value",new JArray(1,2,3,4,5,6)}}}}));
   Yes(host.TryTakeQuery(query,out value)&&value is JArray,"native query ack result");
   Yes(!host.Supports("UnityEngine.CoreModule::UnityEngine.Mesh::Clear()->System.Void"),"uncovered member export rejected");
  }
 }
 static void FailureBoundaries(){
  using(var host=new MirrorVmHost(new OwnedDomain(Identity.Instance,1),Identity,Snapshot())){
   var clicks=Enumerable.Range(1,10).Select(id=>new TickEvent(id,"down","Primary")).ToArray();
   host.BeginTick(new TickInput(1,Snapshot(),.02f,null,clicks));Yes(host.Events.Count==10&&host.DeltaTime==.02f,"ten queued clicks remain ten events");
   var first=host.EndTick();Yes(first.ConsumedEvent==10,"batch acknowledges exact event frontier");
   SceneCommandProtocol.Validate(SceneCommandProtocol.Decode(SceneCommandProtocol.Encode(first),Identity),Identity,Snapshot(),1,1,0,10);
   host.ApplyAck(new BatchAck(Identity,1,1,true,Snapshot(2)));
   host.BeginTick(new TickInput(2,Snapshot(2),.02f,null,clicks));Yes(host.Events.Count==0,"cumulative input queue does not replay acknowledged clicks");host.EndTick();
   host.ApplyAck(new BatchAck(Identity,2,2,true,Snapshot(3)));
   No(()=>host.BeginTick(new TickInput(3,Snapshot(3),.02f,null,new[]{new TickEvent(12,"down","Primary")})),"input saturation/gap cannot silently lose click");Yes(host.Stopped,"missing input stops session explicitly");
  }
  No(()=>new TickInput(1,Snapshot(),float.NaN),"nonfinite delta time");
  No(()=>new TickInput(1,Snapshot(),.3f),"unbounded tick time");
  No(()=>new TickInput(1,Snapshot(),.01f,new byte[96]),"invalid input payload does not pass by size alone");
  var input=SpatialWallpaper.Sdk.Capabilities.InputSnapshotCodec.Encode(1,1,false,0,0,0,0,new uint[7],new uint[7]);
  var immutableInput=new TickInput(1,Snapshot(),.01f,input);input[0]=99;immutableInput.Inputs[0]=99;Yes(immutableInput.Inputs[0]==1,"input frame is immutable and shares existing codec");
  using(var host=new MirrorVmHost(new OwnedDomain(Identity.Instance,1),Identity,Snapshot())){
   host.BeginTick(new TickInput(1,Snapshot()));var node=(OwnedHandle)host.ResolveId(1,"node");
   long ray=host.BeginQuery(node,"raycast",new JArray(new JArray(0,0,0),new JArray(0,0,1),100));
   long animation=host.BeginQuery(node,"animator_state",new JArray(0));
   Yes(host.QueryState(ray)==NativeQueryState.Pending&&host.QueryState(animation)==NativeQueryState.Pending,"ray and animator never fabricate synchronous results");host.EndTick();
   var results=new Dictionary<long,JToken>{{ray,new JObject{{"status","ok"},{"value",new JObject{{"hit",true},{"node",2},{"point",new JArray(0,0,4)},{"normal",new JArray(0,0,-1)},{"distance",4}}}}},
    {animation,new JObject{{"status","error"},{"error","unavailable"}}}};
   host.ApplyAck(new BatchAck(Identity,1,1,true,Snapshot(2),null,results));
   Yes(host.QueryState(ray)==NativeQueryState.Ready&&host.QueryState(animation)==NativeQueryState.Failed&&host.QueryError(animation)=="unavailable","query failure is explicit");
   Yes(host.TryTakeQuery(ray,out var hit)&&(bool)hit["hit"]&&(long)hit["node"]==2,"ray result belongs to owned scene");host.ReleaseQuery(animation);
  }
  No(()=>SceneCommandRules.CheckQueryArguments("raycast",new JArray(new JArray(0,0,0),new JArray(0,0,0),1)),"zero ray direction");
  No(()=>SceneCommandRules.CheckQueryResult("raycast",new JArray(new JArray(0,0,0),new JArray(0,0,1),10),new JObject{{"status","ok"},{"value",new JObject{{"hit",true},{"node",999},{"point",new JArray(0,0,1)},{"normal",new JArray(0,0,-1)},{"distance",1}}}},Snapshot()),"query cannot return foreign scene reference");
  SceneCommandRules.CheckQueryResult("animator_state",new JArray(0),new JObject{{"status","ok"},{"value",new JObject{{"state_hash",-123},{"normalized_time",2.25},{"in_transition",false}}}},Snapshot());Yes(true,"actual animator result has explicit typed fields");
  using(var host=new MirrorVmHost(new OwnedDomain(Identity.Instance,1),Identity,Snapshot())){
   host.BeginTick(new TickInput(1,Snapshot()));var sound=(OwnedHandle)host.ResolveId(3,"sound");
   for(int i=0;i<256;i++)host.PlayEffect(sound,1);
   No(()=>host.PlayEffect(sound,1),"257th event never commits a partial batch");Yes(host.Stopped,"overflow stops speculative VM state");
   No(()=>host.EndTick(),"overflowed tick cannot publish events");
  }
  using(var host=new MirrorVmHost(new OwnedDomain(Identity.Instance,1),Identity,Snapshot())){
   host.BeginTick(new TickInput(1,Snapshot()));var node=(OwnedHandle)host.ResolveId(1,"node");
   for(int i=0;i<4096;i++)host.SetPosition(node,new VectorValue("vector3",i,0,0));
   No(()=>host.SetPosition(node,new VectorValue("vector3",1,0,0)),"coalescing does not bypass VM write cost");Yes(host.Stopped,"write cost failure stops tick");
  }
  using(var host=new MirrorVmHost(new OwnedDomain(Identity.Instance,1),Identity,Snapshot())){
   host.BeginTick(new TickInput(1,Snapshot()));var born=host.Instantiate((OwnedHandle)host.ResolveId(5,"prefab"));host.EndTick();host.Dispose();
   host.ApplyAck(new BatchAck(Identity,1,1,true,Snapshot(2,2),new Dictionary<long,long>{{-1,101},{-2,102}}));
   No(()=>host.GetPosition(born),"stop and late ack never resurrect tentative references");
  }
 }
 static void RealVm(string path){
  var build=Admission.Admit(System.IO.File.ReadAllBytes(path),"windows-x86_64");
  var domain=new OwnedDomain(Identity.Instance,1);
  using(var host=new MirrorVmHost(domain,Identity,Snapshot())){
   var runtime=new VmRuntime(build,domain,host,new ExecutionQuota(1000,16,1000));host.BindRuntime(runtime);
   var list=domain.List("i32",new object[]{1});
   host.BeginTick(new TickInput(1,Snapshot()));runtime.Start();runtime.Update();Yes((int)runtime.Field(0,"counter")==1,"real VM callback updated field");host.EndTick();
   No(()=>runtime.Update(),"actual VM cannot advance pure fields while waiting for ack");
   host.ApplyAck(new BatchAck(Identity,1,1,false,Snapshot(2,2)));
   Yes(runtime.State==VmState.Stopped,"conflict stops actual VM instance");
   No(()=>runtime.Update(),"actual VM callback cannot run after conflict");
   No(()=>runtime.Field(0,"counter"),"actual VM fields invalidated after conflict");
   No(()=>domain.Add(list,2),"existing VM collection cannot fork after conflict");
  }
  var program=build.Program;
  JObject Ins(string op,object value=null)=>new JObject{{"op",op},{"value",value==null?JValue.CreateNull():JToken.FromObject(value)}};
  program["classes"][0]["methods"][0]["body"]=new JArray(Ins("ldarg",0),Ins("ldfield","target"),Ins("ldc.r4",5f),Ins("ldc.r4",0f),Ins("ldc.r4",0f),Ins("newvector3"),Ins("call","UnityEngine.CoreModule::UnityEngine.Transform::set_localPosition(UnityEngine.Vector3)->System.Void"),Ins("br",7));
  var hostile=Admission.Admit(PackageWriter.Export(program,build.Content,new Dictionary<string,byte[]>(),(JArray)build.Manifest["licenses"]),"windows-x86_64");
  domain=new OwnedDomain(Identity.Instance,1);
  using(var host=new MirrorVmHost(domain,Identity,Snapshot())){
   var runtime=new VmRuntime(hostile,domain,host,new ExecutionQuota(1000,16,1000));host.BindRuntime(runtime);host.BeginTick(new TickInput(1,Snapshot()));runtime.Start();
   No(()=>runtime.Update(),"actual VM loops after speculative write");Yes(runtime.State==VmState.Faulted,"actual VM fault recorded");
   Yes(host.GetPosition((OwnedHandle)host.ResolveId(1,"node")).X==5,"fault happened after command was queued");
   No(()=>host.EndTick(),"faulted VM cannot publish partial commands");Yes(host.Stopped&&runtime.State==VmState.Stopped,"fault cancels speculative batch and VM");
  }
 }
 static int Main(string[] args){
  Yes(!typeof(MirrorVmHost).Assembly.GetReferencedAssemblies().Any(a=>a.Name.StartsWith("Unity",StringComparison.Ordinal)),"bridge compiled without Unity assemblies");
  Protocol();Mirror();FailureBoundaries();RealVm(args[0]);Console.WriteLine("scene-command checks passed: "+checks);return 0;
 }
}
