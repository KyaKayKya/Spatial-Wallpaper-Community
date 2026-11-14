using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Sdk.Hosting {
 public interface ISupervisorSessionTransport {Guid Host{get;}Task<JObject> Call(string operation,JObject payload);}
 // A connection is pinned to the descriptor supplied by the trusted launcher.
 // It never follows a replacement endpoint while it still owns a session.
 public sealed class SupervisorManagerConnection:ISupervisorSessionTransport {
  readonly string pipe,auth;public Guid Host{get;}public int? HostProcessId{get;}public long? HostCreationFileTime{get;}int frameReading;
  public SupervisorManagerConnection(string descriptor){
   if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("Supervisor manager transport is verified only on Windows");
   var file=new FileInfo(descriptor);if(file.Length<1||file.Length>4096||(file.Attributes&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("manager-descriptor");
   JObject value;using(var stream=OpenDescriptor(descriptor))using(var reader=new StreamReader(stream,new UTF8Encoding(false,true)))value=Parse(reader.ReadToEnd());
   if(value["version"]?.Type!=JTokenType.Integer||(int)value["version"]!=1||value["pipe"]?.Type!=JTokenType.String||value["auth"]?.Type!=JTokenType.String||value["session_host"]?.Type!=JTokenType.String)throw new InvalidDataException("manager-descriptor-fields");
   if(value.ContainsKey("host_process")){var process=value["host_process"] as JObject;if(process==null||process.Count!=2||process["pid"]?.Type!=JTokenType.Integer||process["creation_filetime"]?.Type!=JTokenType.Integer||(int)process["pid"]<=0||(long)process["creation_filetime"]<=0)throw new InvalidDataException("manager-descriptor-process");HostProcessId=(int)process["pid"];HostCreationFileTime=(long)process["creation_filetime"];}
   pipe=(string)value["pipe"];auth=(string)value["auth"];Guid host;
   if(!pipe.StartsWith("SpatialWallpaper.Supervisor.",StringComparison.Ordinal)||pipe.Length>128||pipe.IndexOfAny(new[]{'\\','/','\0'})>=0||Convert.FromBase64String(auth).Length!=32||!Guid.TryParseExact((string)value["session_host"],"N",out host)||host==Guid.Empty)throw new InvalidDataException("manager-descriptor-identity");Host=host;
  }
  // Readers may retain the old complete descriptor while the Host publishes a new one.
  internal static FileStream OpenDescriptor(string path)=>new FileStream(path,FileMode.Open,FileAccess.Read,FileShare.ReadWrite|FileShare.Delete);
  static JObject Parse(string json){using(var text=new StringReader(json))using(var reader=new JsonTextReader(text){MaxDepth=12,DateParseHandling=DateParseHandling.None}){var result=JObject.Load(reader,new JsonLoadSettings{DuplicatePropertyNameHandling=DuplicatePropertyNameHandling.Error});if(reader.Read())throw new InvalidDataException("manager-json-tail");return result;}}
  public Task<JObject> Call(string operation,JObject payload)=>Task.Run(()=>{
   var clock=Stopwatch.StartNew();using(var stream=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous)){
    stream.Connect(RuntimeLimits.ManagerIoMs);
    var bytes=Encoding.UTF8.GetBytes(new JObject{{"auth",auth},{"operation",operation},{"payload",payload}}.ToString(Formatting.None));if(bytes.Length>RuntimeLimits.ManagerRequestBytes)throw new InvalidDataException("manager-request-size");
    Write(stream,BitConverter.GetBytes(bytes.Length),clock);Write(stream,bytes,clock);var header=new byte[4];Read(stream,header,clock);int length=BitConverter.ToInt32(header,0);if(length<1||length>RuntimeLimits.ManagerReplyBytes)throw new InvalidDataException("manager-reply-size");bytes=new byte[length];Read(stream,bytes,clock);return Parse(new UTF8Encoding(false,true).GetString(bytes));
   }
  });
  static void NativeOwner(PinnedProcessOwner renderer){if(renderer==null)throw new ArgumentNullException(nameof(renderer));if(renderer.HasExited)throw new InvalidOperationException("native-renderer-ended");}
  JObject NativePayload(WorkerIdentity identity,PinnedProcessOwner renderer=null){if(identity==null)throw new ArgumentNullException(nameof(identity));var value=new JObject{{"host",Host.ToString("N")},{"attempt",identity.Attempt.ToString("N")},{"instance",identity.Instance.ToString("N")},{"generation",identity.Generation}};if(renderer!=null){value["renderer_pid"]=renderer.Id;value["renderer_creation_filetime"]=renderer.CreationFileTime;}return value;}
  void NativeReply(JObject reply,WorkerIdentity identity,params string[] extra){
   var fields=new[]{"ok","host","attempt","instance","generation"}.Concat(extra).OrderBy(n=>n,StringComparer.Ordinal);
   if(reply==null||!reply.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(fields)||reply["ok"]?.Type!=JTokenType.Boolean||!(bool)reply["ok"]||reply["host"]?.Type!=JTokenType.String||(string)reply["host"]!=Host.ToString("N")||reply["attempt"]?.Type!=JTokenType.String||(string)reply["attempt"]!=identity.Attempt.ToString("N")||reply["instance"]?.Type!=JTokenType.String||(string)reply["instance"]!=identity.Instance.ToString("N")||reply["generation"]?.Type!=JTokenType.Integer||(long)reply["generation"]!=identity.Generation)throw new InvalidDataException("native-input-reply-identity");
  }
  public async Task<string> OpenNativeInputAuthority(WorkerIdentity identity,PinnedProcessOwner renderer){
   NativeOwner(renderer);var reply=await Call("OpenNativeInputAuthority",NativePayload(identity,renderer)).ConfigureAwait(false);NativeOwner(renderer);NativeReply(reply,identity,"section");if(reply["section"]?.Type!=JTokenType.String)throw new InvalidDataException("native-authority-reply");return (string)reply["section"];
  }
  // Trusted renderer lifecycle only. Registration uses a retained kernel owner;
  // authors never receive this connection or the input-source pipe.
  public Task RegisterNativeInput(WorkerIdentity identity,bool preview,PinnedProcessOwner renderer)=>RegisterNativeInputCore("RegisterNativeInput",identity,preview,renderer);
  public Task RegisterNativeOrderedInput(WorkerIdentity identity,bool preview,PinnedProcessOwner renderer)=>RegisterNativeInputCore("RegisterNativeOrderedInput",identity,preview,renderer);
  async Task RegisterNativeInputCore(string operation,WorkerIdentity identity,bool preview,PinnedProcessOwner renderer){
   NativeOwner(renderer);var payload=NativePayload(identity);payload["renderer_pid"]=renderer.Id;payload["renderer_creation_filetime"]=renderer.CreationFileTime;payload["preview"]=preview;
   var reply=await Call(operation,payload).ConfigureAwait(false);NativeReply(reply,identity);NativeOwner(renderer);
  }
  public async Task<byte[]> ReadNativeInput(WorkerIdentity identity,PinnedProcessOwner renderer){
   NativeOwner(renderer);var reply=await Call("ReadNativeInput",NativePayload(identity,renderer)).ConfigureAwait(false);NativeReply(reply,identity,"input");
   if(reply["input"]?.Type!=JTokenType.String||((string)reply["input"]).Length!=128)throw new InvalidDataException("native-input-snapshot-size");
   var bytes=Convert.FromBase64String((string)reply["input"]);SpatialWallpaper.Sdk.Capabilities.InputSnapshotCodec.RequireShape(bytes);NativeOwner(renderer);return bytes;
  }
  public async Task UnregisterNativeInput(WorkerIdentity identity){
   var reply=await Call("UnregisterNativeInput",NativePayload(identity)).ConfigureAwait(false);NativeReply(reply,identity,"removed");if(reply["removed"]?.Type!=JTokenType.Boolean)throw new InvalidDataException("native-input-close-reply");
  }
  public async Task<SpatialWallpaper.Sdk.Capabilities.OrderedInputBatch> ReadNativeOrderedInput(WorkerIdentity identity,PinnedProcessOwner renderer,long acknowledged){
   NativeOwner(renderer);var payload=NativePayload(identity,renderer);payload["cursor"]=acknowledged;
   var reply=await Call("ReadNativeOrderedInput",payload).ConfigureAwait(false);NativeReply(reply,identity,"input","cursor","events");
   if(reply["input"]?.Type!=JTokenType.String||((string)reply["input"]).Length!=128||reply["cursor"]?.Type!=JTokenType.Integer||!(reply["events"] is JArray rows)||rows.Count>SpatialWallpaper.Sdk.Capabilities.OrderedInputBatch.MaxEvents)throw new InvalidDataException("native-ordered-input-reply");
   var events=new SpatialWallpaper.Sdk.Capabilities.OrderedInputEvent[rows.Count];
   for(int i=0;i<rows.Count;i++){
    var row=rows[i] as JArray;if(row==null||row.Count!=7||row.Any(v=>v.Type!=JTokenType.Integer))throw new InvalidDataException("native-ordered-input-event");
    events[i]=new SpatialWallpaper.Sdk.Capabilities.OrderedInputEvent((long)row[0],(long)row[1],(ulong)row[2],(uint)row[3],(uint)row[4],(uint)row[5],(int)row[6]);
   }
   var batch=new SpatialWallpaper.Sdk.Capabilities.OrderedInputBatch(Convert.FromBase64String((string)reply["input"]),events,(long)reply["cursor"]);NativeOwner(renderer);return batch;
  }
  public Task<Stream> OpenNativeDesktopInputStream(WorkerIdentity identity,PinnedProcessOwner renderer,System.Threading.CancellationToken cancellation)=>OpenNativeInputStream(identity,renderer,false,cancellation);
  public Task<Stream> OpenNativePreviewInputStream(WorkerIdentity identity,PinnedProcessOwner renderer,System.Threading.CancellationToken cancellation)=>OpenNativeInputStream(identity,renderer,true,cancellation);
  Task<Stream> OpenNativeInputStream(WorkerIdentity identity,PinnedProcessOwner renderer,bool preview,System.Threading.CancellationToken cancellation)=>Task.Run<Stream>(()=>{
   cancellation.ThrowIfCancellationRequested();NativeOwner(renderer);var payload=NativePayload(identity,renderer);var stream=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous);
   try{var clock=Stopwatch.StartNew();stream.Connect(RuntimeLimits.ManagerIoMs);cancellation.ThrowIfCancellationRequested();
    var request=Encoding.UTF8.GetBytes(new JObject{{"auth",auth},{"operation",preview?"OpenNativePreviewInput":"OpenNativeDesktopInput"},{"payload",payload}}.ToString(Formatting.None));
    if(request.Length>RuntimeLimits.ManagerRequestBytes)throw new InvalidDataException("native-input-request-size");Write(stream,BitConverter.GetBytes(request.Length),clock);Write(stream,request,clock);
    cancellation.ThrowIfCancellationRequested();NativeOwner(renderer);return stream;
   }catch{stream.Dispose();throw;}
  });
  // Installation storage deletion is a trusted manager operation, never a Worker capability.
  public async Task UninstallInstallation(Guid installation){
   if(installation==Guid.Empty)throw new ArgumentException("uninstall-installation");
   var reply=await Call("UninstallInstallation",new JObject{{"host",Host.ToString("N")},{"installation",installation.ToString("N")}}).ConfigureAwait(false);
   if((bool?)reply["ok"]!=true)throw new InvalidOperationException("uninstall-refused:"+(string)reply["error"]);
   if(!reply.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(new[]{"error","host","installation","ok","storage_deleted"})||reply["host"].Type!=JTokenType.String||(string)reply["host"]!=Host.ToString("N")||reply["installation"].Type!=JTokenType.String||(string)reply["installation"]!=installation.ToString("N")||reply["storage_deleted"].Type!=JTokenType.Boolean||!(bool)reply["storage_deleted"]||reply["error"].Type!=JTokenType.Null)throw new InvalidDataException("uninstall-reply-identity");
  }
  // Admission runs in the restricted Validator on the Host's bounded launch
  // lane. Polling completion never owns the Validator or parses author content.
  public async Task<RuntimePackageInspection> InspectPackage(string path){
   if(string.IsNullOrEmpty(path)||path.Length>2048)throw new ArgumentException("inspection-package-path");var ticket=Guid.NewGuid();var clock=Stopwatch.StartNew();bool begin=true;
   while(true){var payload=new JObject{{"host",Host.ToString("N")},{"ticket",ticket.ToString("N")}};if(begin)payload["package"]=path;var reply=await Call(begin?"BeginPackageInspection":"PollPackageInspection",payload).ConfigureAwait(false);begin=false;
    if(!reply.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(new[]{"archive_sha256","build","capabilities","error","host","ok","state","ticket"})||reply["ok"].Type!=JTokenType.Boolean||!(bool)reply["ok"]||reply["host"].Type!=JTokenType.String||(string)reply["host"]!=Host.ToString("N")||reply["ticket"].Type!=JTokenType.String||(string)reply["ticket"]!=ticket.ToString("N")||reply["state"].Type!=JTokenType.String)throw new InvalidDataException("inspection-reply-identity");
    var tags=reply["capabilities"] as JArray;if(tags==null||tags.Count>32||tags.Any(t=>t.Type!=JTokenType.String||!CapabilityNames.Contains((string)t)))throw new InvalidDataException("inspection-reply-capabilities");var names=tags.Select(t=>(string)t).ToArray();if(!names.SequenceEqual(names.Distinct(StringComparer.Ordinal).OrderBy(n=>n,StringComparer.Ordinal)))throw new InvalidDataException("inspection-reply-capabilities");
    string state=(string)reply["state"];if(state=="Succeeded"){
     Func<JToken,bool> digest=t=>t.Type==JTokenType.String&&((string)t).Length==64&&((string)t).All(c=>c>='0'&&c<='9'||c>='a'&&c<='f');if(!digest(reply["build"])||!digest(reply["archive_sha256"])||reply["error"].Type!=JTokenType.Null)throw new InvalidDataException("inspection-reply-result");return new RuntimePackageInspection((string)reply["build"],(string)reply["archive_sha256"],names);
    }
    if(reply["build"].Type!=JTokenType.Null||reply["archive_sha256"].Type!=JTokenType.Null||names.Length!=0)throw new InvalidDataException("inspection-reply-state");
    if(state=="Failed"){if(reply["error"].Type!=JTokenType.String||((string)reply["error"]).Length<1||((string)reply["error"]).Length>256)throw new InvalidDataException("inspection-reply-error");throw new InvalidOperationException("restricted-package-inspection:"+(string)reply["error"]);}
    if(state!="Pending"||reply["error"].Type!=JTokenType.Null)throw new InvalidDataException("inspection-reply-state");if(clock.ElapsedMilliseconds>=15000)throw new TimeoutException("package-inspection-poll-deadline");await Task.Delay(50).ConfigureAwait(false);
   }
  }
  public async Task<long> ReadSessionFrame(RuntimeSession session,int width,int height,long after,byte[] destination){
   RuntimeLimits.RequireRenderSize(width,height);int size=WorkerFrameCodec.PayloadBytes(width,height);if(destination==null||destination.Length!=size)throw new ArgumentException("presenter-buffer-size");
   var frame=await ReadVerifiedFrame(session,width,height,after).ConfigureAwait(false);var identity=session.Identity;
   if(!session.TryUseRunning(identity,()=>{frame?.CopyPixels(destination);}))throw new InvalidOperationException("presenter-session-ended");return frame?.Sequence??0;
  }
  public async Task<RuntimeFrame> ReadVerifiedFrame(RuntimeSession session,int width,int height,long after){
   if(session==null)throw new ArgumentNullException(nameof(session));var snapshot=session.Snapshot();
   if(snapshot.State!=RuntimeSessionState.Running||snapshot.Identity==null)throw new InvalidOperationException("presenter-session-ended");
   if(session.BuildId==null)throw new InvalidOperationException("presenter-session-build-unbound");RuntimeLimits.RequireRenderSize(width,height);
   var staged=new byte[WorkerFrameCodec.PayloadBytes(width,height)];var transfer=await ReceiveFrame(snapshot.Identity,width,height,after,staged,false).ConfigureAwait(false);RuntimeFrame frame=null;
   if(!session.TryUseRunning(snapshot.Identity,()=>{if(transfer.Sequence>0)frame=new RuntimeFrame(snapshot.Identity,session.BuildId,width,height,transfer.Sequence,transfer.ProjectionRevision,staged);}))throw new InvalidOperationException("presenter-session-ended");return frame;
  }
  // Acknowledges enqueue only. A frame with the requested revision must still
  // be received and presented before the configuration is considered visible.
  public Task<string> SetSoundMix(RuntimeSession session,float music,float effects)=>SoundRequest(session,new SpatialWallpaper.Sdk.Capabilities.SoundMix(music,effects));
  public Task<string> ReadSoundState(RuntimeSession session)=>SoundRequest(session,null);
  async Task<string> SoundRequest(RuntimeSession session,SpatialWallpaper.Sdk.Capabilities.SoundMix mix){
   var snapshot=session.Snapshot();if(snapshot.State!=RuntimeSessionState.Running||snapshot.Identity==null)throw new InvalidOperationException("sound-session-ended");var id=snapshot.Identity;
   var payload=new JObject{{"host",Host.ToString("N")},{"attempt",id.Attempt.ToString("N")},{"instance",id.Instance.ToString("N")},{"generation",id.Generation}};if(mix!=null){payload["music"]=mix.Music;payload["effects"]=mix.Effects;}
   var reply=await Call(mix==null?"SoundState":"SetSoundMix",payload).ConfigureAwait(false);
   if(!reply.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(new[]{"attempt","generation","host","instance","ok","sound_error"})||(bool?)reply["ok"]!=true||(string)reply["host"]!=Host.ToString("N")||(string)reply["attempt"]!=id.Attempt.ToString("N")||(string)reply["instance"]!=id.Instance.ToString("N")||(long?)reply["generation"]!=id.Generation||reply["sound_error"].Type!=JTokenType.Null&&reply["sound_error"].Type!=JTokenType.String)throw new InvalidDataException("sound-reply-identity");
   if(!session.TryUseRunning(id,()=>{}))throw new InvalidOperationException("sound-session-ended");return (string)reply["sound_error"];
  }
  public async Task SetProjection(RuntimeSession session,ProjectionConfiguration value){
   if(session==null||value==null)throw new ArgumentNullException();var snapshot=session.Snapshot();if(snapshot.State!=RuntimeSessionState.Running||snapshot.Identity==null)throw new InvalidOperationException("presenter-session-ended");RuntimeLimits.RequireRenderSize(value.Width,value.Height);
   var identity=snapshot.Identity;var reply=await Call("SetProjection",new JObject{{"host",Host.ToString("N")},{"attempt",identity.Attempt.ToString("N")},{"instance",identity.Instance.ToString("N")},{"generation",identity.Generation},{"revision",value.Revision},{"width",value.Width},{"height",value.Height},{"display_width",value.DisplayWidth},{"display_height",value.DisplayHeight},{"eye",new JArray(value.EyeX,value.EyeY,value.EyeZ)},{"preserve_width",value.PreserveWidth}}).ConfigureAwait(false);
   if(!reply.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(new[]{"attempt","generation","host","instance","ok","revision"})||reply["ok"].Type!=JTokenType.Boolean||!(bool)reply["ok"]||reply["host"].Type!=JTokenType.String||(string)reply["host"]!=Host.ToString("N")||reply["attempt"].Type!=JTokenType.String||(string)reply["attempt"]!=identity.Attempt.ToString("N")||reply["instance"].Type!=JTokenType.String||(string)reply["instance"]!=identity.Instance.ToString("N")||reply["generation"].Type!=JTokenType.Integer||(long)reply["generation"]!=identity.Generation||reply["revision"].Type!=JTokenType.Integer||(long)reply["revision"]!=value.Revision)throw new InvalidDataException("projection-reply-identity");
   if(!session.TryUseRunning(identity,()=>{}))throw new InvalidOperationException("presenter-session-ended");
  }
  // Trusted UI owns the returned stream. The existing preview input protocol
  // consumes its epoch handshake; this method does not expose it to a Worker.
  public Task<Stream> OpenPreviewInputStream(RuntimeSession session)=>OpenInputStream(session,true,System.Threading.CancellationToken.None);
  // The desktop owner supplies its recovery cancellation. A late successful
  // pipe remains owned here until the running generation and token are checked.
  public Task<Stream> OpenDesktopInputStream(RuntimeSession session,System.Threading.CancellationToken cancellation)=>OpenInputStream(session,false,cancellation);
  Task<Stream> OpenInputStream(RuntimeSession session,bool preview,System.Threading.CancellationToken cancellation)=>Task.Run<Stream>(()=>{
   cancellation.ThrowIfCancellationRequested();string kind=preview?"preview":"desktop";if(session==null)throw new ArgumentNullException(nameof(session));var snapshot=session.Snapshot();if(snapshot.State!=RuntimeSessionState.Running||snapshot.Identity==null)throw new InvalidOperationException(kind+"-input-session-ended");
   var stream=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous);
   try{var clock=Stopwatch.StartNew();stream.Connect(RuntimeLimits.ManagerIoMs);cancellation.ThrowIfCancellationRequested();var identity=snapshot.Identity;
    var payload=new JObject{{"attempt",identity.Attempt.ToString("N")},{"instance",identity.Instance.ToString("N")},{"generation",identity.Generation}};
    var request=Encoding.UTF8.GetBytes(new JObject{{"auth",auth},{"operation",preview?"OpenPreviewInput":"OpenDesktopInput"},{"payload",payload}}.ToString(Formatting.None));
    if(request.Length>RuntimeLimits.ManagerRequestBytes)throw new InvalidDataException(kind+"-input-request-size");Write(stream,BitConverter.GetBytes(request.Length),clock);Write(stream,request,clock);
    cancellation.ThrowIfCancellationRequested();if(!session.TryUseRunning(identity,()=>{}))throw new InvalidOperationException(kind+"-input-session-ended");return stream;
   }catch{stream.Dispose();throw;}
  });
  // One bounded binary transfer per connection object. The caller's visible
  // buffer is committed only after a complete, identity-checked frame arrives.
  // A successful copy is not a presentation acknowledgement.
  sealed class Transfer {internal readonly long Sequence,ProjectionRevision;internal Transfer(long sequence,long revision){Sequence=sequence;ProjectionRevision=revision;}}
  public async Task<long> ReadFrame(WorkerIdentity identity,int width,int height,long after,byte[] destination){return (await ReceiveFrame(identity,width,height,after,destination).ConfigureAwait(false)).Sequence;}
  Task<Transfer> ReceiveFrame(WorkerIdentity identity,int width,int height,long after,byte[] destination,bool stageCommit=true)=>Task.Run(()=>{
   RuntimeLimits.RequireRenderSize(width,height);int size=WorkerFrameCodec.PayloadBytes(width,height);
   if(identity==null||after<0||destination==null||destination.Length!=size)throw new ArgumentException("presenter-frame-request");
   if(System.Threading.Interlocked.CompareExchange(ref frameReading,1,0)!=0)throw new InvalidOperationException("presenter-frame-read-pending");
   try{var clock=Stopwatch.StartNew();using(var stream=new NamedPipeClientStream(".",pipe,PipeDirection.InOut,PipeOptions.Asynchronous)){
    stream.Connect(RuntimeLimits.ManagerIoMs);
    var payload=new JObject{{"host",Host.ToString("N")},{"attempt",identity.Attempt.ToString("N")},{"instance",identity.Instance.ToString("N")},{"generation",identity.Generation},{"width",width},{"height",height},{"after",after}};
    var request=Encoding.UTF8.GetBytes(new JObject{{"auth",auth},{"operation","ReadFrame"},{"payload",payload}}.ToString(Formatting.None));
    Write(stream,BitConverter.GetBytes(request.Length),clock);Write(stream,request,clock);var length=new byte[4];Read(stream,length,clock);int count=BitConverter.ToInt32(length,0);
    if(count<1||count>RuntimeLimits.ManagerReplyBytes)throw new InvalidDataException("presenter-frame-reply-size");var bytes=new byte[count];Read(stream,bytes,clock);var reply=Parse(new UTF8Encoding(false,true).GetString(bytes));
    if(!reply.Properties().Select(p=>p.Name).OrderBy(n=>n,StringComparer.Ordinal).SequenceEqual(new[]{"available","host","ok"})||reply["ok"].Type!=JTokenType.Boolean||!(bool)reply["ok"]||reply["host"].Type!=JTokenType.String||(string)reply["host"]!=Host.ToString("N")||reply["available"].Type!=JTokenType.Boolean)throw new InvalidDataException("presenter-frame-reply-identity");
    if(!(bool)reply["available"])return new Transfer(0,0);
    var header=new byte[WorkerFrameCodec.HeaderBytes];Read(stream,header,clock);long revision;long sequence=WorkerFrameCodec.Validate(header,identity,width,height,after,out revision);
    var staged=stageCommit?new byte[size]:destination;Read(stream,staged,clock);if(stageCommit)Buffer.BlockCopy(staged,0,destination,0,size);return new Transfer(sequence,revision);
   }}finally{System.Threading.Volatile.Write(ref frameReading,0);}
  });
  static void Wait(Task task,Stopwatch clock){int remaining=RuntimeLimits.ManagerIoMs-(int)clock.ElapsedMilliseconds;if(remaining<=0||!task.Wait(remaining)){
   // Closing the using-owned pipe aborts pending IO. Observe its late fault
   // without making the timed-out caller wait for that callback to be scheduled.
   task.ContinueWith(t=>{var observed=t.Exception;},TaskContinuationOptions.OnlyOnFaulted);throw new TimeoutException("manager-io-deadline");
  }task.GetAwaiter().GetResult();}
  static void Write(Stream stream,byte[] bytes,Stopwatch clock){Wait(stream.WriteAsync(bytes,0,bytes.Length),clock);}
  static void Read(Stream stream,byte[] bytes,Stopwatch clock){for(int offset=0;offset<bytes.Length;){var task=stream.ReadAsync(bytes,offset,bytes.Length-offset);Wait(task,clock);int count=task.GetAwaiter().GetResult();if(count==0)throw new EndOfStreamException();offset+=count;}}
 }
}
