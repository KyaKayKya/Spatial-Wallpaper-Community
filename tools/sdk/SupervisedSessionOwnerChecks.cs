using System;using System.Threading;using System.Threading.Tasks;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Windows;using SpatialWallpaper.Sdk.Hosting;
static class SupervisedSessionOwnerChecks {
 sealed class Transport:ISupervisorSessionTransport {
  public Guid Host{get;}=Guid.NewGuid();internal readonly OwnedSessionRegistry Registry;readonly OwnedSessionProtocol protocol;internal bool LoseStart,WrongHost,LoseStop,LoseForget,HoldStart;internal Guid LastToken;
  internal readonly ManualResetEventSlim ReplyEntered=new ManualResetEventSlim(),ReplyRelease=new ManualResetEventSlim();
  internal Transport(SupervisorRuntime runtime){Registry=new OwnedSessionRegistry(runtime,2,()=>Environment.TickCount&int.MaxValue);protocol=new OwnedSessionProtocol(Registry,Host);}
  public Task<Newtonsoft.Json.Linq.JObject> Call(string operation,Newtonsoft.Json.Linq.JObject payload){return Task.Run(()=>{
   Newtonsoft.Json.Linq.JObject result;
   if(operation=="ReserveSession"){
    var request=new LaunchRequest(Guid.ParseExact((string)payload["installation"],"N"),(string)payload["build"],Guid.ParseExact((string)payload["instance"],"N"),(uint)payload["generation"],StartIntent.Manual,(string)payload["package"],RuntimeLimits.ProcessBytes);
    LastToken=Registry.Reserve(request);result=protocol.Status(LastToken);
   }else{result=protocol.Dispatch(operation,payload);if(operation=="StartSession"&&LoseStart)throw new System.IO.IOException("lost-start-reply");
    if(operation=="StopSession"&&LoseStop){LoseStop=false;throw new System.IO.IOException("lost-stop-reply");}
    if(operation=="ForgetSession"&&LoseForget){LoseForget=false;throw new System.IO.IOException("lost-forget-reply");}
    if(operation=="StartSession"&&HoldStart){Join(Registry.Start(LastToken).Running);result=protocol.Status(LastToken);ReplyEntered.Set();if(!ReplyRelease.Wait(3000))throw new TimeoutException("blocked-start-reply");}
   }
   if(WrongHost&&operation!="ReserveSession"&&operation!="StartSession")result["host"]=Guid.NewGuid().ToString("N");return result;
  });}
 }
 sealed class Worker:IManagedWorker,IWorkerFrameSource {
  public int FrameWidth=>640;public int FrameHeight=>360;
  public bool CopyFrame(byte[] destination,out long frameSequence,out long projectionRevision){projectionRevision=0;frameSequence=Interlocked.Read(ref sequence);if(frameSequence==0)return false;Array.Clear(destination,0,destination.Length);destination[0]=17;destination[4]=29;destination[destination.Length-1]=255;return true;}
  internal volatile bool Alive=true,Frames=true,ProtocolFault,HoldDispose;internal readonly ManualResetEventSlim DisposeEntered=new ManualResetEventSlim(),DisposeRelease=new ManualResetEventSlim();internal int Stops,Kills,Disposed;long sequence,published=-1;readonly System.Diagnostics.Stopwatch clock;
  internal Worker(System.Diagnostics.Stopwatch time){clock=time;}
  public int ProcessId=>12345;
  public WorkerSample Sample(){Thread.Sleep(2);return new WorkerSample(Alive,1024,0,Frames?Interlocked.Increment(ref sequence):sequence,Frames?clock.ElapsedMilliseconds:0,ProtocolFault,exitCode:Alive?(uint?)null:Kills==0?0u:124u,stopPublishedAt:published);}
  public void RequestStop(){Interlocked.Increment(ref Stops);published=clock.ElapsedMilliseconds;Alive=false;}
  public void Kill(){Interlocked.Increment(ref Kills);Alive=false;}
  public void Dispose(){if(HoldDispose){DisposeEntered.Set();if(!DisposeRelease.Wait(5000))throw new TimeoutException("controlled-cleanup-release");}Interlocked.Increment(ref Disposed);}
 }
 sealed class Launcher:IWorkerLauncher {
  internal readonly ManualResetEventSlim Entered=new ManualResetEventSlim(),Release=new ManualResetEventSlim();internal Worker Last;internal int Starts;internal string Failure;readonly System.Diagnostics.Stopwatch clock;
  internal Launcher(System.Diagnostics.Stopwatch time){clock=time;}
  public IManagedWorker Start(LaunchRequest request,AttemptReceipt receipt,Action<int> created){Interlocked.Increment(ref Starts);Entered.Set();Release.Wait();if(Failure!=null)throw new System.IO.IOException(Failure);created(12345);return Last=new Worker(clock);}
 }
 static void Need(bool value,string message){if(!value)throw new Exception(message);Console.WriteLine("PASS "+message);}
 static void Wait(Func<bool> condition){var clock=System.Diagnostics.Stopwatch.StartNew();while(!condition()){if(clock.ElapsedMilliseconds>10000)throw new TimeoutException();Thread.Sleep(2);}}
 static void Join(Task task){if(!task.Wait(10000))throw new TimeoutException();task.GetAwaiter().GetResult();}
 static int Main(string[] args){try{
  var clock=System.Diagnostics.Stopwatch.StartNew();var launcher=new Launcher(clock);var journal=new JournalActor(args[0],16);var runtime=new SupervisorRuntime(journal,launcher,new WatchdogPolicy(15000,1000,500,RuntimeLimits.ProcessBytes,1000,1000),2,RuntimeLimits.GlobalProcessBytes,()=>clock.ElapsedMilliseconds);
  try{
   Guid instance=Guid.NewGuid();var request=new LaunchRequest(Guid.NewGuid(),new string('a',64),instance,1,StartIntent.Manual,"owned",RuntimeLimits.ProcessBytes);
   var owner=SupervisedRuntimeSession.Start(runtime,request);Need(launcher.Entered.Wait(3000),"production-start-queue-entered");owner.Stop();Need(!owner.Completion.IsCompleted,"pending-production-launch-remains-owned");launcher.Release.Set();Join(owner.Completion);
   Need(owner.ResourcesReleased&&launcher.Last.Stops==1&&launcher.Last.Kills==0&&launcher.Last.Disposed==1,"late-production-worker-normal-stop-and-cleanup");
   var second=SupervisedRuntimeSession.Start(runtime,new LaunchRequest(Guid.NewGuid(),new string('b',64),instance,2,StartIntent.Manual,"restart",RuntimeLimits.ProcessBytes));Join(second.Running);var worker=launcher.Last;owner.Stop();Thread.Sleep(30);
   Need(worker.Alive&&worker.Stops==0,"old-stop-cannot-affect-new-generation");worker.Frames=false;Wait(()=>second.Completion.IsCompleted);
   try{second.Completion.GetAwaiter().GetResult();throw new Exception("fault-lost");}catch(Exception error){Need(error.Message=="worker-fault:FrameTimeout"&&second.ResourcesReleased&&worker.Disposed==1,"production-frame-fault-preserved-with-cleanup");}
   long now=0;var registry=new OwnedSessionRegistry(runtime,2,()=>now);
   Func<uint,LaunchRequest> next=g=>new LaunchRequest(Guid.NewGuid(),new string('c',64),Guid.NewGuid(),g,StartIntent.Manual,"registry",RuntimeLimits.ProcessBytes);
   var reserved=registry.Reserve(next(3));registry.Stop(reserved);bool rejected=false;try{registry.Start(reserved);}catch(InvalidOperationException){rejected=true;}Need(rejected,"stop-before-start-rejects-late-management-request");
   var expired=registry.Reserve(next(4));now=15001;rejected=false;try{registry.Start(expired);}catch(InvalidOperationException){rejected=true;}Need(rejected,"expired-reservation-cannot-create-worker");
   var token=registry.Reserve(next(5));var owned=registry.Start(token);Need(object.ReferenceEquals(owned,registry.Start(token)),"duplicate-start-does-not-create-second-worker");Join(owned.Running);worker=launcher.Last;
   registry.Stop(reserved);Need(worker.Stops==0,"absent-old-token-cannot-stop-current-worker");registry.Stop(token);Join(owned.Completion);Need(registry.Status(token).ResourcesReleased,"stopped-record-retains-cleanup-proof");registry.Forget(token);Need(registry.Status(token).Absent,"forget-only-after-release");
   launcher.Entered.Reset();launcher.Release.Reset();int previous=launcher.Starts;token=registry.Reserve(next(6));
   var starts=new Task<RuntimeSession>[16];for(int i=0;i<starts.Length;i++)starts[i]=Task.Run(()=>registry.Start(token));Join(Task.WhenAll(starts));owned=starts[0].Result;
   Need(Array.TrueForAll(starts,t=>object.ReferenceEquals(t.Result,owned))&&launcher.Entered.Wait(3000)&&launcher.Starts==previous+1,"concurrent-start-one-owner-one-launch");
   now+=30000;Need(!registry.Status(token).Absent&&!registry.Status(token).ResourcesReleased,"active-launch-never-expires");
   var spare=registry.Reserve(next(7));rejected=false;try{registry.Reserve(next(8));}catch(InvalidOperationException){rejected=true;}Need(rejected&&!registry.Status(token).Absent,"capacity-rejects-without-evicting-active-owner");
   registry.Stop(token);Need(!owned.Completion.IsCompleted,"lost-start-reply-stop-retains-delayed-launch");launcher.Release.Set();Join(owned.Completion);
   var final=registry.Status(token);Need(final.ResourcesReleased&&final.State=="Stopped"&&launcher.Last.Stops==1&&launcher.Last.Disposed==1,"lost-start-reply-late-worker-cleaned");
   registry.Forget(token);registry.Forget(token);registry.Stop(spare);Join(registry.CloseAsync());
   rejected=false;try{registry.Reserve(next(9));}catch(InvalidOperationException){rejected=true;}Need(rejected,"closed-registry-rejects-new-reservations");
   Guid host=Guid.NewGuid();var protocol=new OwnedSessionProtocol(registry,host);var command=new Newtonsoft.Json.Linq.JObject{{"host",Guid.NewGuid().ToString("N")},{"token",token.ToString("N")}};
   rejected=false;try{protocol.Dispatch("SessionStatus",command);}catch(System.IO.InvalidDataException){rejected=true;}Need(rejected,"old-host-cannot-prove-absence");
   command["host"]=host.ToString("N");var absent=protocol.Dispatch("SessionStatus",command);Need((string)absent["host"]==host.ToString("N")&&(string)absent["token"]==token.ToString("N")&&(string)absent["state"]=="Absent"&&(bool)absent["resources_released"],"absence-proof-bound-to-host-and-token");
   command["unexpected"]=true;rejected=false;try{protocol.Dispatch("StopSession",command);}catch(System.IO.InvalidDataException){rejected=true;}Need(rejected,"session-command-rejects-extra-fields");
   var remote=new Transport(runtime);var client=SupervisorSessionClient.Start(remote,Guid.NewGuid(),new string('d',64),Guid.NewGuid(),10,"remote",false);Join(client.Running);Need(client.Identity!=null,"remote-client-receives-confirmed-worker-identity");client.Stop();Join(client.Completion);Need(client.ResourcesReleased&&remote.Registry.Status(remote.LastToken).Absent,"remote-normal-stop-forgets-only-after-release");
   remote=new Transport(runtime){LoseStart=true};client=SupervisorSessionClient.Start(remote,Guid.NewGuid(),new string('e',64),Guid.NewGuid(),11,"lost",false);Wait(()=>client.Completion.IsCompleted);
   Need(client.Completion.IsFaulted&&client.ResourcesReleased&&remote.Registry.Status(remote.LastToken).Absent,"lost-start-reply-cleans-created-worker-and-retains-error");
   remote=new Transport(runtime);client=SupervisorSessionClient.Start(remote,Guid.NewGuid(),new string('f',64),Guid.NewGuid(),12,"wrong-host",false);Join(client.Running);remote.WrongHost=true;client.Stop();Wait(()=>client.Completion.IsCompleted);
   Need(client.State==RuntimeSessionState.CleanupFailed&&!client.ResourcesReleased,"foreign-host-response-cannot-release-ownership");remote.WrongHost=false;Join(client.RetryCleanupAsync());Need(client.ResourcesReleased&&client.Completion.IsFaulted,"remote-cleanup-retry-preserves-original-failure");
   remote=new Transport(runtime){LoseForget=true};client=SupervisorSessionClient.Start(remote,Guid.NewGuid(),new string('2',64),Guid.NewGuid(),14,"lost-forget",false);Join(client.Running);client.Stop();Wait(()=>client.Completion.IsCompleted);
   Need(!client.ResourcesReleased&&remote.Registry.Status(remote.LastToken).Absent,"lost-forget-reply-retains-client-unconfirmed-cleanup");Join(client.RetryCleanupAsync());Need(client.ResourcesReleased&&client.Completion.IsFaulted,"same-host-absence-closes-lost-forget-retry");
   remote=new Transport(runtime){LoseStop=true};client=SupervisorSessionClient.Start(remote,Guid.NewGuid(),new string('3',64),Guid.NewGuid(),15,"lost-stop",false);Join(client.Running);worker=launcher.Last;client.Stop();Wait(()=>client.Completion.IsCompleted);Need(!client.ResourcesReleased,"lost-stop-reply-retains-client-owner");Join(client.RetryCleanupAsync());Need(client.ResourcesReleased&&worker.Stops==1,"duplicate-stop-after-lost-reply-cleans-once");
   remote=new Transport(runtime){HoldStart=true};client=SupervisorSessionClient.Start(remote,Guid.NewGuid(),new string('4',64),Guid.NewGuid(),16,"late-running",false);Need(remote.ReplyEntered.Wait(3000),"running-reply-held-before-delivery");client.Stop();remote.ReplyRelease.Set();Join(client.Completion);Need(client.Running.IsCanceled&&client.ResourcesReleased,"late-running-reply-cannot-revive-stopped-client");
   launcher.Entered.Reset();launcher.Release.Reset();var closingRegistry=new OwnedSessionRegistry(runtime,2,()=>clock.ElapsedMilliseconds);token=closingRegistry.Reserve(next(17));owned=closingRegistry.Start(token);Need(launcher.Entered.Wait(3000),"close-boundary-launch-entered");var closing=closingRegistry.CloseAsync();Need(!closing.IsCompleted,"close-waits-for-pending-owner-not-only-known-worker");launcher.Release.Set();Join(closing);Need(owned.ResourcesReleased&&launcher.Last.Disposed==1,"close-cleans-late-created-worker");
   string descriptor=System.IO.Path.Combine(System.IO.Path.GetDirectoryName(args[0]),"endpoint.json");
   using(var service=new Host.Service(runtime,journal,descriptor,null)){
    var pipe=new SupervisorManagerConnection(descriptor);client=SupervisorSessionClient.Start(pipe,Guid.NewGuid(),new string('1',64),Guid.NewGuid(),13,"real-pipe",false);Join(client.Running);Need(client.Identity!=null,"real-host-pipe-client-running-identity");
    var destination=new byte[640*360*4];var frame=pipe.ReadSessionFrame(client,640,360,0,destination);Join(frame);Need(frame.Result>0&&destination[0]==17&&destination[4]==29&&destination[destination.Length-1]==255,"real-host-pipe-exact-frame-pixels");
    launcher.Last.Frames=false;Thread.Sleep(30);frame=pipe.ReadFrame(client.Identity,640,360,0,destination);Join(frame);long delivered=frame.Result;
    destination[0]=93;frame=pipe.ReadFrame(client.Identity,640,360,delivered,destination);Join(frame);Need(frame.Result==0&&destination[0]==93,"unchanged-frame-not-replayed-or-copied");
    rejected=false;try{pipe.ReadFrame(new WorkerIdentity(Guid.NewGuid(),client.Identity.Instance,client.Identity.Generation),640,360,0,destination).GetAwaiter().GetResult();}catch(System.IO.InvalidDataException){rejected=true;}Need(rejected&&destination[0]==93,"foreign-attempt-rejected-before-pixel-copy");
    rejected=false;try{pipe.ReadFrame(new WorkerIdentity(client.Identity.Attempt,client.Identity.Instance,client.Identity.Generation+1),640,360,0,destination).GetAwaiter().GetResult();}catch(System.IO.InvalidDataException){rejected=true;}Need(rejected&&destination[0]==93,"foreign-generation-rejected-before-pixel-copy");
    client.Stop();Join(client.Completion);Need(client.ResourcesReleased,"real-host-pipe-stop-cleanup-forget");
    rejected=false;try{pipe.ReadFrame(client.Identity,640,360,0,destination).GetAwaiter().GetResult();}catch(System.IO.InvalidDataException){rejected=true;}Need(rejected&&destination[0]==93,"stopped-session-cannot-read-retained-pixels");
    var faulted=SupervisorSessionClient.Start(pipe,Guid.NewGuid(),new string('6',64),Guid.NewGuid(),19,"frame-fault",false);Join(faulted.Running);worker=launcher.Last;worker.HoldDispose=true;worker.ProtocolFault=true;
    try{Need(worker.DisposeEntered.Wait(3000)&&!faulted.Completion.IsCompleted,"faulted-frame-worker-awaiting-cleanup");rejected=false;try{pipe.ReadFrame(faulted.Identity,640,360,0,destination).GetAwaiter().GetResult();}catch(System.IO.InvalidDataException){rejected=true;}Need(rejected&&destination[0]==93,"faulted-session-cannot-publish-retained-pixels");}
    finally{worker.DisposeRelease.Set();faulted.Stop();try{Join(faulted.Completion);}catch{if(!faulted.ResourcesReleased)throw;}}
   }
   FrameTransportChecks.Run(System.IO.Path.GetDirectoryName(args[0]));
   InspectionTransportChecks.Run(System.IO.Path.GetDirectoryName(args[0]));
   launcher.Failure=new string('x',5000);remote=new Transport(runtime);client=SupervisorSessionClient.Start(remote,Guid.NewGuid(),new string('5',64),Guid.NewGuid(),18,"long-error",false);Wait(()=>client.Completion.IsCompleted);Need(client.Completion.IsFaulted&&client.ResourcesReleased,"oversized-diagnostic-does-not-block-cleanup-protocol");launcher.Failure=null;
   var inspectionEntered=new ManualResetEventSlim();var inspectionRelease=new ManualResetEventSlim();
   try{
    var firstInspection=runtime.Inspect(()=>{inspectionEntered.Set();if(!inspectionRelease.Wait(5000))throw new TimeoutException("controlled-inspection-release");return 17;});Need(inspectionEntered.Wait(3000),"inspection-enters-shared-launch-lane");
    var secondInspection=runtime.Inspect(()=>23);var extraInspection=runtime.Inspect(()=>29);Need(extraInspection.IsFaulted,"inspection-count-and-memory-reservations-reject-over-capacity");
    var blockedLaunch=runtime.Start(new LaunchRequest(Guid.NewGuid(),new string('9',64),Guid.NewGuid(),1,StartIntent.Manual,"blocked-by-inspections",RuntimeLimits.ProcessBytes));Need(blockedLaunch.IsFaulted,"inspection-reservations-also-bound-worker-starts");
    inspectionRelease.Set();Join(firstInspection);Join(secondInspection);Need(firstInspection.Result==17&&secondInspection.Result==23,"inspection-results-return-after-owned-operations");
    bool failedInspection=false;try{runtime.Inspect<int>(()=>{throw new System.IO.InvalidDataException("controlled-inspection-failure");}).GetAwaiter().GetResult();}catch(System.IO.InvalidDataException){failedInspection=true;}Need(failedInspection,"inspection-failure-preserved");var afterFailure=runtime.Inspect(()=>47);Join(afterFailure);Need(afterFailure.Result==47,"failed-inspection-releases-reservation");
   }finally{inspectionRelease.Set();}
   foreach(bool failInspection in new[]{false,true}){
    var operationEntered=new ManualResetEventSlim();var operationRelease=new ManualResetEventSlim();var operationReturned=new ManualResetEventSlim();
    var publication=runtime.Inspect(()=>{operationEntered.Set();if(!operationRelease.Wait(5000))throw new TimeoutException("controlled-inspection-publication-release");operationReturned.Set();if(failInspection)throw new System.IO.InvalidDataException("controlled-inspection-publication-failure");return 31;});
    try{Need(operationEntered.Wait(3000),"inspection-publication-operation-entered-"+failInspection);var reservationGate=typeof(SupervisorRuntime).GetField("gate",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).GetValue(runtime);
     lock(reservationGate){operationRelease.Set();Need(operationReturned.Wait(3000),"inspection-operation-finished-before-reservation-release-"+failInspection);Thread.Sleep(100);Need(!publication.IsCompleted,"inspection-completion-must-follow-reservation-release-"+failInspection);}
     bool failedPublication=false;try{Join(publication);}catch(AggregateException error){failedPublication=error.GetBaseException() is System.IO.InvalidDataException;}Need(failInspection?failedPublication:publication.Result==31,"inspection-publication-after-release-"+failInspection);
    }finally{operationRelease.Set();}
   }
   var closeEntered=new ManualResetEventSlim();var closeRelease=new ManualResetEventSlim();var inspectClosing=new SupervisorRuntime(journal,launcher,new WatchdogPolicy(15000,1000,500,RuntimeLimits.ProcessBytes,1000,1000),1,RuntimeLimits.ProcessBytes,()=>clock.ElapsedMilliseconds);
   try{var pendingInspection=inspectClosing.Inspect(()=>{closeEntered.Set();if(!closeRelease.Wait(5000))throw new TimeoutException("controlled-closing-inspection-release");return 53;});Need(closeEntered.Wait(3000),"inspection-active-before-close");inspectClosing.Close();Need(!inspectClosing.Completion.IsCompleted,"backend-close-waits-for-owned-inspection");Need(inspectClosing.Inspect(()=>59).IsFaulted,"closed-backend-rejects-new-inspection");closeRelease.Set();Join(pendingInspection);Join(inspectClosing.Completion);Need(pendingInspection.Result==53,"active-inspection-completes-before-backend-release");}
   finally{closeRelease.Set();inspectClosing.Close();Join(inspectClosing.Completion);}
   string stalledPipe="SpatialWallpaper.Supervisor."+Guid.NewGuid().ToString("N");var connected=new ManualResetEventSlim();
   using(var server=new System.IO.Pipes.NamedPipeServerStream(stalledPipe,System.IO.Pipes.PipeDirection.InOut,1,System.IO.Pipes.PipeTransmissionMode.Byte,System.IO.Pipes.PipeOptions.Asynchronous)){
    var disconnected=Task.Run(()=>{server.WaitForConnection();connected.Set();using(var reader=new System.IO.BinaryReader(server,System.Text.Encoding.UTF8,true)){int count=reader.ReadInt32();Need(reader.ReadBytes(count).Length==count,"stalled-manager-received-request");return server.ReadByte()==-1;}});
    System.IO.File.WriteAllText(descriptor,new Newtonsoft.Json.Linq.JObject{{"version",1},{"pipe",stalledPipe},{"auth",Convert.ToBase64String(new byte[32])},{"session_host",Guid.NewGuid().ToString("N")}}.ToString());
    var timed=new SupervisorManagerConnection(descriptor);var timer=System.Diagnostics.Stopwatch.StartNew();rejected=false;try{timed.Call("Status",new Newtonsoft.Json.Linq.JObject()).GetAwaiter().GetResult();}catch(TimeoutException){rejected=true;}
    Need(rejected&&connected.IsSet&&timer.ElapsedMilliseconds<5000,"stalled-manager-read-has-bounded-timeout");Join(disconnected);Need(disconnected.Result,"timed-out-client-disposes-pipe-before-return");
   }
  }finally{launcher.Release.Set();runtime.Close();Join(runtime.Completion);journal.Close();Join(journal.Completion);}
  return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
