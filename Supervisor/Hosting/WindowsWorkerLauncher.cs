using System;using System.IO;using System.Threading;using System.Diagnostics;using System.Collections.Generic;using System.Security.Cryptography;using System.Security.AccessControl;using System.Security.Principal;using SpatialWallpaper.Sdk.Hosting;using SpatialWallpaper.Sdk.Capabilities;using SpatialWallpaper.Supervision.Broker;
namespace SpatialWallpaper.Supervision.Windows {
 // Trusted distribution inventory, not a manifest supplied by wallpaper content.
 public sealed class WorkerImage {
  internal readonly string Root,Executable;internal readonly Dictionary<string,string> Files;
  public WorkerImage(string root,string executable,IDictionary<string,string> files){
   if(files==null||files.Count==0||files.Count>8192)throw new ArgumentException("worker-image-files");Root=Path.GetFullPath(root);Executable=executable;Files=new Dictionary<string,string>(StringComparer.Ordinal);
   foreach(var pair in files){string name=pair.Key;if(string.IsNullOrEmpty(name)||name.Length>240||name.Contains("\\")||name.Contains(":")||name.StartsWith("/",StringComparison.Ordinal))throw new ArgumentException("worker-image-path");foreach(string part in name.Split(new[]{'/'}))if(part.Length==0||part=="."||part=="..")throw new ArgumentException("worker-image-path");if(pair.Value==null||pair.Value.Length!=64)throw new ArgumentException("worker-image-hash");Files.Add(name,pair.Value);}
   if(!Files.ContainsKey(executable))throw new ArgumentException("worker-image-executable");
  }
  static void NoLink(string path){if((File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("worker-source-link");}
  internal void Copy(string destination){
   NoLink(Root);
   foreach(string name in Files.Keys)if(destination.Length+1+name.Length>RuntimeLimits.CachePathMax)throw new InvalidDataException("worker-cache-path-too-long");
   foreach(var pair in Files){
    string source=Root;foreach(string segment in pair.Key.Split(new[]{'/'})){source=Path.Combine(source,segment);NoLink(source);}
    string target=Path.Combine(destination,pair.Key.Replace('/',Path.DirectorySeparatorChar));Directory.CreateDirectory(Path.GetDirectoryName(target));
    using(var input=new FileStream(source,FileMode.Open,FileAccess.Read,FileShare.Read))using(var hash=SHA256.Create()){
     string digest=BitConverter.ToString(hash.ComputeHash(input)).Replace("-","").ToLowerInvariant();if(digest!=pair.Value)throw new InvalidDataException("worker-image-drift");input.Position=0;
     using(var output=new FileStream(target,FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
    }
   }
  }
 }
 public sealed class WindowsWorkerLauncher:IWorkerLauncher {
  readonly PrivateStation station;readonly WorkerImage image;readonly string attempts;readonly Func<long> now;readonly int width,height;readonly WindowsGpuSampler gpu;readonly ValidatorLauncher validator;readonly StorageBroker storage;readonly LlmBroker llm;
  readonly AudioBroker audio;readonly InputBroker input;readonly int frameTimingFps;readonly Action<Exception> cleanupFailure;
  public WindowsWorkerLauncher(PrivateStation desktop,WorkerImage distribution,string attemptDirectory,Func<long> monotonicMilliseconds,int pixelWidth,int pixelHeight,WindowsGpuSampler gpuAccounting=null,ValidatorLauncher admission=null,StorageBroker persistence=null,LlmBroker language=null,AudioBroker audioFeatures=null,InputBroker inputFeatures=null,Action<Exception> cleanupFailure=null,int frameTimingFps=0){if(frameTimingFps!=0&&frameTimingFps!=30&&frameTimingFps!=60)throw new ArgumentException("frame-timing-fps");this.frameTimingFps=frameTimingFps;
   this.cleanupFailure=cleanupFailure;audio=audioFeatures;input=inputFeatures;
   if(desktop==null||distribution==null||monotonicMilliseconds==null)throw new ArgumentNullException();RuntimeLimits.RequireRenderSize(pixelWidth,pixelHeight);WorkerFrameCodec.PayloadBytes(pixelWidth,pixelHeight);
   station=desktop;image=distribution;attempts=Path.GetFullPath(attemptDirectory);Directory.CreateDirectory(attempts);if((File.GetAttributes(attempts)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("attempt-directory-link");now=monotonicMilliseconds;width=pixelWidth;height=pixelHeight;gpu=gpuAccounting;validator=admission;storage=persistence;llm=language;
  }
  public IManagedWorker Start(LaunchRequest request,AttemptReceipt receipt,Action<int> created){
   if(validator==null||storage==null||storage.Initialized.Status!=System.Threading.Tasks.TaskStatus.RanToCompletion||storage.Completion.IsCompleted)throw new InvalidOperationException("worker-admission-services-unavailable");
   var workspace=new OwnedAttempt(attempts,receipt.Attempt,cleanupFailure);WindowsManagedWorker result=null;
   try{
   result=new WindowsManagedWorker(now,width,height,workspace,gpu);
   string root=workspace.Root;image.Copy(root);
   if((File.GetAttributes(request.RuntimePath)&FileAttributes.ReparsePoint)!=0)throw new InvalidDataException("package-snapshot-link");
   using(var input=new FileStream(request.RuntimePath,FileMode.Open,FileAccess.Read,FileShare.Read)){
    if(input.Length<1||input.Length>64L*1024*1024)throw new InvalidDataException("package-snapshot-size");using(var output=new FileStream(Path.Combine(root,"scene.spatial"),FileMode.CreateNew,FileAccess.Write,FileShare.None))input.CopyTo(output);
   }
   // Keep the exact copied archive immutable through admission and renderer creation.
   using(var snapshot=new FileStream(Path.Combine(root,"scene.spatial"),FileMode.Open,FileAccess.Read,FileShare.Read)){
   var validation=validator.Validate(Path.Combine(root,"scene.spatial"));if(validation.BuildId!=receipt.BuildId)throw new AdmissionRejectedException("launch-build-mismatch");
   result.Session=new BrokerSession(receipt,request.Preview,validation.Capabilities);
   string boundary=WindowsSandboxCanaries.Create(attempts,workspace);
   var identity=new WorkerIdentity(receipt.Attempt,receipt.Instance,receipt.Generation);
   if(input!=null&&result.Session.Allows("input"))result.Input=input.Register(identity,!request.Preview);
   var args=new[]{"-batchmode","-force-d3d11","-screen-width",width.ToString(),"-screen-height",height.ToString(),"-screen-fullscreen","0","-logFile","-","--sdk-package",Path.Combine(root,"scene.spatial"),"--sdk-build",receipt.BuildId,"--sdk-attempt",identity.Attempt.ToString("N"),"--sdk-instance",identity.Instance.ToString("N"),"--sdk-generation",identity.Generation.ToString(),"--sdk-width",width.ToString(),"--sdk-height",height.ToString(),"--sdk-lpac-canary",Path.Combine(boundary,"aap"),"--sdk-lpac-control",Path.Combine(boundary,"arap"),"--sdk-frame-handle","0"};
   var capabilityArgs=new[]{"--sdk-capability-request","0","--sdk-capability-reply","0","--sdk-stop-handle","0","--sdk-projection-handle","0"};int frameIndex=args.Length-1;var allArgs=new List<string>(args);allArgs.AddRange(capabilityArgs);if(frameTimingFps!=0){allArgs.Add("--sdk-timing-fps");allArgs.Add(frameTimingFps.ToString(System.Globalization.CultureInfo.InvariantCulture));}args=allArgs.ToArray();long logs=0;
    result.Child=RestrictedProcess.Start(station,Path.Combine(root,image.Executable),args,root,request.ReservedProcessBytes,sid=>{
     result.Frames=new PixelFramePipe(sid,identity,width,height);result.Logs=new WorkerWritePipe(sid);result.StopChannel=new WorkerStopChannel(sid,identity,now);result.Projection=new WorkerProjectionChannel(sid,identity);result.Capabilities=new CapabilityServer(sid,result.Session,storage,now,llm,audio,result.Input);args[frameIndex]=result.Frames.ChildWriter.ToInt64().ToString();args[frameIndex+2]=result.Capabilities.ChildRequestWriter.ToInt64().ToString();args[frameIndex+4]=result.Capabilities.ChildReplyReader.ToInt64().ToString();args[frameIndex+6]=result.StopChannel.ChildReader.ToInt64().ToString();args[frameIndex+8]=result.Projection.ChildReader.ToInt64().ToString();logs=result.Logs.ChildWriter.ToInt64();return new[]{result.Frames.ChildWriter,result.Logs.ChildWriter,result.Capabilities.ChildRequestWriter,result.Capabilities.ChildReplyReader,result.StopChannel.ChildReader,result.Projection.ChildReader};
    },pid=>{created(pid);result.Frames.CloseChildCopy();result.Logs.CloseChildCopy();result.Capabilities.CloseChildCopies();result.StopChannel.CloseChildCopy();result.Projection.CloseChildCopy();},receipt.Attempt,()=>new IntPtr(logs),cleanupFailure);
    result.Capabilities.Start();result.StartReaders();return result;
   }
   }catch{try{if(result==null)workspace.Dispose();else result.Dispose();}catch(Exception error){cleanupFailure?.Invoke(error);throw;}throw;}
  }
 }
 public interface IWorkerFrameSource {
  int FrameWidth{get;}int FrameHeight{get;}
  bool CopyFrame(byte[] destination,out long frameSequence,out long projectionRevision);
 }
 public sealed class WindowsManagedWorker:IManagedWorker,IWorkerDiagnostics,IWorkerProcessIdentity,IWorkerFrameSource {
  public int FrameWidth{get;}public int FrameHeight{get;}
  public long CreationFileTime=>Child.CreationFileTime;
  // Trusted presenter registration; never exposed through the Worker protocol.
  public InputTarget Input{get;internal set;}
  internal WorkerStopChannel StopChannel;internal WorkerProjectionChannel Projection;internal RestrictedProcess Child;internal PixelFramePipe Frames;internal WorkerWritePipe Logs;internal CapabilityServer Capabilities;internal BrokerSession Session;
  public void UpdateProjection(ProjectionConfiguration value){if(value.Width!=FrameWidth||value.Height!=FrameHeight)throw new ArgumentException("projection-frame-size");Projection.Update(value);}
  public void ConfigureSound(float music,float effects){Capabilities.Sound?.Configure(music,effects);}
  public string SoundError=>Capabilities.Sound?.Error;
  readonly Func<long> now;readonly object gate=new object();readonly byte[] pixels,log=new byte[RuntimeLimits.LogBytes];readonly int frameBytes;readonly OwnedAttempt workspace;readonly WindowsGpuSampler gpu;bool gpuRegistered;
  Thread reader,logger;Process process;long sequence,projectionRevision,receivedAt,receivedFrames;uint? exitCode;int protocolFault,logBytes;bool disposed;
  internal WindowsManagedWorker(Func<long> clock,int width,int height,OwnedAttempt attempt,WindowsGpuSampler gpuAccounting){now=clock;workspace=attempt;gpu=gpuAccounting;FrameWidth=width;FrameHeight=height;frameBytes=WorkerFrameCodec.PayloadBytes(width,height);pixels=new byte[frameBytes];}
  public int ProcessId=>Child.ProcessId;
  public int LogBytes{get{lock(gate)return logBytes;}}
  public long Sequence{get{lock(gate)return sequence;}}
  public long ReceivedFrames{get{lock(gate)return receivedFrames;}}
  public bool CopyFrame(byte[] destination,out long frameSequence){long revision;return CopyFrame(destination,out frameSequence,out revision);}
  public bool CopyFrame(byte[] destination,out long frameSequence,out long configurationRevision){if(destination==null||destination.Length!=frameBytes)throw new ArgumentException("presenter-buffer-size");lock(gate){frameSequence=sequence;configurationRevision=projectionRevision;if(sequence==0)return false;Buffer.BlockCopy(pixels,0,destination,0,frameBytes);return true;}}
  // Raw bounded diagnostics, decoded only on a trusted request; no per-chunk string allocation.
  public byte[] ReadLog(){lock(gate){var copy=new byte[logBytes];Buffer.BlockCopy(log,0,copy,0,copy.Length);return copy;}}
  internal void StartReaders(){
   process=Process.GetProcessById(ProcessId);if(gpu!=null){gpu.Register(ProcessId);gpuRegistered=true;}
   reader=new Thread(()=>{try{long window=now(),count=0;while(Frames.Receive()){
    long at=now();if(at-window>=1000){window=at;count=0;}if(++count>RuntimeLimits.MaxReceivedFps)throw new InvalidDataException("frame-rate-limit");
    lock(gate){Buffer.BlockCopy(Frames.Pixels,0,pixels,0,frameBytes);sequence=Frames.Sequence;projectionRevision=Frames.ProjectionRevision;receivedAt=at;receivedFrames++;}
   }}catch{Interlocked.Exchange(ref protocolFault,1);}}){IsBackground=true,Name="SpatialWallpaper pixels"};
   logger=new Thread(()=>{try{var bytes=new byte[4096];long window=now(),total=0;int count;while((count=Logs.Read(bytes,0,bytes.Length))>0){
    long at=now();if(at-window>=1000){window=at;total=0;}total+=count;if(total>RuntimeLimits.LogPerSecond)throw new InvalidDataException("worker-log-rate-limit");
    lock(gate){int keep=Math.Min(count,log.Length);int retained=Math.Min(logBytes,log.Length-keep);if(retained>0)Buffer.BlockCopy(log,logBytes-retained,log,0,retained);Buffer.BlockCopy(bytes,count-keep,log,retained,keep);logBytes=retained+keep;}
   }}catch{Interlocked.Exchange(ref protocolFault,1);}}){IsBackground=true,Name="SpatialWallpaper diagnostics"};reader.Start();logger.Start();
  }
  public WorkerSample Sample(){
   bool alive=Child.Wait(0)==258;long memory=0,cpu=0;
   if(alive){try{process.Refresh();memory=process.PrivateMemorySize64;cpu=(long)process.TotalProcessorTime.TotalMilliseconds;}catch(InvalidOperationException){alive=Child.Wait(0)==258;if(alive)throw;}}
   if(!alive)exitCode=Child.ExitCode;
   long gpuBytes=-1,gpuAt=0;gpu?.Read(ProcessId,out gpuBytes,out gpuAt);
   lock(gate)return new WorkerSample(alive,memory,cpu,sequence,receivedAt,Volatile.Read(ref protocolFault)!=0||Capabilities.ProtocolFault||StopChannel.Failed||Projection.Failed,gpuBytes,gpuAt,exitCode,StopChannel.PublishedAt);
  }
  public string ReadDiagnostic(){lock(gate){string text=System.Text.Encoding.UTF8.GetString(log,0,logBytes);int exception=text.IndexOf("Exception:",StringComparison.Ordinal);int start=exception<0?Math.Max(0,text.Length-512):text.LastIndexOf('\n',exception)+1;return new Newtonsoft.Json.Linq.JObject{{"source","OS-exit-code-and-untrusted-Worker-output"},{"observed_exit_code",exitCode.HasValue?(long)exitCode.Value:-1L},{"worker_reported_reason",WorkerExitCodes.ReportedReason(exitCode)},{"log_tail",text.Substring(start,Math.Min(512,text.Length-start))}}.ToString(Newtonsoft.Json.Formatting.None);}}
  public void RequestStop(){Projection?.Close();Capabilities?.Close();Session?.Close();Input?.Dispose();StopChannel?.Request();}
  public void Kill(){Projection?.Close();Session?.Close();Input?.Dispose();Capabilities?.Close();Child?.Kill(124);}
  public void Dispose(){
   if(disposed)return;
   Projection?.Close();Session?.Close();Input?.Dispose();Capabilities?.Close();if(Child!=null&&Child.Wait(0)==258)Child.Kill(125);if(Child!=null&&Child.Wait(5000)!=0)throw new InvalidOperationException("worker-kernel-still-alive");
   if(Child!=null)exitCode=Child.ExitCode;
   if(reader!=null&&!reader.Join(1000)||logger!=null&&!logger.Join(1000))throw new InvalidOperationException("worker-reader-still-alive");
   if(gpuRegistered){gpu.Unregister(ProcessId);gpuRegistered=false;}Projection?.Dispose();Capabilities?.Dispose();StopChannel?.Dispose();Child?.Dispose();Frames?.Dispose();Logs?.Dispose();process?.Dispose();workspace.Dispose();disposed=true;
  }
 }
}
