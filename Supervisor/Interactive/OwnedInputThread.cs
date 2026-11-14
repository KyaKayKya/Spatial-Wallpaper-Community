using System;using System.Collections.Generic;using System.ComponentModel;using System.Diagnostics;using System.Runtime.InteropServices;using System.Threading;using System.Threading.Tasks;
namespace SpatialWallpaper.Supervision.Interactive {
 // Owns native hooks and their message queue independently of the receiver UI.
 // Commands and lifecycle delegates execute only on this thread. Stop is a
 // request: native release obligations may require continued message pumping.
 public sealed class OwnedInputThread:IDisposable {
  public sealed class Diagnostic {
   public int Schema=1;public string[] Columns=new[]{"ordinal","qpc","stage"};
   public bool Available;public string Error,Generation;public long Frequency,ProcessCreated,ThreadCreated,Total,Dropped;public uint ProcessId,ThreadId;
   public Diagnostic Failure;public string Reason;public long CapturedAtQpc;
   public long[][] Rows,FirstRows;
   public string Scope="owner loop stage boundaries; elapsed gaps do not identify scheduler or native wait cause";
  }
  const int TraceCapacity=512,TracePrefix=128;readonly long[] trace=new long[TraceCapacity*3],firstTrace=new long[TracePrefix*3];
  readonly string traceGeneration=Guid.NewGuid().ToString("N");long traceVersion,traceTotal;uint tracePid,traceTid;long traceProcessCreated,traceThreadCreated;string traceError;
  readonly long[] failureTrace=new long[TraceCapacity*3],failurePrefix=new long[TracePrefix*3];int failureReady;long failureTotal,failureAt;string failureReason;
  [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
  [DllImport("kernel32.dll")]static extern uint GetCurrentProcessId();
  [DllImport("kernel32.dll")]static extern IntPtr GetCurrentThread();
  [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetThreadTimes(IntPtr thread,out long creation,out long exit,out long kernel,out long user);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr process,out long creation,out long exit,out long kernel,out long user);
  void InitializeTrace(){try{tracePid=GetCurrentProcessId();traceTid=GetCurrentThreadId();long exit,kernel,user;if(!GetProcessTimes(GetCurrentProcess(),out traceProcessCreated,out exit,out kernel,out user)||!GetThreadTimes(GetCurrentThread(),out traceThreadCreated,out exit,out kernel,out user))throw new Win32Exception(Marshal.GetLastWin32Error());}catch(Exception error){traceError=error.GetType().Name;}}
  // Single owner writer, fixed scalar storage; no callbacks, locks, I/O or per-event allocations.
  void Mark(int stage){Interlocked.Increment(ref traceVersion);long ordinal=traceTotal+1,qpc=Stopwatch.GetTimestamp();int at=(int)(traceTotal%TraceCapacity)*3;trace[at]=ordinal;trace[at+1]=qpc;trace[at+2]=stage;if(traceTotal<TracePrefix){int first=(int)traceTotal*3;firstTrace[first]=ordinal;firstTrace[first+1]=qpc;firstTrace[first+2]=stage;}traceTotal=ordinal;Interlocked.Increment(ref traceVersion);}
  // Called only at explicit failure sites on the owner, never on routine Revoke.
  // One immutable preallocated capture survives later pumping and later failures.
  public void FreezeDiagnostic(string reason){if(Volatile.Read(ref failureReady)!=0||GetCurrentThreadId()!=traceTid)return;Mark(14);Array.Copy(trace,failureTrace,trace.Length);Array.Copy(firstTrace,failurePrefix,firstTrace.Length);failureTotal=traceTotal;failureAt=trace[(int)((traceTotal-1)%TraceCapacity)*3+1];failureReason=reason!=null&&reason.Length<=96?reason:"unspecified-failure";Volatile.Write(ref failureReady,1);}
  Diagnostic ReadFailure(){if(Volatile.Read(ref failureReady)==0)return null;long total=failureTotal;int count=(int)Math.Min(total,TraceCapacity),prefix=(int)Math.Min(total,TracePrefix);var rows=new long[count][];var first=new long[prefix][];for(int i=0;i<count;i++){int at=(int)((total-count+i)%TraceCapacity)*3;rows[i]=new[]{failureTrace[at],failureTrace[at+1],failureTrace[at+2]};}for(int i=0;i<prefix;i++){int at=i*3;first[i]=new[]{failurePrefix[at],failurePrefix[at+1],failurePrefix[at+2]};}return new Diagnostic{Available=traceError==null&&traceProcessCreated>0&&traceThreadCreated>0,Error=traceError,Generation=traceGeneration,Frequency=Stopwatch.Frequency,ProcessId=tracePid,ThreadId=traceTid,ProcessCreated=traceProcessCreated,ThreadCreated=traceThreadCreated,Total=total,Dropped=Math.Max(0,total-TraceCapacity),Rows=rows,FirstRows=first,Reason=failureReason,CapturedAtQpc=failureAt};}
  public Diagnostic ReadDiagnostic(){
   long version=Volatile.Read(ref traceVersion);var result=new Diagnostic{Generation=traceGeneration,Frequency=Stopwatch.Frequency,ProcessId=tracePid,ThreadId=traceTid,ProcessCreated=traceProcessCreated,ThreadCreated=traceThreadCreated,Error=traceError,Failure=ReadFailure()};
   if(version==0||(version&1)!=0){result.Error="owner-trace-unpublished-or-writing";return result;}
   long total=Volatile.Read(ref traceTotal);int count=(int)Math.Min(total,TraceCapacity),prefix=(int)Math.Min(total,TracePrefix);var rows=new long[count][];var first=new long[prefix][];
   for(int i=0;i<count;i++){int at=(int)((total-count+i)%TraceCapacity)*3;rows[i]=new[]{trace[at],trace[at+1],trace[at+2]};}
   for(int i=0;i<prefix;i++){int at=i*3;first[i]=new[]{firstTrace[at],firstTrace[at+1],firstTrace[at+2]};}
   Thread.MemoryBarrier();if(version!=Volatile.Read(ref traceVersion)){result.Error="owner-trace-changing";return result;}
   result.Total=total;result.Dropped=Math.Max(0,total-TraceCapacity);result.Rows=rows;result.FirstRows=first;result.Available=result.Error==null&&result.ProcessCreated>0&&result.ThreadCreated>0;return result;
  }
  readonly object gate=new object();readonly Queue<Action> commands=new Queue<Action>();readonly AutoResetEvent wake=new AutoResetEvent(false);
  readonly Action start,tick;readonly Func<bool> release;readonly Thread thread;
  readonly TaskCompletionSource<bool> started=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  bool stopping;Exception failure;
  [StructLayout(LayoutKind.Sequential)]struct Message{public IntPtr Window;public uint Id;public UIntPtr W;public IntPtr L;public uint Time;public int X,Y;public uint Private;}
  [DllImport("user32.dll")]static extern bool PeekMessage(out Message message,IntPtr window,uint first,uint last,uint remove);
  [DllImport("user32.dll")]static extern bool TranslateMessage(ref Message message);
  [DllImport("user32.dll")]static extern IntPtr DispatchMessage(ref Message message);
  [DllImport("user32.dll",SetLastError=true)]static extern uint MsgWaitForMultipleObjectsEx(uint count,IntPtr[] handles,uint timeout,uint mask,uint flags);
  public Task Started=>started.Task;public Task Completion=>completion.Task;
  public string Failure{get{lock(gate)return failure==null?null:failure.GetType().Name+":"+failure.Message;}}
  public OwnedInputThread(Action start,Action tick,Func<bool> release){
   this.start=start??throw new ArgumentNullException(nameof(start));this.tick=tick??throw new ArgumentNullException(nameof(tick));this.release=release??throw new ArgumentNullException(nameof(release));
   thread=new Thread(Run){IsBackground=true,Name="Spatial Wallpaper native input owner"};thread.SetApartmentState(ApartmentState.STA);
   try{thread.Start();}catch{wake.Dispose();throw;}
   // Completion means the owner has actually left its message loop/thread.
   Task.Run(()=>{thread.Join();Exception error;lock(gate)error=failure;if(error==null)completion.TrySetResult(true);else completion.TrySetException(error);});
  }
  public void Post(Action action){if(action==null)throw new ArgumentNullException(nameof(action));lock(gate){if(stopping)throw new ObjectDisposedException(nameof(OwnedInputThread));if(commands.Count>=64)throw new InvalidOperationException("input-owner-command-capacity");commands.Enqueue(action);wake.Set();}}
  public void Dispose(){lock(gate){if(stopping)return;stopping=true;commands.Clear();wake.Set();}}
  void Fail(Exception error){lock(gate){if(failure==null)failure=error;stopping=true;commands.Clear();}started.TrySetException(error);}
  void Run(){
   try{
    InitializeTrace();Mark(11);
    // Create the queue before publishing Started; no UI-thread wait is needed.
    PeekMessage(out var ignored,IntPtr.Zero,0,0,0);
    try{start();Mark(12);started.TrySetResult(true);}catch(Exception error){Fail(error);}
    var handles=new[]{wake.SafeWaitHandle.DangerousGetHandle()};var clock=Stopwatch.StartNew();long lastTick=-16,nextRelease=0;
    while(true){
     try{
      Mark(1);for(int i=0;i<64&&PeekMessage(out var message,IntPtr.Zero,0,0,1);i++){if(message.Id==0x12){Fail(new InvalidOperationException("input-owner-unexpected-quit"));break;}TranslateMessage(ref message);DispatchMessage(ref message);}Mark(2);
      Mark(3);
      for(int i=0;i<64;i++){Action command;lock(gate){if(stopping||commands.Count==0)break;command=commands.Dequeue();}command();}
      Mark(4);
      bool stop;lock(gate)stop=stopping;
      if(stop){if(clock.ElapsedMilliseconds>=nextRelease){nextRelease=clock.ElapsedMilliseconds+16;Mark(9);bool released=release();Mark(10);if(released)break;}}
      else if(clock.ElapsedMilliseconds-lastTick>=16){lastTick=clock.ElapsedMilliseconds;Mark(5);tick();Mark(6);}
      Mark(7);uint result=MsgWaitForMultipleObjectsEx(1,handles,16,0x4ff,4);Mark(8);if(result==0xffffffff)throw new Win32Exception(Marshal.GetLastWin32Error());
     }catch(Exception error){Fail(error);}
    }
   }catch(Exception error){Fail(error);}
   finally{lock(gate){stopping=true;commands.Clear();wake.Dispose();}if(!started.Task.IsCompleted)started.TrySetException(new InvalidOperationException("input-owner-never-started"));Mark(13);}
  }
 }
}
