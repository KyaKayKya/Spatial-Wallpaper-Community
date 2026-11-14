using System;using System.IO;using System.Diagnostics;using System.Reflection;using System.Threading.Tasks;using SpatialWallpaper.Sdk.Capabilities;
static class DesktopHitChecks {
 class StalledWrite:Stream {
  readonly bool flush;readonly TaskCompletionSource<bool> pending=new TaskCompletionSource<bool>();public StalledWrite(bool flush){this.flush=flush;}
  public override Task WriteAsync(byte[] bytes,int offset,int count,System.Threading.CancellationToken token)=>flush?Task.CompletedTask:pending.Task;
  public override Task FlushAsync(System.Threading.CancellationToken token)=>pending.Task;
  public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
  public override void Flush()=>throw new NotSupportedException();public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();public override long Seek(long n,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();
 }
 sealed class ThrowingDispose:StalledWrite {public ThrowingDispose():base(false){}protected override void Dispose(bool disposing){throw new IOException("injected-stream-dispose");}}
 sealed class ReentrantWrite:Stream {
  readonly Stream inner;readonly Action callback;bool entered;public ReentrantWrite(Stream stream,Action action){inner=stream;callback=action;}
  public override Task WriteAsync(byte[] bytes,int offset,int count,System.Threading.CancellationToken token){if(!entered){entered=true;callback();}return inner.WriteAsync(bytes,offset,count,token);}
  public override Task FlushAsync(System.Threading.CancellationToken token)=>inner.FlushAsync(token);
  public override bool CanRead=>false;public override bool CanSeek=>false;public override bool CanWrite=>true;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
  public override void Flush()=>inner.Flush();public override void Write(byte[] b,int o,int n)=>inner.Write(b,o,n);public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();public override long Seek(long n,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();
 }
 sealed class AfterReply:Stream {
  readonly Stream inner;readonly Action callback;public AfterReply(Stream inner,Action callback){this.inner=inner;this.callback=callback;}
  public override async Task<int> ReadAsync(byte[] bytes,int offset,int count,System.Threading.CancellationToken token){int n=await inner.ReadAsync(bytes,offset,count,token).ConfigureAwait(false);callback();return n;}
  public override bool CanRead=>true;public override bool CanSeek=>false;public override bool CanWrite=>false;public override long Length=>throw new NotSupportedException();public override long Position{get=>throw new NotSupportedException();set=>throw new NotSupportedException();}
  public override void Flush()=>throw new NotSupportedException();public override void Write(byte[] b,int o,int n)=>throw new NotSupportedException();public override int Read(byte[] b,int o,int n)=>throw new NotSupportedException();public override long Seek(long n,SeekOrigin origin)=>throw new NotSupportedException();public override void SetLength(long n)=>throw new NotSupportedException();
 }
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);Console.WriteLine("PASS "+reason);}
 static int Main(string[] args){try{
  var type=typeof(InputBroker).Assembly.GetType("SpatialWallpaper.Supervision.Interactive.DesktopHitClient");Need(type!=null,"bounded-desktop-hit-client-available");
  foreach(string mode in new[]{"Good","Hang","Partial","Stale","Invalid"}){
   dynamic client=Activator.CreateInstance(type,new object[]{Path.Combine(args[0],mode+".exe")});
   using(var child=Process.GetProcessById((int)client.ProcessId)){
    IntPtr retained=child.Handle;try{
     var clock=Stopwatch.StartNew();object result=client.Query(100,100);long elapsed=clock.ElapsedMilliseconds;Console.WriteLine(mode+" query ms="+elapsed+" failure="+(string)client.Failure);
     Need(elapsed<150,"query-deadline-does-not-wait-for-helper-"+mode);
     if(mode=="Good"){Need(result!=null,"valid-unavailable-reply-accepted");for(int i=0;i<10;i++){Need((object)client.Query(100,100)!=null,"repeat-good");Console.WriteLine((string)client.Failure);}var field=type.GetField("input",BindingFlags.NonPublic|BindingFlags.Instance);var original=(Stream)field.GetValue(client);object nested=new object();field.SetValue(client,new ReentrantWrite(original,()=>nested=client.Query(100,100)));object outer=client.Query(100,100);field.SetValue(client,original);Need(nested==null&&outer!=null,"reentrant-query-rejected-without-corrupting-outer-protocol");}else{Need(result==null,"bad-or-stalled-helper-fails-closed-"+mode);Need((long)client.LastQuerySequence==1,"failed-query-sequence-recorded-"+mode);string trace=(string)client.FailureTrace;Need(trace!=null&&trace.StartsWith("sequence=1;stages=")&&long.Parse(trace.Substring(trace.IndexOf("stages=")+7).Split(',')[0])>0,"helper-trace-survives-timeout-or-protocol-rejection-"+mode);Need((string)client.FailurePhase==(mode=="Hang"||mode=="Partial"?"read":"validate"),"failed-query-phase-recorded-"+mode);Need((object)client.Query(100,100)==null,"failed-helper-does-not-revive-"+mode);}
    }finally{((IDisposable)client).Dispose();Need(((Task)client.Completion).Wait(3000),"helper-cleanup-completed-"+mode);}
    Need(child.WaitForExit(1000),"owned-helper-kernel-exited-"+mode);
    if(mode!="Good")Need(((string)client.FailureTrace).StartsWith("sequence=1;stages="),"failure-trace-retained-after-cleanup-and-kernel-exit-"+mode);
   }
  }
  // Exercise a useful 25..50ms reply and an expired reply, not just liveness.
  foreach(int delay in new[]{30,65}){
   dynamic client=Activator.CreateInstance(type,new object[]{Path.Combine(args[0],"Good.exe")});
   var field=type.GetField("input",BindingFlags.NonPublic|BindingFlags.Instance);var original=(Stream)field.GetValue(client);long beforeWrite=0;
   using(var child=Process.GetProcessById((int)client.ProcessId)){
    IntPtr retained=child.Handle;
    try{
     field.SetValue(client,new ReentrantWrite(original,()=>{beforeWrite=WindowsMachineClock.Milliseconds();System.Threading.Thread.Sleep(delay);}));
     var acquisition=type.GetMethod("QueryForAcquisition",BindingFlags.NonPublic|BindingFlags.Instance);
     Need(acquisition!=null,"separate-bounded-background-acquisition-query");
     var elapsed=Stopwatch.StartNew();dynamic result=acquisition.Invoke(client,new object[]{100,100});
     Need(result!=null,"background-query-can-complete-outside-hook-budget");
     long now=WindowsMachineClock.Milliseconds();Console.WriteLine("background delay="+delay+" elapsed="+elapsed.Elapsed.TotalMilliseconds+" age="+(now-(long)result.ObservedAt));
     Need((long)result.ObservedAt<=beforeWrite,"observation-age-includes-pre-observation-delay");
     Need((bool)result.IsFresh(now)==(delay==30),delay==30?"background-reply-outside-hook-budget-still-fresh":"late-reply-cannot-refresh-old-observation");
    }finally{field.SetValue(client,original);((IDisposable)client).Dispose();Need(((Task)client.Completion).Wait(3000),"background-age-test-cleanup");}
    Need(child.WaitForExit(1000),"background-age-test-helper-kernel-exited");
   }
  }
  foreach(bool disposeDuringReply in new[]{false,true}){
   dynamic client=Activator.CreateInstance(type,new object[]{Path.Combine(args[0],"Good.exe")});
   var field=type.GetField("output",BindingFlags.NonPublic|BindingFlags.Instance);var original=(Stream)field.GetValue(client);
   using(var child=Process.GetProcessById((int)client.ProcessId)){
    IntPtr retained=child.Handle;
    try{
     field.SetValue(client,new AfterReply(original,()=>{if(disposeDuringReply)((IDisposable)client).Dispose();else System.Threading.Thread.Sleep(65);}));
     dynamic result=type.GetMethod("QueryForAcquisition",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(client,new object[]{100,100});
     if(disposeDuringReply)Need(result==null,"dispose-during-reply-never-returns-evidence");
     else {Need(result!=null,"delayed-reply-completes-within-background-budget");Need(!(bool)result.IsFresh(WindowsMachineClock.Milliseconds()),"post-observation-delay-remains-stale");}
    }finally{((IDisposable)client).Dispose();Need(((Task)client.Completion).Wait(3000),"delayed-reply-cleanup");original.Dispose();}
    Need(child.WaitForExit(1000),"delayed-reply-helper-kernel-exited");
   }
  }
  {
   dynamic client=Activator.CreateInstance(type,new object[]{Path.Combine(args[0],"Hang.exe")});
   using(var child=Process.GetProcessById((int)client.ProcessId)){
    IntPtr retained=child.Handle;var elapsed=Stopwatch.StartNew();
    try{object result=type.GetMethod("QueryForAcquisition",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(client,new object[]{100,100});Need(result==null&&(string)client.Failure=="deadline"&&elapsed.ElapsedMilliseconds<500,"background-hung-query-remains-bounded-and-fails-closed");}
    finally{((IDisposable)client).Dispose();Need(((Task)client.Completion).Wait(3000),"background-timeout-cleanup");}
    Need(child.WaitForExit(1000),"background-timeout-helper-kernel-exited");
   }
  }
  {
   var resultType=type.Assembly.GetType("SpatialWallpaper.Supervision.Interactive.DesktopHitResult");
   dynamic result=Activator.CreateInstance(resultType,BindingFlags.NonPublic|BindingFlags.Instance,null,new object[]{1u,1u,1UL,new IntPtr(1),1000L},null);
   Need(!(bool)result.IsFresh(999L)&&(bool)result.IsFresh(1000L)&&(bool)result.IsFresh(1050L)&&!(bool)result.IsFresh(1051L),"freshness-rejects-future-and-expired-evidence");
  }
  {
   dynamic client=Activator.CreateInstance(type,new object[]{Path.Combine(args[0],"Good.exe")});var flags=BindingFlags.NonPublic|BindingFlags.Instance;
   try{
    var mapping=(System.IO.MemoryMappedFiles.MemoryMappedFile)type.GetField("trace",flags).GetValue(client);
    using(var view=mapping.CreateViewAccessor(0,256,System.IO.MemoryMappedFiles.MemoryMappedFileAccess.ReadWrite)){
    var snapshot=type.GetMethod("SnapshotTrace",flags);
    view.Write(168,2L);view.Write(176,3L);view.Write(184,123L);view.Write(192,7L);view.Write(200,456L);
    Need(((string)snapshot.Invoke(client,null)).Contains(";enumeration=3,123,7,456;"),"enumeration-diagnostic-retains-stage-window-count-time");
    view.Write(168,3L);
    Need(((string)snapshot.Invoke(client,null)).Contains(";enumeration=changing;"),"partial-enumeration-diagnostic-not-reported-as-consistent");
    view.Write(168,4L);view.Write(208,2L);view.Write(216,2L);view.Write(224,1L);view.Write(232,0L);view.Write(240,789L);view.Write(248,0L);
    Need(((string)snapshot.Invoke(client,null)).EndsWith(";interactive=2,1,0,789,0"),"interactive-pending-call-location-stage-status-time-retained");
    view.Write(208,3L);
    Need(((string)snapshot.Invoke(client,null)).EndsWith(";interactive=changing"),"partial-interactive-diagnostic-not-reported-as-consistent");
    view.Write(208,4L);view.Write(224,8L);view.Write(232,2L);view.Write(248,6L);
    Need(((string)snapshot.Invoke(client,null)).EndsWith(";interactive=2,8,2,789,6"),"interactive-false-return-distinct-from-pending-call");
    }
   }finally{((IDisposable)client).Dispose();Need(((Task)client.Completion).Wait(3000),"enumeration-diagnostic-cleanup");}
  }
  foreach(bool flush in new[]{false,true}){
   dynamic client=Activator.CreateInstance(type,new object[]{Path.Combine(args[0],"Good.exe")});
   using(var child=Process.GetProcessById((int)client.ProcessId)){
    IntPtr retained=child.Handle;var field=type.GetField("input",BindingFlags.NonPublic|BindingFlags.Instance);var original=(Stream)field.GetValue(client);
    try{field.SetValue(client,new StalledWrite(flush));Need((object)client.Query(100,100)==null,"stalled-write-or-flush-rejected");Need((string)client.Failure=="deadline"&&(string)client.FailurePhase==(flush?"flush":"write")&&(long)client.LastQuerySequence==1,"stalled-write-or-flush-attributed");}
    finally{((IDisposable)client).Dispose();Need(((Task)client.Completion).Wait(3000),"stalled-write-or-flush-cleanup");original.Dispose();}
    Need(child.WaitForExit(1000),"stalled-write-or-flush-helper-kernel-exited");
   }
  }
  {
   dynamic client=Activator.CreateInstance(type,new object[]{Path.Combine(args[0],"Good.exe")});var fields=BindingFlags.NonPublic|BindingFlags.Instance;
   var mapping=(System.IO.MemoryMappedFiles.MemoryMappedFile)type.GetField("trace",fields).GetValue(client);var view=(System.IO.MemoryMappedFiles.MemoryMappedViewAccessor)type.GetField("traceView",fields).GetValue(client);var mapHandle=mapping.SafeMemoryMappedFileHandle;var viewHandle=view.SafeMemoryMappedViewHandle;
   using(var child=Process.GetProcessById((int)client.ProcessId)){
    IntPtr retained=child.Handle;var field=type.GetField("input",fields);var original=(Stream)field.GetValue(client);field.SetValue(client,new ThrowingDispose());
    try{((IDisposable)client).Dispose();bool rejected=false;try{((Task)client.Completion).GetAwaiter().GetResult();}catch(IOException error){rejected=error.Message=="injected-stream-dispose";}Need(rejected,"cleanup-preserves-stream-dispose-failure");Need(child.WaitForExit(1000),"cleanup-fault-helper-kernel-exited");Need(mapHandle.IsClosed&&viewHandle.IsClosed,"cleanup-fault-still-closes-trace-mapping-and-view");}
    finally{original.Dispose();view.Dispose();mapping.Dispose();}
   }
  }return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
