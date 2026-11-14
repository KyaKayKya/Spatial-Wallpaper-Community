using System;using System.IO;using System.Diagnostics;using System.Threading;using System.Threading.Tasks;using System.Runtime.InteropServices;using System.ComponentModel;
using SpatialWallpaper.Supervision.Windows;using SpatialWallpaper.Sdk.Capabilities;
using System.IO.Pipes;using System.Security.AccessControl;using System.Security.Principal;
using System.IO.MemoryMappedFiles;
namespace SpatialWallpaper.Supervision.Interactive {
 internal sealed class DesktopHitDiagnosticCleanupException:IOException {
  internal DesktopHitDiagnosticCleanupException(Exception original,Exception cleanup):base("diagnostic-binding-start-cleanup-failed",new AggregateException(original,cleanup)){}
 }
 internal sealed class DesktopHitThreadSnapshot {
  internal bool Available;internal string Error,Trace;internal Guid Generation;internal long Sequence,CommitVersion,TraceVersion,CommitQpc;
 }
 internal sealed class DesktopHitThreadBinding:IDisposable {
  internal readonly uint ThreadId;internal readonly ulong ThreadCreated,ProcessCreated;internal readonly int ProcessId;internal readonly Guid Generation;
  readonly object gate=new object();readonly MemoryMappedViewAccessor view;readonly Func<int> readTraceVersion;readonly Func<string> readTrace;Process retained;IntPtr thread;bool closed;
  [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenThread(uint rights,bool inherit,uint tid);
  [DllImport("kernel32.dll",SetLastError=true)]static extern uint GetProcessIdOfThread(IntPtr handle);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetThreadTimes(IntPtr handle,out ulong creation,out ulong exit,out ulong kernel,out ulong user);
  [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForSingleObject(IntPtr handle,uint timeout);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool CloseHandle(IntPtr handle);
  internal DesktopHitThreadBinding(Process process,Guid generation,MemoryMappedViewAccessor identity,Func<int> traceVersion,Func<string> traceSnapshot){
   Generation=generation;ProcessId=process.Id;view=identity;readTraceVersion=traceVersion;readTrace=traceSnapshot;
   try{
    if(process.Handle==IntPtr.Zero||process.HasExited)throw new InvalidDataException("diagnostic-process-exited");
    retained=Process.GetProcessById(ProcessId);if(retained.Handle==IntPtr.Zero||process.HasExited||retained.StartTime!=process.StartTime)throw new InvalidDataException("diagnostic-process-identity");
    ProcessCreated=(ulong)retained.StartTime.ToUniversalTime().ToFileTimeUtc();
    int version=view.ReadInt32(0);Thread.MemoryBarrier();ThreadId=view.ReadUInt32(12);ThreadCreated=view.ReadUInt64(24);ValidateIdentity(version);
    thread=OpenThread(0x100800,false,ThreadId);if(thread==IntPtr.Zero)throw new Win32Exception(Marshal.GetLastWin32Error(),"diagnostic-thread-open");
    ulong created,exit,kernel,user;if(GetProcessIdOfThread(thread)!=(uint)ProcessId)throw new InvalidDataException("diagnostic-thread-process");
    if(!GetThreadTimes(thread,out created,out exit,out kernel,out user))throw new Win32Exception(Marshal.GetLastWin32Error(),"diagnostic-thread-times");
    if(created!=ThreadCreated)throw new InvalidDataException("diagnostic-thread-creation");RequireLive();
   }catch(Exception original){try{Dispose();}catch(Exception cleanup){throw new DesktopHitDiagnosticCleanupException(original,cleanup);}throw;}
  }
  void ValidateIdentity(int version){
   byte[] bytes=new byte[16];view.ReadArray(32,bytes,0,16);
   bool matches=view.ReadUInt32(4)==1&&view.ReadUInt32(8)==(uint)ProcessId&&view.ReadUInt32(12)==ThreadId&&view.ReadUInt64(16)==ProcessCreated&&view.ReadUInt64(24)==ThreadCreated&&new Guid(bytes)==Generation;
   Thread.MemoryBarrier();if(version<=0||(version&1)!=0||version!=view.ReadInt32(0)||!matches)throw new InvalidDataException("diagnostic-identity-changing-or-mismatch");
  }
  void RequireLive(){if(retained.HasExited)throw new InvalidDataException("diagnostic-process-exited");uint result=WaitForSingleObject(thread,0);if(result==258)return;if(result==0)throw new InvalidDataException("diagnostic-thread-exited");if(result==uint.MaxValue)throw new Win32Exception(Marshal.GetLastWin32Error(),"diagnostic-thread-wait");throw new InvalidDataException("diagnostic-thread-wait-unknown:"+result);}
  internal void ValidateTarget(Process process){lock(gate){if(closed)throw new ObjectDisposedException("diagnostic-binding");if(process.HasExited||process.Id!=ProcessId||(ulong)process.StartTime.ToUniversalTime().ToFileTimeUtc()!=ProcessCreated)throw new InvalidDataException("diagnostic-target-mismatch");ValidateIdentity(view.ReadInt32(0));RequireLive();}}
  internal DesktopHitThreadSnapshot Read(){lock(gate){var result=new DesktopHitThreadSnapshot{Generation=Generation};try{
   if(closed)throw new ObjectDisposedException("diagnostic-binding");RequireLive();ValidateIdentity(view.ReadInt32(0));
   long version=view.ReadInt64(48);Thread.MemoryBarrier();result.Sequence=view.ReadInt64(56);result.TraceVersion=view.ReadInt64(64);result.CommitQpc=view.ReadInt64(72);result.CommitVersion=version;
   int traceBefore=readTraceVersion();result.Trace=readTrace();int traceAfter=readTraceVersion();Thread.MemoryBarrier();
   if(version<=0||(version&1)!=0||version!=view.ReadInt64(48)||traceBefore!=traceAfter||traceBefore!=result.TraceVersion||(traceBefore&1)!=0||result.Sequence<0||result.CommitQpc<=0||result.Trace==null||!result.Trace.StartsWith("sequence="+result.Sequence+";",StringComparison.Ordinal))throw new InvalidDataException("diagnostic-request-changing-or-mismatch");
   ValidateIdentity(view.ReadInt32(0));RequireLive();result.Available=true;
  }catch(Exception error){result.Error=error.GetType().Name+":"+error.Message;}return result;}}
  public void Dispose(){lock(gate){if(closed)return;closed=true;try{if(thread!=IntPtr.Zero){var handle=thread;thread=IntPtr.Zero;if(!CloseHandle(handle))throw new Win32Exception(Marshal.GetLastWin32Error());}}finally{retained?.Dispose();}}}
 }
 public sealed class DesktopHitResult {
  public readonly uint Classification,ShellProcess;public readonly ulong ShellCreated;public readonly IntPtr Window;public readonly long ObservedAt;
  internal DesktopHitResult(uint classification,uint process,ulong created,IntPtr window,long at){Classification=classification;ShellProcess=process;ShellCreated=created;Window=window;ObservedAt=at;}
  public bool IsFresh(long now)=>now>=ObservedAt&&now-ObservedAt<=50;
 }
 // The helper has no author code. It runs on the interactive desktop; never
 // launch it in Host's private window station. Query has one in-flight request,
 // no queue, and a 25ms synchronous / 100ms background acquisition I/O budget.
 // Neither budget extends the result's 50ms authorization freshness.
 // OS scheduling can overshoot a timed wait;
 // this is not a hard real-time guarantee. Failure closes the Job before
 // cleanup is scheduled, so a blocked COM provider cannot retain the input hook.
 public sealed class DesktopHitClient:IDisposable {
  readonly TaskCompletionSource<bool> completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  Process child;Stream input,output;IntPtr job;int closed,busy;long sequence;MemoryMappedFile trace;MemoryMappedViewAccessor traceView;
  MemoryMappedFile threadIdentity;MemoryMappedViewAccessor threadIdentityView,threadIdentitySeed;Guid helperGeneration;Action<string> diagnosticFault;Exception diagnosticCleanupFailure;
  internal DesktopHitThreadBinding ThreadDiagnostic{get;private set;}internal string ThreadDiagnosticError{get;private set;}
  public string FailureTrace{get;private set;}public long LastQueryBeganAt{get;private set;}public long LastQueryCompletedAt{get;private set;}public long FailureObservedAt{get;private set;}
  public int ProcessId{get;private set;}public string Failure{get;private set;}public string FailurePhase{get;private set;}public long LastQuerySequence{get;private set;}public double MaximumQueryMilliseconds{get;private set;}public Task Completion=>completion.Task;
   internal bool Running{get{try{return Volatile.Read(ref closed)==0&&child!=null&&!child.HasExited;}catch(ObjectDisposedException){return false;}catch(InvalidOperationException){return false;}}}
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetNamedPipeClientProcessId(IntPtr pipe,out uint process);
  [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForSingleObject(Microsoft.Win32.SafeHandles.SafeWaitHandle handle,uint milliseconds);
  static void Check(bool value){if(!value)throw new Win32Exception(Marshal.GetLastWin32Error());}
  public DesktopHitClient(string executable):this(executable,null){}
  internal DesktopHitClient(string executable,Action<string> diagnosticFault){
   this.diagnosticFault=diagnosticFault;
   if(string.IsNullOrEmpty(executable)||!Path.IsPathRooted(executable)||!File.Exists(executable))throw new ArgumentException("desktop-hit-helper-path");
   try{
    job=Native.CreateJobObjectW(IntPtr.Zero,null);Check(job!=IntPtr.Zero);var limits=new Native.Limits{basic=new Native.BasicLimits{flags=0x2000|8|0x100,active=1},processMemory=new UIntPtr(128UL*1024*1024)};
    IntPtr data=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Native.Limits)));try{Marshal.StructureToPtr(limits,data,false);Check(Native.SetInformationJobObject(job,9,data,(uint)Marshal.SizeOf(typeof(Native.Limits))));}finally{Marshal.FreeHGlobal(data);}
    string name="SpatialWallpaper.DesktopHit."+Guid.NewGuid().ToString("N");var security=new PipeSecurity();security.SetAccessRuleProtection(true,false);security.AddAccessRule(new PipeAccessRule(WindowsIdentity.GetCurrent().User,PipeAccessRights.FullControl,AccessControlType.Allow));
    var traceSecurity=new MemoryMappedFileSecurity();traceSecurity.SetAccessRuleProtection(true,false);traceSecurity.AddAccessRule(new AccessRule<MemoryMappedFileRights>(WindowsIdentity.GetCurrent().User,MemoryMappedFileRights.FullControl,AccessControlType.Allow));
    trace=MemoryMappedFile.CreateNew(name+".trace",256,MemoryMappedFileAccess.ReadWrite,MemoryMappedFileOptions.None,traceSecurity,HandleInheritability.None);traceView=trace.CreateViewAccessor(0,256,MemoryMappedFileAccess.Read);
    // Separate fixed diagnostic record: the existing 256-byte trace is full.
    // Its generation is the unique pipe epoch, not a helper-supplied identity.
    try{
     helperGeneration=Guid.ParseExact(name.Substring(name.LastIndexOf('.')+1),"N");
     diagnosticFault?.Invoke("create");threadIdentity=MemoryMappedFile.CreateNew(name+".thread",96,MemoryMappedFileAccess.ReadWrite,MemoryMappedFileOptions.None,traceSecurity,HandleInheritability.None);
     diagnosticFault?.Invoke("seed-view");threadIdentitySeed=threadIdentity.CreateViewAccessor(0,96,MemoryMappedFileAccess.Write);
     diagnosticFault?.Invoke("seed-write");threadIdentitySeed.WriteArray(32,helperGeneration.ToByteArray(),0,16);
     try{threadIdentitySeed.Dispose();threadIdentitySeed=null;}catch(Exception error){RecordDiagnosticCleanup(error);throw;}
     diagnosticFault?.Invoke("read-view");threadIdentityView=threadIdentity.CreateViewAccessor(0,96,MemoryMappedFileAccess.Read);
     diagnosticFault?.Invoke("ready");
    }catch(Exception error){ThreadDiagnosticError="unavailable:"+error.GetType().Name+":"+error.Message;ReleaseDiagnostics();}
    var pipe=new NamedPipeServerStream(name,PipeDirection.InOut,1,PipeTransmissionMode.Byte,PipeOptions.Asynchronous,4096,4096,security);input=output=pipe;
    child=Process.Start(new ProcessStartInfo(executable,"\\\\.\\pipe\\"+name){UseShellExecute=false,CreateNoWindow=true});ProcessId=child.Id;Check(AssignProcessToJobObject(job,child.Handle));
    var connected=pipe.WaitForConnectionAsync();ObserveFault(connected);if(!connected.Wait(2000))throw new TimeoutException("desktop-hit-connect");connected.GetAwaiter().GetResult();Check(GetNamedPipeClientProcessId(pipe.SafePipeHandle.DangerousGetHandle(),out uint connectedPid));if(connectedPid!=(uint)ProcessId)throw new InvalidDataException("desktop-hit-helper-owner");
    var hello=new byte[16];Read(output,hello,Deadline(2000));
    if(BitConverter.ToUInt32(hello,0)!=0x31484453u||BitConverter.ToUInt32(hello,4)!=1||BitConverter.ToUInt32(hello,8)!=(uint)ProcessId||BitConverter.ToUInt32(hello,12)!=0)throw new InvalidDataException("desktop-hit-helper-hello");
    // Optional diagnostics never grant input or alter the protocol budgets.
    if(ThreadDiagnosticError==null&&threadIdentityView!=null)try{ThreadDiagnostic=new DesktopHitThreadBinding(child,helperGeneration,threadIdentityView,()=>traceView.ReadInt32(0),SnapshotTrace);}catch(Exception error){ThreadDiagnosticError="unavailable:"+error.GetType().Name+":"+error.Message;if(error is DesktopHitDiagnosticCleanupException)RecordDiagnosticCleanup(error);ReleaseDiagnostics();}
   }catch{Dispose();if(!Completion.Wait(3000))throw new IOException("desktop-hit-start-cleanup-timeout");throw;}
  }
  static long Deadline(int milliseconds)=>Stopwatch.GetTimestamp()+milliseconds*Stopwatch.Frequency/1000;
  void RecordDiagnosticCleanup(Exception error){diagnosticCleanupFailure=diagnosticCleanupFailure==null?error:new AggregateException(diagnosticCleanupFailure,error);}
  void ReleaseDiagnostics(){
   Action<string,Action> release=(name,action)=>{try{action();}catch(Exception error){RecordDiagnosticCleanup(error);}try{diagnosticFault?.Invoke("release-"+name);}catch(Exception error){RecordDiagnosticCleanup(error);}};
   if(ThreadDiagnostic!=null)release("binding",()=>{ThreadDiagnostic.Dispose();ThreadDiagnostic=null;});
   if(threadIdentitySeed!=null)release("seed",()=>{threadIdentitySeed.Dispose();threadIdentitySeed=null;});
   if(threadIdentityView!=null)release("view",()=>{threadIdentityView.Dispose();threadIdentityView=null;});
   if(threadIdentity!=null)release("mapping",()=>{threadIdentity.Dispose();threadIdentity=null;});
  }
  static int Remaining(long deadline){long value=(deadline-Stopwatch.GetTimestamp())*1000/Stopwatch.Frequency;if(value<=0)throw new TimeoutException("desktop-hit-deadline");return (int)value;}
  static void ObserveFault(Task task){task.ContinueWith(done=>{var observed=done.Exception;},CancellationToken.None,TaskContinuationOptions.OnlyOnFaulted|TaskContinuationOptions.ExecuteSynchronously,TaskScheduler.Default);}
  // CLR STA waits may dispatch another low-level hook inside this request.
  // This short, bounded native wait does not pump. Acquisition queries run on
  // a background thread so Explorer activation can still dispatch normally.
  static bool Wait(Task task,int milliseconds){if(task.IsCompleted)return true;uint result=WaitForSingleObject(((IAsyncResult)task).AsyncWaitHandle.SafeWaitHandle,(uint)milliseconds);if(result==0)return true;if(result==258)return false;throw new Win32Exception(Marshal.GetLastWin32Error());}
  static void Read(Stream stream,byte[] bytes,long deadline){int at=0;while(at<bytes.Length){var pending=stream.ReadAsync(bytes,at,bytes.Length-at);ObserveFault(pending);if(!Wait(pending,Remaining(deadline)))throw new TimeoutException("desktop-hit-read");int count=pending.GetAwaiter().GetResult();if(count==0)throw new EndOfStreamException();at+=count;}}
  // Best-effort telemetry only: it never grants input or extends a deadline.
  // The helper may advance while this snapshot is read; sequence/version
  // checks explicitly reject a mixed request instead of presenting it as fact.
  static string InteractiveTrace(MemoryMappedViewAccessor view){long before=view.ReadInt64(208),site=view.ReadInt64(216),stage=view.ReadInt64(224),outcome=view.ReadInt64(232),at=view.ReadInt64(240),detail=view.ReadInt64(248),after=view.ReadInt64(208);return before!=after||(before&1)!=0?"changing":site+","+stage+","+outcome+","+at+","+detail;}
  string SnapshotTrace(){try{var view=traceView;if(view==null)return "unavailable";int before=view.ReadInt32(0);long serial=view.ReadInt64(8);var values=new long[16];for(int i=0;i<values.Length;i++)values[i]=view.ReadInt64(16+i*8);long objectDone=view.ReadInt64(152),hitStart=view.ReadInt64(160);long enumBefore=view.ReadInt64(168),enumStage=view.ReadInt64(176),enumWindow=view.ReadInt64(184),enumCount=view.ReadInt64(192),enumAt=view.ReadInt64(200),enumAfter=view.ReadInt64(168);string enumeration=enumBefore!=enumAfter||(enumBefore&1)!=0?"changing":enumStage+","+enumWindow+","+enumCount+","+enumAt;string interactive=InteractiveTrace(view);int after=view.ReadInt32(0);return before!=after||(before&1)!=0?"changing":"sequence="+serial+";stages="+string.Join(",",values)+";frequency="+view.ReadInt64(144)+";object_done="+objectDone+";hit_start="+hitStart+";enumeration="+enumeration+";interactive="+interactive;}catch{return "unavailable";}}
  public DesktopHitResult Query(int x,int y)=>Query(x,y,25);
  internal DesktopHitResult QueryForAcquisition(int x,int y)=>Query(x,y,100);
  DesktopHitResult Query(int x,int y,int budget){
   // Monitor is reentrant: an STA wait may dispatch another input callback on
   // this same thread. Reject it too, before allocating a protocol sequence.
   if(Volatile.Read(ref closed)!=0||Interlocked.CompareExchange(ref busy,1,0)!=0)return null;
   LastQueryCompletedAt=0;LastQueryBeganAt=Stopwatch.GetTimestamp();var elapsed=Stopwatch.StartNew();string phase="prepare";
   try{
    if(Volatile.Read(ref closed)!=0)return null;long began=WindowsMachineClock.Milliseconds(),deadline=Deadline(budget);var request=new byte[32];long serial=checked(++sequence);
    LastQuerySequence=serial;using(var writer=new BinaryWriter(new MemoryStream(request))){writer.Write(0x31484453u);writer.Write(1u);writer.Write(serial);writer.Write(x);writer.Write(y);writer.Write(0UL);}
    phase="write";
    var written=input.WriteAsync(request,0,request.Length);ObserveFault(written);if(!Wait(written,Remaining(deadline)))throw new TimeoutException("desktop-hit-write");written.GetAwaiter().GetResult();
    phase="flush";var flushed=input.FlushAsync();ObserveFault(flushed);if(!Wait(flushed,Remaining(deadline)))throw new TimeoutException("desktop-hit-flush");flushed.GetAwaiter().GetResult();
    phase="read";var reply=new byte[48];Read(output,reply,deadline);phase="validate";long stamp=BitConverter.ToInt64(reply,16);ulong hwnd=BitConverter.ToUInt64(reply,24),created=BitConverter.ToUInt64(reply,32);uint pid=BitConverter.ToUInt32(reply,40),kind=BitConverter.ToUInt32(reply,44);
    if(BitConverter.ToUInt32(reply,0)!=0x31484453u||BitConverter.ToUInt32(reply,4)!=1||BitConverter.ToInt64(reply,8)!=serial||stamp<began||stamp>WindowsMachineClock.Milliseconds()||kind>4||hwnd>long.MaxValue||kind==0&&(hwnd!=0||created!=0||pid!=0)||kind!=0&&(hwnd==0||created==0||pid==0))throw new InvalidDataException("desktop-hit-reply");
    Remaining(deadline);if(Volatile.Read(ref closed)!=0)return null;
    // Conservatively include transport, provider and reply delays. A helper
    // finishing later must never refresh evidence it observed earlier.
    return new DesktopHitResult(kind,pid,created,new IntPtr((long)hwnd),began);
   }catch(Exception error){FailureObservedAt=Stopwatch.GetTimestamp();FailurePhase=phase;FailureTrace=SnapshotTrace();Failure=error is TimeoutException?"deadline":error is InvalidDataException?"protocol":error.GetType().Name;Dispose();return null;}
   finally{LastQueryCompletedAt=Stopwatch.GetTimestamp();MaximumQueryMilliseconds=Math.Max(MaximumQueryMilliseconds,elapsed.Elapsed.TotalMilliseconds);Volatile.Write(ref busy,0);}
  }
  public void Dispose(){
   if(Interlocked.Exchange(ref closed,1)!=0)return;
   IntPtr owned=Interlocked.Exchange(ref job,IntPtr.Zero);if(owned!=IntPtr.Zero)Native.CloseHandle(owned);
   // No stream disposal or process wait in the caller's input callback.
   Task.Run(()=>{
    Exception failure=null;Action<Action> release=action=>{try{action();}catch(Exception error){failure=failure==null?error:new AggregateException(failure,error);}};
    release(()=>{if(child!=null&&!child.WaitForExit(2000)){child.Kill();if(!child.WaitForExit(1000))throw new IOException("desktop-hit-helper-still-alive");}});
    release(()=>input?.Dispose());if(!ReferenceEquals(input,output))release(()=>output?.Dispose());release(ReleaseDiagnostics);if(diagnosticCleanupFailure!=null)release(()=>{throw new IOException("desktop-hit-diagnostic-cleanup-failed",diagnosticCleanupFailure);});release(()=>child?.Dispose());release(()=>traceView?.Dispose());release(()=>trace?.Dispose());
    if(failure==null)completion.TrySetResult(true);else completion.TrySetException(failure);
   });
  }
 }
}
