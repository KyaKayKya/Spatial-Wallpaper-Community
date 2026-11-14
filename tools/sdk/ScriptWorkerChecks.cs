extern alias supervisor;
using System;
using System.IO;
using System.Text;
using System.Linq;
using System.Threading.Tasks;
using System.Collections.Generic;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Net;
using System.Net.Sockets;
using System.Runtime.InteropServices;
using SpatialWallpaper.Sdk.Native.Windows;
using Newtonsoft.Json.Linq;
using SpatialWallpaper.Sdk.Native;
using Windows=supervisor::SpatialWallpaper.Supervision.Windows;
using WorkerIdentity=supervisor::SpatialWallpaper.Sdk.Hosting.WorkerIdentity;

static class ScriptWorkerChecks {
 [DllImport("ntdll.dll")]static extern int NtSuspendProcess(IntPtr process);
 [DllImport("ntdll.dll")]static extern int NtResumeProcess(IntPtr process);
 static int checks;
 static void Yes(bool value,string label){if(!value)throw new Exception(label);checks++;}
 static string Hash(byte[] bytes){using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(bytes)).Replace("-","").ToLowerInvariant();}
 static ScriptMessage Read(Stream input,SessionIdentity identity){var task=Task.Run(()=>ScriptWire.Read(input,identity,false));if(!task.Wait(10000))throw new TimeoutException("script-test-read-deadline");return task.GetAwaiter().GetResult();}
 static JObject Ins(string op,object value=null)=>new JObject{{"op",op},{"value",value==null?JValue.CreateNull():JToken.FromObject(value)}};
 static SceneSnapshot Snapshot(long revision,long sequence,bool sound=false){var objects=new List<SceneObject>{new SceneObject(1,"node",0,new JObject{{"position",new JArray(3,0,0)}})};if(sound)objects.Add(new SceneObject(2,"sound"));return new SceneSnapshot(revision,sequence,objects);}
 static void Run(Windows.ScriptProcessLauncher launcher,JObject seed,string output,string kind){
  var program=(JObject)seed["program"].DeepClone();var content=(JObject)seed["content"].DeepClone();JArray body;
  if(kind=="blocked-writer"){
   ((JArray)program["classes"][0]["fields"]).Add(new JObject{{"id","clip"},{"type","sound"}});content["nodes"][0]["components"][1]["fields"]["clip"]="clip";
   content["resources"]=new JArray(new JObject{{"id","clip"},{"kind","sound"}});body=new JArray();
   for(int i=0;i<256;i++){body.Add(Ins("ldarg",0));body.Add(Ins("ldfield","clip"));body.Add(Ins("ldc.r4",1f));body.Add(Ins("call","SpatialWallpaper.Sdk::Sound::PlayEffect(UnityEngine.AudioClip,System.Single)->System.Void"));}body.Add(Ins("ret"));
  }else{
   body=new JArray(Ins("ldarg",0),Ins("ldfield","target"),Ins("ldc.r4",3f),Ins("ldc.r4",0f),Ins("ldc.r4",0f),Ins("newvector3"),Ins("call","UnityEngine.CoreModule::UnityEngine.Transform::set_localPosition(UnityEngine.Vector3)->System.Void"));
   body.Add(kind=="budget-fault"?Ins("br",7):Ins("ret"));
  }
  program["classes"][0]["methods"][0]["body"]=body;byte[] bytes=NativeScriptDefinition.Encode(program,content);var admitted=NativeScriptDefinition.Admit(bytes);
  string path=Path.Combine(output,kind+".native-script.json");File.WriteAllBytes(path,bytes);
  var worker=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);var identity=new SessionIdentity(worker.Instance,worker.Generation,SceneCommandCatalog.Digest);
  var record=new JObject{{"case",kind}};
  using(var process=launcher.Start(new Windows.ScriptLaunchRequest(worker,path,Hash(bytes),admitted.BuildId)))using(var input=process.OpenReader())using(var writer=process.OpenWriter()){
   int pid=process.ProcessId;long creation=process.CreationFileTime;var hello=Read(input,identity);Yes(hello.Type=="hello","real script hello");var data=hello.Payload;
   Yes((int)data["pid"]==pid&&(long)data["creation_filetime"]==creation&&(string)data["build"]==admitted.BuildId&&(string)data["role"]=="pure-script","kernel PID/creation and build binding");
   using(var observed=Process.GetProcessById(pid)){
    var modules=observed.Modules.Cast<ProcessModule>().Select(m=>m.ModuleName).ToArray();record["modules"]=new JArray(modules);
    Yes(!modules.Any(n=>n.StartsWith("Unity",StringComparison.OrdinalIgnoreCase)||n.Equals("d3d11.dll",StringComparison.OrdinalIgnoreCase)||n.Equals("dxgi.dll",StringComparison.OrdinalIgnoreCase)),"no Unity or GPU device modules in real process");
   }
   bool sound=kind=="blocked-writer";var snapshot=Snapshot(1,1,sound);var resources=sound?new JObject{{"clip",2}}:new JObject();
   ScriptWire.Write(writer,identity,"start",new JObject{{"snapshot",ScriptWire.Snapshot(snapshot)},{"root_nodes",new JArray(1)},{"resources",resources}},true);
   ScriptWire.Write(writer,identity,"tick",ScriptWire.Tick(new TickInput(1,snapshot)),true);
   if(kind=="blocked-writer"){
    System.Threading.Thread.Sleep(150);var clock=Stopwatch.StartNew();process.RequestStop();Yes(process.Wait(2000),"stop interrupts blocked data writer");
    record["stop_ms"]=clock.ElapsedMilliseconds;Yes(process.ExitCode==0,"independent stop thread exits cleanly");
   }else{
    var message=Read(input,identity);
    if(kind=="budget-fault"){
     Yes(message.Type=="fault"&&(string)message.Payload["error"]!=null,"faulted author emits no partial batch");Yes(process.Wait(2000)&&process.ExitCode==20,"faulted worker exits");
    }else{
     Yes(message.Type=="batch","real interpreted command batch");var batch=ScriptWire.Batch(message.Payload,identity);
     Yes(batch.Commands.Count==1&&batch.Commands[0].Operation=="SetPosition"&&batch.Commands[0].Target==1&&(int)batch.Commands[0].Arguments[0]==3,"actual sandbox VM writes mirror command");
     ScriptWire.Write(writer,identity,"ack",ScriptWire.Ack(new BatchAck(identity,1,1,true,Snapshot(2,2))),true);
     ScriptWire.Write(writer,identity,"tick",ScriptWire.Tick(new TickInput(2,Snapshot(2,1000))),true);
     message=Read(input,identity);batch=ScriptWire.Batch(message.Payload,identity);Yes(batch.Sequence==2&&batch.Tick==2&&batch.BaseRevision==2,"hundreds of native frame advances do not invalidate next tick");
     ScriptWire.Write(writer,identity,"ack",ScriptWire.Ack(new BatchAck(identity,2,2,true,Snapshot(3,1001))),true);
     process.RequestStop();Yes(process.Wait(2000)&&process.ExitCode==0,"normal worker stop");
    }
   }
   record["pid"]=pid;record["creation_filetime"]=creation;record["exit_code"]=process.ExitCode;record["passed"]=true;
  }
  File.WriteAllText(Path.Combine(output,kind+".json"),record.ToString());
 }
 static int RunMain(string[] args){
  Console.WriteLine("LPAC check: load trusted inventories");
  string worker=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[2]),attempts=Path.GetFullPath(args[3]);Directory.CreateDirectory(output);
  var build=JObject.Parse(File.ReadAllText(Path.Combine(worker,"build.json")));Yes((string)build["identity"]["scene_catalog_digest"]==SceneCommandCatalog.Digest,"worker/catalog version matches");
  var files=((JObject)build["runtime_files"]).Properties().ToDictionary(p=>p.Name,p=>(string)p.Value);var image=new Windows.WorkerImage(worker,"ScriptWorker.exe",files);var seed=JObject.Parse(File.ReadAllText(args[1]));
  Console.WriteLine("LPAC check: create private station");using(var station=new Windows.PrivateStation()){
   var launcher=new Windows.ScriptProcessLauncher(station,image,attempts);
   foreach(string kind in new[]{"healthy","budget-fault","blocked-writer"})Run(launcher,seed,output,kind);
   InputProgram(launcher,seed,output);
   HeldProgram(launcher,output);
   if(args.Length>5)PermissionProbe(station,Path.GetFullPath(args[5]),output,attempts);
  }
  if(args.Contains("--negative-issued-input-assert")){IssuedInputs(worker,Path.GetFullPath(args[4]),output,attempts,true);throw new Exception("missing-injected-assertion");}
  DirectBridge(worker,Path.GetFullPath(args[4]),output,attempts);
  PumpBridge(worker,Path.GetFullPath(args[4]),output,attempts);
  QueuedInputs(worker,Path.GetFullPath(args[4]),output,attempts,false);
  QueuedInputs(worker,Path.GetFullPath(args[4]),output,attempts,true);
  IssuedInputs(worker,Path.GetFullPath(args[4]),output,attempts);
  BrokerIssuedInputs(worker,Path.GetFullPath(args[4]),output,attempts);
  CleanupFailure(seed,output);
  Console.WriteLine("real LPAC script checks passed: "+checks);return 0;
 }
 static void InputProgram(Windows.ScriptProcessLauncher launcher,JObject seed,string output){
  var program=(JObject)seed["program"].DeepClone();var content=(JObject)seed["content"].DeepClone();
  program["classes"][0]["methods"][0]["body"]=new JArray(Ins("ldstr","Primary"),Ins("call","SpatialWallpaper.Sdk::Input::GetButtonDown(System.String)->System.Boolean"),Ins("brfalse",10),
   Ins("ldarg",0),Ins("ldfield","target"),Ins("ldc.r4",3f),Ins("ldc.r4",0f),Ins("ldc.r4",0f),Ins("newvector3"),Ins("call","UnityEngine.CoreModule::UnityEngine.Transform::set_localPosition(UnityEngine.Vector3)->System.Void"),Ins("ret"));
  byte[] bytes=NativeScriptDefinition.Encode(program,content);var admitted=NativeScriptDefinition.Admit(bytes);string path=Path.Combine(output,"input.native-script.json");File.WriteAllBytes(path,bytes);
  var worker=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);var id=new SessionIdentity(worker.Instance,1,SceneCommandCatalog.Digest);long revision=1;int deliveries=0;var counts=new uint[7];
  using(var process=launcher.Start(new Windows.ScriptLaunchRequest(worker,path,Hash(bytes),admitted.BuildId)))using(var reader=process.OpenReader())using(var writer=process.OpenWriter()){
   Yes(Read(reader,id).Type=="hello","input program admitted by actual pure worker");ScriptWire.Write(writer,id,"start",new JObject{{"snapshot",ScriptWire.Snapshot(Snapshot(1,1))},{"root_nodes",new JArray(1)},{"resources",new JObject()}},true);
   for(int tick=1;tick<=15;tick++){
    bool expected=tick<=10||tick==15;ulong epoch=tick<=11?1ul:tick==12?2ul:tick==13?1ul:tick==14?3ul:4ul;bool focused=tick!=12;
    if(tick<=10)counts[0]++;long now=supervisor::SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds();
    byte[] input=SpatialWallpaper.Sdk.Capabilities.InputSnapshotCodec.Encode(epoch,tick==14?now-1000:now,focused,0,focused?(ushort)100:(ushort)0,focused?(ushort)200:(ushort)0,0,focused?counts:new uint[7],new uint[7]);
    var snapshot=Snapshot(revision,tick*2);ScriptWire.Write(writer,id,"tick",ScriptWire.Tick(new TickInput(tick,snapshot,inputs:input)),true);var message=Read(reader,id);Yes(message.Type=="batch","input invokes actual interpreted VM");var batch=ScriptWire.Batch(message.Payload,id);
    Yes(batch.Commands.Count==(expected?1:0),"input edge exact once, revoke/duplicate/stale/old-epoch zero");if(expected){deliveries++;revision++;}ScriptWire.Write(writer,id,"ack",ScriptWire.Ack(new BatchAck(id,tick,tick,true,Snapshot(revision,tick*2+1))),true);
   }
   Yes(deliveries==11,"ten input edges plus one fresh epoch edge");process.RequestStop();Yes(process.Wait(2000)&&process.ExitCode==0,"input worker stops cleanly");
  }
  File.WriteAllText(Path.Combine(output,"input.json"),new JObject{{"passed",true},{"native_commands",deliveries},{"scope","trusted synthetic derived snapshots; actual native focus collector still pending"}}.ToString());
 }
 static void HeldProgram(Windows.ScriptProcessLauncher launcher,string output){
  var previous=JObject.Parse(File.ReadAllText(Path.Combine(output,"input.native-script.json")));var program=(JObject)previous["program"].DeepClone();
  program["classes"][0]["methods"][0]["body"][1]["value"]="SpatialWallpaper.Sdk::Input::GetButton(System.String)->System.Boolean";
  byte[] bytes=NativeScriptDefinition.Encode(program,(JObject)previous["content"]);var admitted=NativeScriptDefinition.Admit(bytes);string path=Path.Combine(output,"held.native-script.json");File.WriteAllBytes(path,bytes);
  var worker=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);var id=new SessionIdentity(worker.Instance,1,SceneCommandCatalog.Digest);long revision=1;
  using(var process=launcher.Start(new Windows.ScriptLaunchRequest(worker,path,Hash(bytes),admitted.BuildId)))using(var reader=process.OpenReader())using(var writer=process.OpenWriter()){
   Yes(Read(reader,id).Type=="hello","held program actual worker handshake");ScriptWire.Write(writer,id,"start",new JObject{{"snapshot",ScriptWire.Snapshot(Snapshot(1,1))},{"root_nodes",new JArray(1)},{"resources",new JObject()}},true);
   for(int tick=1;tick<=4;tick++){
    long now=supervisor::SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds();byte[] input=tick==2?null:SpatialWallpaper.Sdk.Capabilities.InputSnapshotCodec.Encode(tick==4?2ul:1ul,now,true,1,100,200,0,new uint[]{1,0,0,0,0,0,0},new uint[7]);
    ScriptWire.Write(writer,id,"tick",ScriptWire.Tick(new TickInput(tick,Snapshot(revision,tick*2),inputs:input)),true);var reply=Read(reader,id);Yes(reply.Type=="batch","held program actual VM tick");var batch=ScriptWire.Batch(reply.Payload,id);
    Yes(batch.Commands.Count==(tick==1||tick==4?1:0),"actual LPAC empty tick clears held and blocks old epoch:"+tick);if(batch.Commands.Count>0)revision++;
    ScriptWire.Write(writer,id,"ack",ScriptWire.Ack(new BatchAck(id,tick,tick,true,Snapshot(revision,tick*2+1))),true);
   }
   process.RequestStop();Yes(process.Wait(2000)&&process.ExitCode==0,"held program releases actual worker");
  }
 }
 static void DirectBridge(string workerRoot,string helperRoot,string output,string attempts){
  string program=Path.Combine(output,"healthy.native-script.json"),helper=Path.Combine(helperRoot,"Supervisor.exe");byte[] bytes=File.ReadAllBytes(program);var admitted=NativeScriptDefinition.Admit(bytes);
  var id=new SessionIdentity(Guid.NewGuid(),1,SceneCommandCatalog.Digest);var inventory=JObject.Parse(File.ReadAllText(Path.Combine(workerRoot,"build.json")));
  var config=new JObject{{"worker_image",workerRoot},{"worker_files",inventory["runtime_files"]},{"attempts",attempts},{"attempt",Guid.NewGuid().ToString("N")},{"session",id.Instance.ToString("N")},{"generation",id.Generation},{"program",program},{"program_sha256",Hash(bytes)},{"build",admitted.BuildId}};
  var record=new JObject();using(var client=new ScriptProcessClient(helper,Hash(File.ReadAllBytes(helper)),Path.Combine(Path.GetDirectoryName(attempts),"bootstrap"),config)){
   using(var owner=Process.GetProcessById(client.HelperProcessId)){
    Yes(NtSuspendProcess(owner.Handle)==0,"pause trusted helper after handle duplication");
    try{
     var hello=Read(client.Reader,id);Yes(hello.Type=="hello"&&(int)hello.Payload["pid"]==client.ScriptProcessId&&(long)hello.Payload["creation_filetime"]==client.ScriptCreationFileTime,"direct pipe peer is actual LPAC script");
     var snapshot=Snapshot(1,1);ScriptWire.Write(client.Writer,id,"start",new JObject{{"snapshot",ScriptWire.Snapshot(snapshot)},{"root_nodes",new JArray(1)},{"resources",new JObject()}},true);
     ScriptWire.Write(client.Writer,id,"tick",ScriptWire.Tick(new TickInput(1,snapshot)),true);var reply=Read(client.Reader,id);var batch=ScriptWire.Batch(reply.Payload,id);
     Yes(reply.Type=="batch"&&batch.Commands.Count==1&&batch.Commands[0].Operation=="SetPosition","script command delivered with launch helper suspended; no command relay");
     record["helper_pid"]=client.HelperProcessId;record["script_pid"]=client.ScriptProcessId;record["script_creation"]=client.ScriptCreationFileTime;record["helper_suspended_during_data_exchange"]=true;
    }finally{Yes(NtResumeProcess(owner.Handle)==0,"resume owned launch helper");}
   }
  }
  Yes(Directory.EnumerateFileSystemEntries(attempts).All(p=>Path.GetFileName(p)=="boundary"&&Directory.Exists(p)&&!Directory.EnumerateFileSystemEntries(p).Any()),"direct launch cleans owned script attempt; shared boundary container remains empty");record["passed"]=true;File.WriteAllText(Path.Combine(output,"direct-pipe.json"),record.ToString());
 }
 sealed class NativeTarget:INativeCommandTarget {
  long revision=1,sequence;public float Position;public int Frames,Nonempty;
  public SceneSnapshot Snapshot=>new SceneSnapshot(revision,++sequence,new[]{new SceneObject(1,"node",0,new JObject{{"position",new JArray(Position,0,0)}})});
  public int PrepareAttempts,PendingInputAttempts;public Action BeforeInputPrepare;public bool Pending,PendingInput;public bool TryPrepare(CommandBatch batch,out object prepared){PrepareAttempts++;prepared=batch;if(batch.Commands.Count>0)BeforeInputPrepare?.Invoke();if(PendingInput&&batch.Commands.Count>0){PendingInputAttempts++;return false;}return !Pending;}
  public BatchAck Commit(CommandBatch batch,object prepared){if(batch.Commands.Count>0)Nonempty++;foreach(var cmd in batch.Commands)Position=(float)cmd.Arguments[0];if(SceneCommandProtocol.AdvancesRevision(batch))revision++;return new BatchAck(batch.Identity,batch.Tick,batch.Sequence,true,Snapshot);}
  public void CancelPreparation(){}
 }
 sealed class FailedClose:IScriptProcessConnection {
  public Stream Reader{get;}public Stream Writer{get;}=new MemoryStream();public int ScriptProcessId=>123;public long ScriptCreationFileTime=>456;
  public FailedClose(SessionIdentity id,string build){var input=new MemoryStream();ScriptWire.Write(input,id,"hello",new JObject{{"pid",123},{"creation_filetime",456},{"role","pure-script"},{"build",build}},false);ScriptWire.Write(input,id,"fault",new JObject{{"error","synthetic-author-fault"}},false);input.Position=0;Reader=input;}
  public void Dispose(){Reader.Dispose();Writer.Dispose();throw new IOException("synthetic-kernel-cleanup-unproven");}
 }
 static void CleanupFailure(JObject seed,string output){
  var id=new SessionIdentity(Guid.NewGuid(),1,SceneCommandCatalog.Digest);string build=NativeScriptDefinition.Admit(File.ReadAllBytes(Path.Combine(output,"healthy.native-script.json"))).BuildId;
  using(var session=new NativeScriptSession(id,new NativeTarget(),()=>new FailedClose(id,build),new long[]{1},new JObject(),build)){
   Yes(session.Completion.Wait(2000)&&session.CleanupError!=null&&!session.ResourcesReleased,"completed cleanup failure cannot masquerade as kernel release");
  }
 }
 static void QueuedInputs(string workerRoot,string helperRoot,string output,string attempts,bool ordered){
  var results=new JArray();foreach(bool revoke in new[]{false,true}){
   string path=Path.Combine(output,"input.native-script.json"),helper=Path.Combine(helperRoot,"Supervisor.exe");byte[] bytes=File.ReadAllBytes(path);var build=NativeScriptDefinition.Admit(bytes);var id=new SessionIdentity(Guid.NewGuid(),1,SceneCommandCatalog.Digest);var inventory=JObject.Parse(File.ReadAllText(Path.Combine(workerRoot,"build.json")));
   var config=new JObject{{"worker_image",workerRoot},{"worker_files",inventory["runtime_files"]},{"attempts",attempts},{"attempt",Guid.NewGuid().ToString("N")},{"session",id.Instance.ToString("N")},{"generation",id.Generation},{"program",path},{"program_sha256",Hash(bytes)},{"build",build.BuildId}};
   var target=new NativeTarget{Pending=true};var clock=Stopwatch.StartNew();var counts=new uint[7];counts[0]=10;
   using(var session=new NativeScriptSession(id,target,()=>new ScriptProcessClient(helper,Hash(File.ReadAllBytes(helper)),Path.Combine(Path.GetDirectoryName(attempts),"bootstrap"),config),new long[]{1},new JObject(),build.BuildId)){
    // The first no-input Tick is held unacknowledged while ten complete short
    // clicks arrive, then optionally lose authority before that ack is released.
    while(target.PrepareAttempts==0&&clock.ElapsedMilliseconds<10000){session.Pump();System.Threading.Thread.Sleep(2);}Yes(session.Running&&target.PrepareAttempts>0,"first no-input batch is pending before clicks arrive");clock.Restart();
    long captured=supervisor::SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds();
    var events=new SpatialWallpaper.Sdk.Capabilities.OrderedInputEvent[20];for(int i=0;i<20;i++)events[i]=new SpatialWallpaper.Sdk.Capabilities.OrderedInputEvent(i+1,captured,1,i%2==0?1u:0u,i%2==0?1u:0u,i%2==0?0u:1u,0);
    try{while(clock.ElapsedMilliseconds<1600){
     bool focused=!(revoke&&clock.ElapsedMilliseconds>=200);long now=supervisor::SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds();var packet=SpatialWallpaper.Sdk.Capabilities.InputSnapshotCodec.Encode(focused?1ul:2ul,now,focused,0,focused?(ushort)100:(ushort)0,focused?(ushort)200:(ushort)0,0,focused?counts:new uint[7],focused?counts:new uint[7]);
     if(clock.ElapsedMilliseconds>=400)target.Pending=false;
     if(ordered)session.PumpOrdered(1f/60,new SpatialWallpaper.Sdk.Capabilities.OrderedInputBatch(packet,focused?events:new SpatialWallpaper.Sdk.Capabilities.OrderedInputEvent[0],20));else session.Pump(1f/60,packet);
     target.Frames++;System.Threading.Thread.Sleep(2);
    }
    Yes(session.Error==null&&target.Nonempty==(revoke?0:10),"ten queued short clicks survive ack backpressure; revoked queue dispatches zero; ordered="+ordered+"; actual="+target.Nonempty+"; error="+session.Error);
    Yes(session.LastAcknowledgedInputEvent==(revoke?0:20),"accepted-VM-batch-ACK-event-diagnostic-retains-all-edges-and-no-revoked-edges; ordered="+ordered);
    }finally{session.Stop();Yes(session.Completion.Wait(8000)&&session.ResourcesReleased,"queued input session releases actual process tree even after failed assertion");}
    results.Add(new JObject{{"revoked_before_ack",revoke},{"native_commands",target.Nonempty},{"native_iterations",target.Frames},{"last_accepted_batch_input_event",session.LastAcknowledgedInputEvent},{"scope","synthetic broker snapshots, actual LPAC/VM/ack written; not Worker ACK-read receipt or native window focus"}});
   }
  }
  File.WriteAllText(Path.Combine(output,ordered?"queued-ordered-input.json":"queued-input.json"),results.ToString());
 }
 static void IssuedInputs(string workerRoot,string helperRoot,string output,string attempts,bool negativeAssertion=false){
   var results=new JArray();foreach(string mode in new[]{"current","revoked","new-epoch","local-pause","paused-long"}){
   string path=Path.Combine(output,"input.native-script.json"),helper=Path.Combine(helperRoot,"Supervisor.exe");byte[] bytes=File.ReadAllBytes(path);var build=NativeScriptDefinition.Admit(bytes);var id=new SessionIdentity(Guid.NewGuid(),1,SceneCommandCatalog.Digest);var inventory=JObject.Parse(File.ReadAllText(Path.Combine(workerRoot,"build.json")));
   var config=new JObject{{"worker_image",workerRoot},{"worker_files",inventory["runtime_files"]},{"attempts",attempts},{"attempt",Guid.NewGuid().ToString("N")},{"session",id.Instance.ToString("N")},{"generation",id.Generation},{"program",path},{"program_sha256",Hash(bytes)},{"build",build.BuildId}};
   var target=new NativeTarget{PendingInput=true};var clock=Stopwatch.StartNew();var counts=new uint[7];counts[0]=1;
   using(var session=new NativeScriptSession(id,target,()=>new ScriptProcessClient(helper,Hash(File.ReadAllBytes(helper)),Path.Combine(Path.GetDirectoryName(attempts),"bootstrap"),config),new long[]{1},new JObject(),build.BuildId)){
    try{
    while(target.PendingInputAttempts==0&&clock.ElapsedMilliseconds<10000){long now=supervisor::SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds();session.Pump(1f/60,SpatialWallpaper.Sdk.Capabilities.InputSnapshotCodec.Encode(1,now,true,1,100,200,0,counts,new uint[7]));System.Threading.Thread.Sleep(2);}
    Yes(session.Error==null&&target.PendingInputAttempts>0&&target.Nonempty==0,"real author input batch is prepared but not committed:"+mode);
    if(negativeAssertion)Yes(false,"synthetic-issued-input-assertion");
    bool focused=mode!="revoked";long stamp=supervisor::SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds();var current=SpatialWallpaper.Sdk.Capabilities.InputSnapshotCodec.Encode(mode=="current"?1ul:2ul,stamp,focused,focused?1u:0u,focused?(ushort)100:(ushort)0,focused?(ushort)200:(ushort)0,0,focused?counts:new uint[7],new uint[7]);
     if(mode=="paused-long"){
      session.SetPaused(true);target.PendingInput=false;var hold=Stopwatch.StartNew();
      while(hold.ElapsedMilliseconds<1400){session.Pump();System.Threading.Thread.Sleep(2);}
      Yes(target.Nonempty==0&&session.Error==null&&!session.Completion.IsCompleted,"paused focused pending batch freezes without replay or false deadline");
      session.SetPaused(false);
     }
     target.PendingInput=false;if(mode=="local-pause"){session.RevokeInput();session.Pump();}else session.Pump(1f/60,current);
    Yes(target.Nonempty==(mode=="current"?1:0),"issued real LPAC input batch uses current native authority:"+mode);
    if(mode!="current")Yes(session.Error!=null&&(session.Error.Contains("native-issued-input-revoked-or-expired")||session.Error=="InvalidOperationException:native-script-transaction-rejected"),"revoked input rejects entire script transaction:"+mode+":"+session.Error);
    results.Add(new JObject{{"case",mode},{"native_commands",target.Nonempty},{"error",session.Error},{"scope","actual LPAC issued batch and pure native target; synthetic trusted input, no actual window focus claim"}});
    }finally{session.Stop();Yes(session.Completion.Wait(8000)&&session.ResourcesReleased,"issued input session kernel cleanup even after assertion failure:"+mode);}
   }
  }
  File.WriteAllText(Path.Combine(output,"issued-input.json"),results.ToString());
 }
 static void BrokerIssuedInputs(string workerRoot,string helperRoot,string output,string attempts){
  var results=new JArray();foreach(string mode in new[]{"current","frozen-cache","prepare-revoke"}){
   using(var broker=new supervisor::SpatialWallpaper.Sdk.Capabilities.InputBroker())using(var registry=new Windows.NativeInputRegistry(broker))using(var self=supervisor::SpatialWallpaper.Sdk.Hosting.PinnedProcessOwner.PinCurrent()){
    var owner=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);registry.RegisterOrdered(owner,true,self.Id,self.CreationFileTime);var input=registry.Find(owner,true,self.Id,self.CreationFileTime);
    using(var source=new supervisor::SpatialWallpaper.Sdk.Hosting.NativeInputAuthorityReader(registry.OpenAuthority(owner,self.Id,self.CreationFileTime),owner,self,self)){
     string path=Path.Combine(output,"input.native-script.json"),helper=Path.Combine(helperRoot,"Supervisor.exe");byte[] bytes=File.ReadAllBytes(path);var definition=NativeScriptDefinition.Admit(bytes);var id=new SessionIdentity(owner.Instance,1,SceneCommandCatalog.Digest);var inventory=JObject.Parse(File.ReadAllText(Path.Combine(workerRoot,"build.json")));
     var config=new JObject{{"worker_image",workerRoot},{"worker_files",inventory["runtime_files"]},{"attempts",attempts},{"attempt",Guid.NewGuid().ToString("N")},{"session",id.Instance.ToString("N")},{"generation",1},{"program",path},{"program_sha256",Hash(bytes)},{"build",definition.BuildId}};
     var target=new NativeTarget{PendingInput=true};var clock=Stopwatch.StartNew();
     using(var session=new NativeScriptSession(id,target,()=>new ScriptProcessClient(helper,Hash(File.ReadAllBytes(helper)),Path.Combine(Path.GetDirectoryName(attempts),"bootstrap"),config),new long[]{1},new JObject(),definition.BuildId,()=>{var value=source.Read();return new SpatialWallpaper.Sdk.Capabilities.InputAuthority(value.Epoch,value.Time);})){try{
      while(!session.Running&&clock.ElapsedMilliseconds<10000){session.Pump();System.Threading.Thread.Sleep(2);}Yes(session.Running,"Broker authority actual LPAC startup:"+mode);
      long now=supervisor::SpatialWallpaper.Sdk.Capabilities.WindowsMachineClock.Milliseconds();input.Observe(new supervisor::SpatialWallpaper.Sdk.Capabilities.InputObservation(1,now,true,true,true,true,true,false,true,1,.5f,.5f,0,input.CaptureEpoch));byte[] frozen=input.Snapshot();clock.Restart();
      while(target.PendingInputAttempts==0&&clock.ElapsedMilliseconds<200){session.Pump(1f/60,frozen);System.Threading.Thread.Sleep(1);}Yes(target.PendingInputAttempts>0&&target.Nonempty==0&&session.Error==null,"real input-dependent LPAC batch pending with Broker authority:"+mode);
      if(mode=="frozen-cache")input.Revoke();if(mode=="prepare-revoke")target.BeforeInputPrepare=()=>input.Revoke();target.PendingInput=false;
      session.Pump(1f/60,frozen);Yes(target.Nonempty==(mode=="current"?1:0),"actual LPAC batch uses final Broker authority with background snapshot frozen:"+mode);
      if(mode!="current")Yes(session.Error!=null,"revoked Broker authority rejects whole actual LPAC batch:"+mode);
      results.Add(new JObject{{"case",mode},{"commands",target.Nonempty},{"error",session.Error}});
     }finally{session.Stop();Yes(session.Completion.Wait(8000)&&session.ResourcesReleased,"Broker authority actual LPAC cleanup:"+mode);}}
    }
   }
  }
  File.WriteAllText(Path.Combine(output,"broker-issued-input.json"),new JObject{{"scope","actual Broker, shared authority, LPAC VM and native target; synthetic trusted observations, no OS focus claim"},{"cases",results}}.ToString());
 }
 static void PumpBridge(string workerRoot,string helperRoot,string output,string attempts){
   PausedWireDeadline(workerRoot,helperRoot,output,attempts);
  var results=new JArray();foreach(string kind in new[]{"healthy","budget-fault","prepare-stall"}){
   string program=Path.Combine(output,(kind=="prepare-stall"?"healthy":kind)+".native-script.json"),helper=Path.Combine(helperRoot,"Supervisor.exe");byte[] bytes=File.ReadAllBytes(program);var admitted=NativeScriptDefinition.Admit(bytes);var id=new SessionIdentity(Guid.NewGuid(),1,SceneCommandCatalog.Digest);
   var inventory=JObject.Parse(File.ReadAllText(Path.Combine(workerRoot,"build.json")));var config=new JObject{{"worker_image",workerRoot},{"worker_files",inventory["runtime_files"]},{"attempts",attempts},{"attempt",Guid.NewGuid().ToString("N")},{"session",id.Instance.ToString("N")},{"generation",id.Generation},{"program",program},{"program_sha256",Hash(bytes)},{"build",admitted.BuildId}};
   var target=new NativeTarget{Pending=kind=="prepare-stall"};var clock=Stopwatch.StartNew();long maximumUs=0;
   using(var session=new NativeScriptSession(id,target,()=>new ScriptProcessClient(helper,Hash(File.ReadAllBytes(helper)),Path.Combine(Path.GetDirectoryName(attempts),"bootstrap"),config),new long[]{1},new JObject(),admitted.BuildId)){
    while(clock.ElapsedMilliseconds<1800){long before=Stopwatch.GetTimestamp();session.Pump();long elapsed=(Stopwatch.GetTimestamp()-before)*1000000/Stopwatch.Frequency;maximumUs=Math.Max(maximumUs,elapsed);target.Frames++;System.Threading.Thread.Sleep(2);}
    Yes(target.Frames>100,"native frame loop keeps advancing during real worker startup and execution");
    if(kind=="healthy")Yes(session.AcknowledgedBatches>10&&target.Position==3&&session.Error==null,"real LPAC VM commits repeatedly through native pump");
    else Yes(session.Error!=null&&(kind!="prepare-stall"||session.Error=="native-script-deadline")&&target.Position==0&&session.AcknowledgedBatches==0,"faulted or pending real VM never publishes partial native change");
    session.Stop();Yes(session.Completion.Wait(8000),"session background cleanup completes");
    results.Add(new JObject{{"case",kind},{"native_iterations",target.Frames},{"maximum_pump_us",maximumUs},{"acknowledged_batches",session.AcknowledgedBatches},{"error",session.Error}});
   }
  }
  static void PausedWireDeadline(string workerRoot,string helperRoot,string output,string attempts){
   string program=Path.Combine(output,"healthy.native-script.json"),helper=Path.Combine(helperRoot,"Supervisor.exe");byte[] bytes=File.ReadAllBytes(program);var admitted=NativeScriptDefinition.Admit(bytes);var id=new SessionIdentity(Guid.NewGuid(),1,SceneCommandCatalog.Digest);
   var inventory=JObject.Parse(File.ReadAllText(Path.Combine(workerRoot,"build.json")));var config=new JObject{{"worker_image",workerRoot},{"worker_files",inventory["runtime_files"]},{"attempts",attempts},{"attempt",Guid.NewGuid().ToString("N")},{"session",id.Instance.ToString("N")},{"generation",id.Generation},{"program",program},{"program_sha256",Hash(bytes)},{"build",admitted.BuildId}};
   using(var session=new NativeScriptSession(id,new NativeTarget(),()=>new ScriptProcessClient(helper,Hash(File.ReadAllBytes(helper)),Path.Combine(Path.GetDirectoryName(attempts),"bootstrap"),config),new long[]{1},new JObject(),admitted.BuildId)){
    Process script=null;bool suspended=false;try{
     var clock=Stopwatch.StartNew();while(session.AcknowledgedBatches<2&&clock.ElapsedMilliseconds<10000){session.Pump();System.Threading.Thread.Sleep(2);}Yes(session.AcknowledgedBatches>=2&&session.Error==null,"pause wire actual LPAC running");
     script=Process.GetProcessById(session.ScriptProcessId);Yes(supervisor::SpatialWallpaper.Sdk.Hosting.PinnedProcessOwner.ReadCreationFileTime(script.Handle)==session.ScriptCreationFileTime,"pause wire original script birth");Yes(NtSuspendProcess(script.Handle)==0,"suspend only owned script to block wire");suspended=true;
     var phase=typeof(NativeScriptSession).GetField("phase",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);clock.Restart();while((int)phase.GetValue(session)!=1&&clock.ElapsedMilliseconds<500){session.Pump();System.Threading.Thread.Sleep(1);}Yes((int)phase.GetValue(session)==1,"pause starts with outstanding actual wire");session.SetPaused(true);
     clock.Restart();while(session.Error==null&&clock.ElapsedMilliseconds<2200){session.Pump();System.Threading.Thread.Sleep(2);}Yes(session.Error=="native-script-deadline","owner pause cannot waive script wire deadline");
     File.WriteAllText(Path.Combine(output,"paused-wire.json"),new JObject{{"error",session.Error},{"scope","actual owned suspended LPAC; owner pause does not extend outstanding wire deadline"}}.ToString());
    }finally{if(suspended&&!script.HasExited)NtResumeProcess(script.Handle);script?.Dispose();session.Stop();Yes(session.Completion.Wait(8000)&&session.ResourcesReleased,"paused wire actual kernel cleanup");}
   }
  }
  File.WriteAllText(Path.Combine(output,"native-pump.json"),new JObject{{"scope","non-Unity native-loop/real-LPAC composition; not displayed-frame evidence"},{"cases",results}}.ToString());
 }
 static void PermissionProbe(Windows.PrivateStation station,string imageRoot,string output,string attempts){
  var listener=new TcpListener(IPAddress.Loopback,0);listener.Start(4);
  try{
   int port=((IPEndPoint)listener.LocalEndpoint).Port;
   using(var control=new TcpClient()){control.Connect(IPAddress.Loopback,port);using(var accepted=listener.AcceptTcpClient())Yes(control.Connected,"full-trust loopback positive control");}
   string peer=Path.Combine(output,"outside-owned-canary.txt"),write=Path.Combine(output,"outside-forbidden-write.txt");File.WriteAllText(peer,"owned synthetic permission canary");
   using(var broker=new supervisor::SpatialWallpaper.Sdk.Capabilities.InputBroker())
   using(var registry=new Windows.NativeInputRegistry(broker))
   using(var self=supervisor::SpatialWallpaper.Sdk.Hosting.PinnedProcessOwner.PinCurrent()){
   var authorityIdentity=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);registry.RegisterOrdered(authorityIdentity,true,self.Id,self.CreationFileTime);
   string section=registry.OpenAuthority(authorityIdentity,self.Id,self.CreationFileTime);
   using(var positive=System.IO.MemoryMappedFiles.MemoryMappedFile.OpenExisting(section,System.IO.MemoryMappedFiles.MemoryMappedFileRights.Read))Yes(positive!=null,"trusted authority section read positive control");
   var config=new JObject{{"outside_read",peer},{"outside_write",write},{"port",port},{"authority_section",section}};byte[] bytes=Encoding.UTF8.GetBytes(config.ToString());string path=Path.Combine(output,"permission-probe.json");File.WriteAllBytes(path,bytes);
   var build=JObject.Parse(File.ReadAllText(Path.Combine(imageRoot,"build.json")));var files=((JObject)build["runtime_files"]).Properties().ToDictionary(p=>p.Name,p=>(string)p.Value);
   var launcher=new Windows.ScriptProcessLauncher(station,new Windows.WorkerImage(imageRoot,"ScriptWorker.exe",files),attempts);
   var worker=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);var identity=new SessionIdentity(worker.Instance,worker.Generation,SceneCommandCatalog.Digest);
   using(var process=launcher.Start(new Windows.ScriptLaunchRequest(worker,path,Hash(bytes),Hash(bytes))))using(var input=process.OpenReader()){
    ScriptMessage message;try{message=Read(input,identity);}finally{File.WriteAllBytes(Path.Combine(output,"permission-probe.log"),process.ReadLog());}
    Yes(message.Type=="fault"&&(string)message.Payload["error"]=="synthetic-boundary-probe","real adversarial image ran in same production LPAC launcher");
    var report=message.Payload;
    File.WriteAllText(Path.Combine(output,"permissions.json"),report.ToString());
    foreach(string field in new[]{"outside_read_denied","outside_write_denied","scratch_write_denied","network_denied","child_denied","authority_read_denied","authority_write_denied"})Yes((bool)report[field],field);
    Yes(process.Wait(2000)&&process.ExitCode==0,"adversarial image exited without child");
    Yes(!File.Exists(write)&&File.ReadAllText(peer)=="owned synthetic permission canary"&&!listener.Pending(),"negative attempts have no outside effects");
    report["scope"]="test-only adversarial image, not author API or native focus validation";File.WriteAllText(Path.Combine(output,"permissions.json"),report.ToString());
   }
   }
  }finally{listener.Stop();}
 }
 static int Main(string[] args){try{return RunMain(args);}catch(Exception failure){Console.Error.WriteLine("check failure type: "+failure.GetType().FullName);Console.Error.WriteLine(failure.Message);return 1;}}
}
