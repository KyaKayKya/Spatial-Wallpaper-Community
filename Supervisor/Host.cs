using System;using System.IO;using System.IO.Pipes;using System.Text;using System.Linq;using System.Threading;using System.Threading.Tasks;using System.Diagnostics;using System.Collections.Generic;using System.Security.Cryptography;using System.Security.AccessControl;using System.Security.Principal;using Newtonsoft.Json;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision.Windows;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Supervision {
 // Trusted per-user service. Manager connections carry no Worker capability messages and may freely disconnect.
 static class Host {
  static JObject ReadJson(string text){using(var input=new StringReader(text))using(var reader=new JsonTextReader(input){MaxDepth=12,DateParseHandling=DateParseHandling.None}){var result=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new InvalidDataException("JSON-tail");return result;}}
  static void Fields(JObject value,params string[] names){if(value==null||!value.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(names.OrderBy(n=>n,StringComparer.Ordinal)))throw new InvalidDataException("command-fields");}
  static string Text(JToken value){if(value==null||value.Type!=JTokenType.String||((string)value).Length>2048)throw new InvalidDataException("command-string");return (string)value;}
  static string ReadFile(string path,int maximum){var info=new FileInfo(path);if(info.Length<1||info.Length>maximum||(info.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("metadata-file");return File.ReadAllText(path,new UTF8Encoding(false,true));}
  static Task Wait(Task task){if(!task.Wait(5000))throw new TimeoutException("Supervisor-close-deadline");task.GetAwaiter().GetResult();return task;}
  static void PrivateFile(string path){var acl=new FileSecurity();acl.SetAccessRuleProtection(true,false);foreach(var sid in new[]{WindowsIdentity.GetCurrent().User,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,AccessControlType.Allow));File.SetAccessControl(path,acl);}
  internal sealed class Service:IDisposable {
   sealed class Inspection {internal string Path;internal long CompletedAt;internal Task<ValidationReceipt> Result;}
   readonly Func<string,ValidationReceipt> inspector;readonly Dictionary<Guid,Inspection> inspections=new Dictionary<Guid,Inspection>();
   readonly Guid hostIdentity=Guid.NewGuid();readonly OwnedSessionRegistry owned;readonly OwnedSessionProtocol ownedProtocol;
   readonly object gate=new object();readonly JournalActor journal;readonly SupervisorRuntime runtime;readonly byte[] authentication;readonly string pipeName;readonly PipeSecurity security;
   readonly Dictionary<string,Task<SupervisedSession>> instances=new Dictionary<string,Task<SupervisedSession>>(StringComparer.Ordinal);readonly JArray recent=new JArray();
   readonly HashSet<string> stopRequested=new HashSet<string>(StringComparer.Ordinal);
   readonly NativeInputRegistry nativeInputs;
   readonly SpatialWallpaper.Supervision.Broker.StorageBroker storage;readonly Dictionary<Guid,Task<SpatialWallpaper.Supervision.Broker.StorageResult>> uninstalling=new Dictionary<Guid,Task<SpatialWallpaper.Supervision.Broker.StorageResult>>();
   readonly ManualResetEventSlim exit=new ManualResetEventSlim();readonly SemaphoreSlim clients=new SemaphoreSlim(RuntimeLimits.ManagerClients,RuntimeLimits.ManagerClients);readonly Thread acceptor;Exception acceptFailure;
   internal Service(SupervisorRuntime supervisor,JournalActor persistence,string descriptor,SpatialWallpaper.Supervision.Broker.AudioBroker audio,Func<string,ValidationReceipt> packageInspector=null,SpatialWallpaper.Supervision.Broker.StorageBroker storageBroker=null,SpatialWallpaper.Sdk.Capabilities.InputBroker inputBroker=null){
    inspector=packageInspector;storage=storageBroker;
    runtime=supervisor;journal=persistence;var ownershipClock=Stopwatch.StartNew();owned=new OwnedSessionRegistry(runtime,RuntimeLimits.MaxInstances,()=>ownershipClock.ElapsedMilliseconds);ownedProtocol=new OwnedSessionProtocol(owned,hostIdentity);authentication=new byte[32];using(var random=RandomNumberGenerator.Create())random.GetBytes(authentication);pipeName="SpatialWallpaper.Supervisor."+Guid.NewGuid().ToString("N");
    security=new PipeSecurity();security.SetAccessRuleProtection(true,false);foreach(var sid in new[]{WindowsIdentity.GetCurrent().User,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})security.AddAccessRule(new PipeAccessRule(sid,PipeAccessRights.FullControl,AccessControlType.Allow));
    // descriptor directory belongs to this backend; package content never supplies its path or authentication.
    var description=new JObject{{"version",1},{"pipe",pipeName},{"auth",Convert.ToBase64String(authentication)},{"session_host",hostIdentity.ToString("N")}};
    using(var processOwner=PinnedProcessOwner.PinCurrent())description["host_process"]=new JObject{{"pid",processOwner.Id},{"creation_filetime",processOwner.CreationFileTime}};
    if(audio!=null)using(var owner=Process.GetCurrentProcess())description["audio_sampler"]=new JObject{{"host_pid",owner.Id},{"host_creation_filetime",RestrictedProcess.ReadCreationFileTime(owner.Handle)},{"pid",audio.SamplerProcessId},{"creation_filetime",audio.SamplerCreationFileTime}};
    string temporary=descriptor+"."+Guid.NewGuid().ToString("N")+".tmp";File.WriteAllText(temporary,description.ToString(Formatting.None),new UTF8Encoding(false));PrivateFile(temporary);
    if(File.Exists(descriptor))File.Replace(temporary,descriptor,null);else File.Move(temporary,descriptor);
    nativeInputs=inputBroker==null?null:new NativeInputRegistry(inputBroker);
    acceptor=new Thread(Accept){IsBackground=true,Name="SpatialWallpaper manager accept"};try{acceptor.Start();}catch{nativeInputs?.Dispose();throw;}
   }
   bool Auth(string value){byte[] bytes;try{bytes=Convert.FromBase64String(value);}catch(FormatException){return false;}if(bytes.Length!=32)return false;int different=0;for(int i=0;i<32;i++)different|=bytes[i]^authentication[i];return different==0;}
   static void Exact(Stream stream,byte[] bytes,long deadline){int at=0;while(at<bytes.Length){int remaining=(int)Math.Max(0,(deadline-Stopwatch.GetTimestamp())*1000/Stopwatch.Frequency);if(remaining==0)throw new TimeoutException("manager-read-deadline");var pending=stream.ReadAsync(bytes,at,bytes.Length-at);if(!pending.Wait(remaining))throw new TimeoutException("manager-read-deadline");int n=pending.GetAwaiter().GetResult();if(n==0)throw new EndOfStreamException();at+=n;}}
   static JObject Receive(Stream stream){long deadline=Stopwatch.GetTimestamp()+RuntimeLimits.ManagerIoMs*Stopwatch.Frequency/1000;var header=new byte[4];Exact(stream,header,deadline);int length=BitConverter.ToInt32(header,0);if(length<1||length>RuntimeLimits.ManagerRequestBytes)throw new InvalidDataException("manager-message-size");var bytes=new byte[length];Exact(stream,bytes,deadline);return ReadJson(new UTF8Encoding(false,true).GetString(bytes));}
   static void Send(Stream stream,JObject result){var bytes=Encoding.UTF8.GetBytes(result.ToString(Formatting.None));if(bytes.Length>RuntimeLimits.ManagerReplyBytes)throw new InvalidDataException("manager-reply-size");var header=BitConverter.GetBytes(bytes.Length);var pending=stream.WriteAsync(header,0,4);if(!pending.Wait(RuntimeLimits.ManagerIoMs))throw new TimeoutException();pending.GetAwaiter().GetResult();pending=stream.WriteAsync(bytes,0,bytes.Length);if(!pending.Wait(RuntimeLimits.ManagerIoMs))throw new TimeoutException();pending.GetAwaiter().GetResult();}
   void Accept(){try{while(!exit.IsSet){
    if(!clients.Wait(50))continue;NamedPipeServerStream pipe=null;
    try{pipe=new NamedPipeServerStream(pipeName,PipeDirection.InOut,RuntimeLimits.ManagerClients,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,RuntimeLimits.ManagerRequestBytes,RuntimeLimits.ManagerReplyBytes,security);var connection=pipe.WaitForConnectionAsync();while(!connection.Wait(50))if(exit.IsSet){pipe.Dispose();clients.Release();return;}connection.GetAwaiter().GetResult();}
    catch{pipe?.Dispose();clients.Release();if(exit.IsSet)return;throw;}
    var accepted=pipe;new Thread(()=>{bool terminate=false,binaryReply=false;try{var command=Receive(accepted);Fields(command,"auth","operation","payload");if(!Auth(Text(command["auth"]))){Send(accepted,new JObject{{"ok",false},{"error","authentication"}});return;}string operation=Text(command["operation"]);
     if(operation=="ReadFrame"){var frame=ReadFrame(command["payload"] as JObject);binaryReply=true;Send(accepted,new JObject{{"ok",true},{"host",hostIdentity.ToString("N")},{"available",frame!=null}});if(frame!=null){long deadline=Stopwatch.GetTimestamp()+RuntimeLimits.ManagerIoMs*Stopwatch.Frequency/1000;WriteFrame(accepted,frame.Item1,deadline);WriteFrame(accepted,frame.Item2,deadline);}return;}
     if(operation=="OpenPreviewInput"||operation=="OpenDesktopInput"||operation=="OpenNativePreviewInput"||operation=="OpenNativeDesktopInput"){bool native=operation.StartsWith("OpenNative",StringComparison.Ordinal),preview=operation=="OpenPreviewInput"||operation=="OpenNativePreviewInput";var target=native?NativeInputTargetFor(command["payload"] as JObject,preview):InputTargetFor(command["payload"] as JObject,preview);if(preview)SpatialWallpaper.Supervision.Broker.PreviewInputChannel.Serve(accepted,target);else SpatialWallpaper.Supervision.Broker.DesktopInputChannel.Serve(accepted,target);return;}
     var reply=Dispatch(operation,command["payload"] as JObject);terminate=operation=="Exit"&&(bool?)reply["ok"]==true;Send(accepted,reply);}catch(Exception error){if(!binaryReply)try{Send(accepted,new JObject{{"ok",false},{"error",error is ArgumentException||error is InvalidDataException?"invalid-command":"manager-operation-failed"}});}catch{}}finally{accepted.Dispose();clients.Release();if(terminate)exit.Set();}}){IsBackground=true,Name="SpatialWallpaper manager request"}.Start();
   }}catch(Exception error){acceptFailure=error;exit.Set();}}
   static void WriteFrame(Stream stream,byte[] bytes,long deadline){
    int remaining=(int)Math.Max(0,(deadline-Stopwatch.GetTimestamp())*1000/Stopwatch.Frequency);if(remaining==0)throw new TimeoutException("presenter-write-deadline");
    var pending=stream.WriteAsync(bytes,0,bytes.Length);if(!pending.Wait(remaining)){pending.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);throw new TimeoutException("presenter-write-deadline");}pending.GetAwaiter().GetResult();
   }
   Tuple<byte[],byte[]> ReadFrame(JObject payload){lock(gate){
    Fields(payload,"host","attempt","instance","generation","width","height","after");
    if(Text(payload["host"])!=hostIdentity.ToString("N"))throw new InvalidDataException("frame-host");
    foreach(string key in new[]{"generation","width","height","after"})if(payload[key].Type!=JTokenType.Integer)throw new InvalidDataException("frame-request-number");
    var identity=new WorkerIdentity(Guid.ParseExact(Text(payload["attempt"]),"N"),Guid.ParseExact(Text(payload["instance"]),"N"),(uint)payload["generation"]);
    int width=(int)payload["width"],height=(int)payload["height"];long after=(long)payload["after"];if(after<0)throw new InvalidDataException("frame-request-sequence");RuntimeLimits.RequireRenderSize(width,height);
    SupervisedSession session;if(instances.TryGetValue(identity.Instance.ToString("N"),out var task)&&task.Status==TaskStatus.RanToCompletion)session=task.Result;else session=owned.Find(identity);
    if(session==null||session.Attempt!=identity.Attempt||session.Request.Generation!=identity.Generation||session.Fault.HasValue||session.StopRequested||session.Completion.IsCompleted||session.Ready.Status!=TaskStatus.RanToCompletion)throw new InvalidDataException("frame-session-unavailable");
    var source=session.Worker as IWorkerFrameSource;if(source==null||source.FrameWidth!=width||source.FrameHeight!=height)throw new InvalidDataException("frame-source-shape");
    var pixels=new byte[WorkerFrameCodec.PayloadBytes(width,height)];long sequence,revision;if(!source.CopyFrame(pixels,out sequence,out revision)||sequence<=after)return null;
    if(session.Fault.HasValue||session.StopRequested||session.Completion.IsCompleted)throw new InvalidDataException("frame-session-stopped");
    var header=new byte[WorkerFrameCodec.HeaderBytes];WorkerFrameCodec.Encode(header,identity,width,height,sequence,revision);return Tuple.Create(header,pixels);
   }}
   SpatialWallpaper.Sdk.Capabilities.InputTarget InputTargetFor(JObject payload,bool preview){lock(gate){
    Fields(payload,"instance","attempt","generation");string instance=Text(payload["instance"]);if(payload["generation"].Type!=JTokenType.Integer)throw new InvalidDataException("input-generation");
    var identity=new WorkerIdentity(Guid.ParseExact(Text(payload["attempt"]),"N"),Guid.ParseExact(instance,"N"),(uint)payload["generation"]);
    SupervisedSession session;if(instances.TryGetValue(instance,out var task)&&task.Status==TaskStatus.RanToCompletion)session=task.Result;else session=owned.Find(identity);
    if(session==null)throw new InvalidDataException("input-instance-unavailable");var worker=session.Worker as WindowsManagedWorker;
    if(session.Request.Preview!=preview||session.StopRequested||session.Completion.IsCompleted||session.Ready.Status!=TaskStatus.RanToCompletion||worker?.Input==null||!worker.Input.Matches(identity))throw new InvalidDataException("input-target-unavailable");
    return worker.Input;
   }}
   SpatialWallpaper.Sdk.Capabilities.InputTarget NativeInputTargetFor(JObject payload,bool preview){
    Fields(payload,"host","instance","attempt","generation","renderer_pid","renderer_creation_filetime");
    if(nativeInputs==null||Text(payload["host"])!=hostIdentity.ToString("N")||payload["generation"].Type!=JTokenType.Integer||payload["renderer_pid"].Type!=JTokenType.Integer||payload["renderer_creation_filetime"].Type!=JTokenType.Integer)throw new InvalidDataException("native-input-owner");
    var identity=new WorkerIdentity(Guid.ParseExact(Text(payload["attempt"]),"N"),Guid.ParseExact(Text(payload["instance"]),"N"),(uint)payload["generation"]);
    return nativeInputs.Find(identity,preview,(int)payload["renderer_pid"],(long)payload["renderer_creation_filetime"])??throw new InvalidDataException("native-input-target-unavailable");
   }
   JObject StatusCore(){
    var statuses=new JObject();foreach(var pair in instances.ToArray()){
     var task=pair.Value;string state="Starting",reason="";bool terminal=false;
     if(task.IsFaulted||task.IsCanceled){state="Failed";reason="start-failed";terminal=true;}
     else if(task.Status==TaskStatus.RanToCompletion){var session=task.Result;if(session.Completion.IsCompleted){state=session.Fault.HasValue?"Faulted":session.Completion.IsFaulted?"CleanupFailed":"Stopped";reason=session.Fault?.ToString()??"";terminal=true;}else if(session.Ready.Status==TaskStatus.RanToCompletion)state="Running";else if(session.Fault.HasValue){state="Faulted";reason=session.Fault.ToString();}}
     var status=new JObject{{"state",state},{"reason",reason}};if(task.Status==TaskStatus.RanToCompletion){status["attempt"]=task.Result.Attempt.ToString("N");status["stop_evidence"]=task.Result.StopEvidence;status["worker_pid"]=task.Result.Worker.ProcessId;status["worker_creation_filetime"]=task.Result.Worker is IWorkerProcessIdentity identity?(JToken)identity.CreationFileTime:JValue.CreateNull();}
     if(task.IsFaulted&&task.Exception.InnerException is LaunchFailureException launch){status["attempt"]=launch.Attempt.ToString("N");status["reason"]=launch.Reason.ToString();}
     if(terminal){instances.Remove(pair.Key);stopRequested.Remove(pair.Key);recent.Add(new JObject{{"instance",pair.Key},{"result",status}});while(recent.Count>32)recent.RemoveAt(0);}else statuses.Add(pair.Key,status);
    }
    return new JObject{{"ok",true},{"instances",statuses},{"recent",recent.DeepClone()}};
   }
   JObject Dispatch(string operation,JObject payload){lock(gate){
    if(payload==null)throw new InvalidDataException("command-payload");StatusCore();
    foreach(var completed in uninstalling.Where(p=>p.Value.IsCompleted).Select(p=>p.Key).ToArray())uninstalling.Remove(completed);
    if(OwnedSessionProtocol.Handles(operation))return ownedProtocol.Dispatch(operation,payload);
    switch(operation){
     case "RegisterNativeInput":case "RegisterNativeOrderedInput":case "UnregisterNativeInput":case "ReadNativeInput":case "ReadNativeOrderedInput":case "OpenNativeInputAuthority":
      bool registerInput=operation=="RegisterNativeInput"||operation=="RegisterNativeOrderedInput";
      bool orderedInput=operation=="ReadNativeOrderedInput";
      bool readInput=operation=="ReadNativeInput"||orderedInput||operation=="OpenNativeInputAuthority";
      Fields(payload,registerInput?new[]{"host","attempt","instance","generation","renderer_pid","renderer_creation_filetime","preview"}:orderedInput?new[]{"host","attempt","instance","generation","renderer_pid","renderer_creation_filetime","cursor"}:readInput?new[]{"host","attempt","instance","generation","renderer_pid","renderer_creation_filetime"}:new[]{"host","attempt","instance","generation"});
      if(nativeInputs==null||Text(payload["host"])!=hostIdentity.ToString("N")||payload["generation"].Type!=JTokenType.Integer)throw new InvalidDataException("native-input-host-or-generation");
      var nativeIdentity=new WorkerIdentity(Guid.ParseExact(Text(payload["attempt"]),"N"),Guid.ParseExact(Text(payload["instance"]),"N"),(uint)payload["generation"]);
      var nativeReply=new JObject{{"ok",true},{"host",hostIdentity.ToString("N")},{"attempt",nativeIdentity.Attempt.ToString("N")},{"instance",nativeIdentity.Instance.ToString("N")},{"generation",nativeIdentity.Generation}};
      if(registerInput){
       if(payload["renderer_pid"].Type!=JTokenType.Integer||payload["renderer_creation_filetime"].Type!=JTokenType.Integer||payload["preview"].Type!=JTokenType.Boolean)throw new InvalidDataException("native-input-renderer-identity");
       if(operation=="RegisterNativeOrderedInput")nativeInputs.RegisterOrdered(nativeIdentity,(bool)payload["preview"],(int)payload["renderer_pid"],(long)payload["renderer_creation_filetime"]);
       else nativeInputs.Register(nativeIdentity,(bool)payload["preview"],(int)payload["renderer_pid"],(long)payload["renderer_creation_filetime"]);
      }else if(operation=="UnregisterNativeInput")nativeReply["removed"]=nativeInputs.Remove(nativeIdentity);
      else{
       if(payload["renderer_pid"].Type!=JTokenType.Integer||payload["renderer_creation_filetime"].Type!=JTokenType.Integer)throw new InvalidDataException("native-input-renderer-identity");
       if(operation=="OpenNativeInputAuthority")nativeReply["section"]=nativeInputs.OpenAuthority(nativeIdentity,(int)payload["renderer_pid"],(long)payload["renderer_creation_filetime"]);
       else if(orderedInput){
        if(payload["cursor"].Type!=JTokenType.Integer||(long)payload["cursor"]<0)throw new InvalidDataException("native-input-event-cursor");
        var inputBatch=nativeInputs.OrderedSnapshot(nativeIdentity,(int)payload["renderer_pid"],(long)payload["renderer_creation_filetime"],(long)payload["cursor"]);
        nativeReply["input"]=Convert.ToBase64String(inputBatch.Snapshot);nativeReply["cursor"]=inputBatch.Cursor;nativeReply["events"]=new JArray(inputBatch.Events.Select(e=>new JArray(e.Sequence,e.Time,e.Epoch,e.Held,e.Pressed,e.Released,e.Wheel)));
       }else nativeReply["input"]=Convert.ToBase64String(nativeInputs.Snapshot(nativeIdentity,(int)payload["renderer_pid"],(long)payload["renderer_creation_filetime"]));
      }
      return nativeReply;
     case "UninstallInstallation":
      Fields(payload,"host","installation");if(Text(payload["host"])!=hostIdentity.ToString("N"))throw new InvalidDataException("uninstall-host");
      var installation=Guid.ParseExact(Text(payload["installation"]),"N");if(installation==Guid.Empty||storage==null)throw new InvalidDataException("uninstall-installation-or-storage");
      if(!uninstalling.TryGetValue(installation,out var removal)){
       if(owned.ContainsInstallation(installation)||!runtime.CanUninstall(installation))throw new InvalidOperationException("uninstall-installation-still-owned");
       removal=storage.Uninstall(installation);uninstalling.Add(installation,removal);
      }
      // A caller timeout never releases the start guard before disk work finishes.
      if(!removal.Wait(1000))return new JObject{{"ok",false},{"error","uninstall-storage-pending"}};
      var removed=removal.GetAwaiter().GetResult();uninstalling.Remove(installation);
      return new JObject{{"ok",removed.Failure==SpatialWallpaper.Supervision.Broker.StorageFailure.None},{"host",hostIdentity.ToString("N")},{"installation",installation.ToString("N")},{"storage_deleted",removed.Failure==SpatialWallpaper.Supervision.Broker.StorageFailure.None},{"error",removed.Failure==SpatialWallpaper.Supervision.Broker.StorageFailure.None?null:"uninstall-storage-"+removed.Failure}};
     case "ReadWorkerDiagnostics":
      Fields(payload,"host","attempt","instance","generation");if(Text(payload["host"])!=hostIdentity.ToString("N")||payload["generation"].Type!=JTokenType.Integer)throw new InvalidDataException("worker-diagnostics-identity");
      var logIdentity=new WorkerIdentity(Guid.ParseExact(Text(payload["attempt"]),"N"),Guid.ParseExact(Text(payload["instance"]),"N"),(uint)payload["generation"]);SupervisedSession logSession;
      if(instances.TryGetValue(logIdentity.Instance.ToString("N"),out var logTask)&&logTask.Status==TaskStatus.RanToCompletion)logSession=logTask.Result;else logSession=owned.Find(logIdentity);
      if(logSession==null||logSession.Attempt!=logIdentity.Attempt||logSession.Request.Generation!=logIdentity.Generation||logSession.StopRequested||logSession.Fault.HasValue||logSession.Completion.IsCompleted||logSession.Ready.Status!=TaskStatus.RanToCompletion||!(logSession.Worker is WindowsManagedWorker logWorker))throw new InvalidDataException("worker-diagnostics-session-unavailable");
      var rawLog=logWorker.ReadLog();int logStart=Math.Max(0,rawLog.Length-8192);return new JObject{{"ok",true},{"host",hostIdentity.ToString("N")},{"attempt",logIdentity.Attempt.ToString("N")},{"instance",logIdentity.Instance.ToString("N")},{"generation",logIdentity.Generation},{"log_base64",Convert.ToBase64String(rawLog,logStart,rawLog.Length-logStart)},{"scope","bounded-untrusted-Worker-log"}};
     case "BeginPackageInspection":case "PollPackageInspection":
      bool begin=operation=="BeginPackageInspection";if(begin)Fields(payload,"host","ticket","package");else Fields(payload,"host","ticket");
      if(Text(payload["host"])!=hostIdentity.ToString("N"))throw new InvalidDataException("inspection-host");var ticket=Guid.ParseExact(Text(payload["ticket"]),"N");if(ticket==Guid.Empty)throw new InvalidDataException("inspection-ticket");
      long time=Stopwatch.GetTimestamp();foreach(var old in inspections.Where(p=>Interlocked.Read(ref p.Value.CompletedAt)>0&&time-Interlocked.Read(ref p.Value.CompletedAt)>15L*Stopwatch.Frequency).Select(p=>p.Key).ToArray())inspections.Remove(old);
      inspections.TryGetValue(ticket,out var inspection);
      if(begin){string path=Path.GetFullPath(Text(payload["package"]));if(inspection!=null){if(!string.Equals(inspection.Path,path,StringComparison.OrdinalIgnoreCase))throw new InvalidDataException("inspection-ticket-path");}else{if(inspector==null||inspections.Count>=64)throw new InvalidOperationException("inspection-unavailable");inspection=new Inspection{Path=path,Result=runtime.Inspect(()=>inspector(path))};inspections.Add(ticket,inspection);var entry=inspection;entry.Result.ContinueWith(done=>{var observed=done.Exception;Interlocked.Exchange(ref entry.CompletedAt,Stopwatch.GetTimestamp());},TaskContinuationOptions.ExecuteSynchronously);}}
      string inspectionState=inspection==null?"Absent":!inspection.Result.IsCompleted?"Pending":inspection.Result.Status==TaskStatus.RanToCompletion?"Succeeded":"Failed";ValidationReceipt inspected=inspectionState=="Succeeded"?inspection.Result.Result:null;
      string inspectionError=null;if(inspectionState=="Failed"){var cause=inspection.Result.Exception?.GetBaseException();inspectionError=cause?.Message??"inspection-cancelled";if(inspectionError.Length>256)inspectionError=inspectionError.Substring(0,256);}
      return new JObject{{"ok",true},{"host",hostIdentity.ToString("N")},{"ticket",ticket.ToString("N")},{"state",inspectionState},{"build",inspected?.BuildId},{"archive_sha256",inspected?.PackageDigest},{"capabilities",inspected==null?new JArray():new JArray(inspected.Capabilities)},{"error",inspectionError}};
     case "Status":Fields(payload);return StatusCore();
     case "Diagnostic":Fields(payload,"attempt");var diagnosis=journal.Diagnostic(Guid.ParseExact(Text(payload["attempt"]),"N"));if(!diagnosis.Wait(1000))throw new TimeoutException("diagnostic-read-deadline");return new JObject{{"ok",true},{"diagnostic",diagnosis.GetAwaiter().GetResult()}};
     case "SoundState":case "SetSoundMix":
      bool changeSound=operation=="SetSoundMix";Fields(payload,changeSound?new[]{"host","attempt","instance","generation","music","effects"}:new[]{"host","attempt","instance","generation"});if(Text(payload["host"])!=hostIdentity.ToString("N")||payload["generation"].Type!=JTokenType.Integer)throw new InvalidDataException("sound-session-identity");
      var soundIdentity=new WorkerIdentity(Guid.ParseExact(Text(payload["attempt"]),"N"),Guid.ParseExact(Text(payload["instance"]),"N"),(uint)payload["generation"]);
      float soundMusic=changeSound?SpatialWallpaper.Sdk.Capabilities.SoundMix.RequireGain(payload["music"]):0,soundEffects=changeSound?SpatialWallpaper.Sdk.Capabilities.SoundMix.RequireGain(payload["effects"]):0;
      SupervisedSession soundSession;if(instances.TryGetValue(soundIdentity.Instance.ToString("N"),out var soundTask)&&soundTask.Status==TaskStatus.RanToCompletion)soundSession=soundTask.Result;else soundSession=owned.Find(soundIdentity);
      if(soundSession==null||soundSession.Attempt!=soundIdentity.Attempt||soundSession.Request.Generation!=soundIdentity.Generation||soundSession.Fault.HasValue||soundSession.StopRequested||soundSession.Completion.IsCompleted||soundSession.Ready.Status!=TaskStatus.RanToCompletion||!(soundSession.Worker is WindowsManagedWorker soundWorker))throw new InvalidDataException("sound-session-unavailable");
      if(changeSound)soundWorker.ConfigureSound(soundMusic,soundEffects);
      return new JObject{{"ok",true},{"host",hostIdentity.ToString("N")},{"attempt",soundIdentity.Attempt.ToString("N")},{"instance",soundIdentity.Instance.ToString("N")},{"generation",soundIdentity.Generation},{"sound_error",soundWorker.SoundError}};
     case "SetProjection":
      Fields(payload,"host","attempt","instance","generation","revision","width","height","display_width","display_height","eye","preserve_width");
      if(Text(payload["host"])!=hostIdentity.ToString("N"))throw new InvalidDataException("projection-host");
      foreach(string key in new[]{"generation","revision","width","height"})if(payload[key].Type!=JTokenType.Integer)throw new InvalidDataException("projection-number");
      foreach(string key in new[]{"display_width","display_height"})if(payload[key].Type!=JTokenType.Integer&&payload[key].Type!=JTokenType.Float)throw new InvalidDataException("projection-number");
      var eye=payload["eye"] as JArray;if(eye==null||eye.Count!=3||eye.Any(n=>n.Type!=JTokenType.Integer&&n.Type!=JTokenType.Float)||payload["preserve_width"].Type!=JTokenType.Boolean)throw new InvalidDataException("projection-eye-or-fit");
      var projectionIdentity=new WorkerIdentity(Guid.ParseExact(Text(payload["attempt"]),"N"),Guid.ParseExact(Text(payload["instance"]),"N"),(uint)payload["generation"]);
      var configuration=new ProjectionConfiguration((long)payload["revision"],(int)payload["width"],(int)payload["height"],(float)payload["display_width"],(float)payload["display_height"],(float)eye[0],(float)eye[1],(float)eye[2],(bool)payload["preserve_width"]);RuntimeLimits.RequireRenderSize(configuration.Width,configuration.Height);
      SupervisedSession projectionSession;if(instances.TryGetValue(projectionIdentity.Instance.ToString("N"),out var projectionTask)&&projectionTask.Status==TaskStatus.RanToCompletion)projectionSession=projectionTask.Result;else projectionSession=owned.Find(projectionIdentity);
      if(projectionSession==null||projectionSession.Attempt!=projectionIdentity.Attempt||projectionSession.Request.Generation!=projectionIdentity.Generation||projectionSession.Fault.HasValue||projectionSession.StopRequested||projectionSession.Completion.IsCompleted||projectionSession.Ready.Status!=TaskStatus.RanToCompletion||!(projectionSession.Worker is WindowsManagedWorker projectionWorker))throw new InvalidDataException("projection-session-unavailable");
      projectionWorker.UpdateProjection(configuration);
      return new JObject{{"ok",true},{"host",hostIdentity.ToString("N")},{"attempt",projectionIdentity.Attempt.ToString("N")},{"instance",projectionIdentity.Instance.ToString("N")},{"generation",projectionIdentity.Generation},{"revision",configuration.Revision}};
     case "ReserveSession":case "ReservePreviewSession":
      Fields(payload,"host","installation","build","instance","generation","intent","package");if(Text(payload["host"])!=hostIdentity.ToString("N"))throw new InvalidDataException("session-host-mismatch");
      var reservedId=Guid.ParseExact(Text(payload["instance"]),"N");if(instances.ContainsKey(reservedId.ToString("N")))throw new InvalidDataException("instance");
      string reservedIntent=Text(payload["intent"]);if(reservedIntent!="Manual"&&reservedIntent!="Resume")throw new InvalidDataException("start-intent");if(payload["generation"].Type!=JTokenType.Integer)throw new InvalidDataException("generation");
      var reservedRequest=new LaunchRequest(Guid.ParseExact(Text(payload["installation"]),"N"),Text(payload["build"]),reservedId,(uint)payload["generation"],reservedIntent=="Manual"?StartIntent.Manual:StartIntent.Resume,Text(payload["package"]),RuntimeLimits.ProcessBytes,operation=="ReservePreviewSession");
      if(uninstalling.ContainsKey(reservedRequest.Installation))throw new InvalidOperationException("installation-uninstalling");
      return ownedProtocol.Status(owned.Reserve(reservedRequest));
     case "Start":case "StartPreview":
      Fields(payload,"installation","build","instance","generation","intent","package");if(instances.Count>=RuntimeLimits.MaxInstances)return new JObject{{"ok",false},{"error","instance-capacity"}};
      string instance=Text(payload["instance"]);var id=Guid.ParseExact(instance,"N");if(id==Guid.Empty||instances.ContainsKey(instance)||owned.ContainsInstance(id))throw new InvalidDataException("instance");
      string intent=Text(payload["intent"]);if(intent!="Manual"&&intent!="Resume")throw new InvalidDataException("start-intent");if(payload["generation"].Type!=JTokenType.Integer)throw new InvalidDataException("generation");
      var request=new LaunchRequest(Guid.ParseExact(Text(payload["installation"]),"N"),Text(payload["build"]),id,(uint)payload["generation"],intent=="Manual"?StartIntent.Manual:StartIntent.Resume,Text(payload["package"]),RuntimeLimits.ProcessBytes,operation=="StartPreview");
      if(uninstalling.ContainsKey(request.Installation))throw new InvalidOperationException("installation-uninstalling");
      var start=runtime.Start(request);if(start.IsFaulted)return new JObject{{"ok",false},{"error","start-rejected"}};instances.Add(instance,start);return new JObject{{"ok",true},{"instance",instance}};
     case "Stop":Fields(payload,"instance");string stopId=Text(payload["instance"]);if(!instances.TryGetValue(stopId,out var stopping))return new JObject{{"ok",false},{"error","instance-not-running"}};
      if(stopRequested.Add(stopId))stopping.ContinueWith(task=>{if(task.Status==TaskStatus.RanToCompletion)task.Result.Stop();},TaskScheduler.Default);return new JObject{{"ok",true}};
     case "Exit":Fields(payload);return new JObject{{"ok",true}};
     default:throw new InvalidDataException("manager-operation");
    }
   }}
   internal void Run(PinnedProcessOwner previewOwner=null){if(previewOwner==null)exit.Wait();else while(!exit.Wait(100)){if(previewOwner.HasExited)break;}if(acceptFailure!=null)throw new InvalidOperationException("manager-accept-failed",acceptFailure);}
   public void Dispose(){exit.Set();if(!acceptor.Join(3000))throw new TimeoutException("manager-accept-cleanup-deadline");try{Wait(owned.CloseAsync());}finally{nativeInputs?.Dispose();}}
  }
  static int Main(string[] args){var cleanup=new CleanupFailures();int result;try{result=args.Length==2&&args[0]=="--script-launch"?ScriptLaunchHost.Run(args[1]):Run(args,cleanup);}catch{result=1;}return cleanup.Failed?3:result;}
  static int Run(string[] args,CleanupFailures cleanup){HostExitTrace trace=null;PinnedProcessOwner previewOwner=null;try{
   if(args.Length!=1)throw new ArgumentException("Supervisor configuration path required");var config=ReadJson(ReadFile(args[0],16384));var fields=new List<string>(new[]{"image","inventory","validator","state","attempts","width","height"});foreach(string optional in new[]{"llm","audio","preview_owner","timing_fps"})if(config.ContainsKey(optional))fields.Add(optional);Fields(config,fields.ToArray());if(config.ContainsKey("preview_owner")){var owner=config["preview_owner"] as JObject;Fields(owner,"pid","creation_filetime");if(owner["pid"].Type!=JTokenType.Integer||owner["creation_filetime"].Type!=JTokenType.Integer)throw new InvalidDataException("preview-owner-identity");previewOwner=PinnedProcessOwner.Open((int)owner["pid"],(long)owner["creation_filetime"]);}var audioConfiguration=SpatialWallpaper.Supervision.Broker.AudioConfiguration.Parse(config["audio"]);int timingFps=0;if(config.ContainsKey("timing_fps")){if(config["timing_fps"].Type!=JTokenType.Integer||((int)config["timing_fps"]!=30&&(int)config["timing_fps"]!=60))throw new InvalidDataException("host-timing-fps");timingFps=(int)config["timing_fps"];}string state=Path.GetFullPath(Text(config["state"]));
   Directory.CreateDirectory(state);if((File.GetAttributes(state)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("Supervisor-state-link");
   string marker=Path.Combine(state,".sdk-supervisor-state");if(!File.Exists(marker)){if(Directory.EnumerateFileSystemEntries(state).Any())throw new InvalidDataException("Supervisor-state-not-owned");File.WriteAllText(marker,"SpatialWallpaper.Supervisor.v1");}else if(File.ReadAllText(marker)!="SpatialWallpaper.Supervisor.v1")throw new InvalidDataException("Supervisor-state-owner");
   var acl=new DirectorySecurity();acl.SetAccessRuleProtection(true,false);foreach(var sid in new[]{WindowsIdentity.GetCurrent().User,new SecurityIdentifier(WellKnownSidType.LocalSystemSid,null)})acl.AddAccessRule(new FileSystemAccessRule(sid,FileSystemRights.FullControl,InheritanceFlags.ContainerInherit|InheritanceFlags.ObjectInherit,PropagationFlags.None,AccessControlType.Allow));Directory.SetAccessControl(state,acl);
   trace=new HostExitTrace(Path.Combine(state,"exit.trace"),cleanup.Record);
   var inventory=ReadJson(ReadFile(Text(config["inventory"]),2*1024*1024));if(inventory["build"]?["frame_protocol"]?.Type!=JTokenType.Integer||(int)inventory["build"]["frame_protocol"]!=WorkerFrameCodec.Version||inventory["build"]?["projection_protocol"]?.Type!=JTokenType.Integer||(int)inventory["build"]["projection_protocol"]!=WorkerProjectionCodec.Version)throw new InvalidDataException("Worker-control-or-frame-version-mismatch");var hashes=new Dictionary<string,string>();foreach(var file in ((JObject)inventory["runtime_files"]).Properties())hashes.Add(file.Name,(string)file.Value);var image=new WorkerImage(Text(config["image"]),"Worker.exe",hashes);
   var journal=new JournalActor(Path.Combine(state,"journal"),RuntimeLimits.JournalPending);Wait(journal.Initialized);
   try{var station=new PrivateStation();using(trace.Track(station,HostExitPhase.StationDispose)){
    string validatorRoot=Text(config["validator"]);var validatorInventory=ReadJson(ReadFile(Path.Combine(validatorRoot,"build.json"),2*1024*1024));var validatorFiles=new Dictionary<string,string>();foreach(var file in ((JObject)validatorInventory["files"]).Properties())validatorFiles.Add(file.Name,(string)file.Value);
    var validator=new ValidatorLauncher(station,new WorkerImage(validatorRoot,"Validator.exe",validatorFiles),Text(config["attempts"]),cleanup.Record);
    var input=new SpatialWallpaper.Sdk.Capabilities.InputBroker();using(trace.Track(input,HostExitPhase.InputDispose)){
     var audio=audioConfiguration?.Open(station,cleanup.Record);using(trace.Track(audio,HostExitPhase.AudioDispose)){
    var llm=new SpatialWallpaper.Supervision.Broker.LlmBroker(SpatialWallpaper.Supervision.Broker.LlmConfiguration.Parse(config["llm"]));using(trace.Track(llm,HostExitPhase.LlmImplicitDispose)){
    var storage=new SpatialWallpaper.Supervision.Broker.StorageBroker(Path.Combine(state,"storage"));
    try{Wait(storage.Initialized);
    var clock=Stopwatch.StartNew();var gpu=new WindowsGpuSampler(()=>clock.ElapsedMilliseconds);var factory=new WindowsWorkerLauncher(station,image,Text(config["attempts"]),()=>clock.ElapsedMilliseconds,(int)config["width"],(int)config["height"],gpu,validator,storage,llm,audio,input,cleanup.Record,timingFps);
    var runtime=new SupervisorRuntime(journal,factory,new WatchdogPolicy(RuntimeLimits.StartupMs,RuntimeLimits.FrameMs,RuntimeLimits.StopMs,RuntimeLimits.ProcessBytes,RuntimeLimits.CpuWindowMs,RuntimeLimits.CpuBudgetMs,RuntimeLimits.ActualGpuBytes,RuntimeLimits.GpuStaleMs),RuntimeLimits.MaxInstances,RuntimeLimits.GlobalProcessBytes,()=>clock.ElapsedMilliseconds,cleanup);
    try{var service=new Service(runtime,journal,Path.Combine(state,"endpoint.json"),audio,validator.Validate,storage,input);using(trace.Track(service,HostExitPhase.ServiceDispose))service.Run(previewOwner);}
    finally{trace.Run(HostExitPhase.RuntimeClose,runtime.Close);trace.Run(HostExitPhase.RuntimeWait,()=>{if(!runtime.Completion.Wait(5000)){trace.Mark(HostExitPhase.Main,HostExitEvent.ForcedProcessExit,2);Environment.Exit(2);}});trace.Run(HostExitPhase.RuntimeResult,()=>runtime.Completion.GetAwaiter().GetResult());trace.Run(HostExitPhase.GpuCloseRequest,gpu.Close,HostExitEvent.CloseRequested);}
    }finally{trace.Run(HostExitPhase.StorageClose,storage.Close);trace.Run(HostExitPhase.StorageWait,()=>Wait(storage.Completion));trace.Run(HostExitPhase.LlmDispose,llm.Dispose);trace.Run(HostExitPhase.LlmWait,()=>Wait(llm.Completion));}}}}
   }}finally{trace.Run(HostExitPhase.JournalClose,journal.Close);trace.Run(HostExitPhase.JournalWait,()=>Wait(journal.Completion));}
   trace.Run(HostExitPhase.EndpointDelete,()=>File.Delete(Path.Combine(state,"endpoint.json")));trace.Mark(HostExitPhase.Main,HostExitEvent.MainReturning,0);return 0;
  }catch(Exception error){trace?.Mark(HostExitPhase.Main,HostExitEvent.Fault,error.HResult);Action report=()=>Console.Error.WriteLine("Supervisor failed: "+error.GetBaseException().Message);if(trace==null)report();else trace.Run(HostExitPhase.FailureReport,report);trace?.Mark(HostExitPhase.Main,HostExitEvent.MainReturning,1);return 1;}finally{try{previewOwner?.Dispose();}catch(Exception error){cleanup.Record(error);}trace?.Dispose();}}
 }
}
