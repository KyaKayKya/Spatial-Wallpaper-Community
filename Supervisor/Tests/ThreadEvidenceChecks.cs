using System;using System.Diagnostics;using System.IO;using System.Runtime.InteropServices;using System.Threading;using Newtonsoft.Json.Linq;
static class ThreadEvidenceChecks {
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 static void Need(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static bool Suspended(JObject row){return (string)row["status"]=="observed"&&(string)row["state"]=="Wait"&&(string)row["wait_reason"]=="Suspended";}
 static bool Flat(JObject a,JObject b){return (string)a["status"]=="observed"&&(string)b["status"]=="observed"&&a["cycles"]!=null&&b["cycles"]!=null&&(ulong)a["cycles"]==(ulong)b["cycles"];}
 static int Main(string[] args){if(args.Length>0&&args[0]=="--busy-child"){Console.WriteLine(GetCurrentThreadId());while(true)Thread.SpinWait(1000);}try{
  var owned=Process.GetCurrentProcess();int pid=owned.Id;uint tid=GetCurrentThreadId();
  bool inject=false;using(var failed=new OwnedThreadEvidence(owned,tid,null,h=>{if(inject){SetLastError(5);return uint.MaxValue;}return 258;})){
   inject=true;var failure=failed.Capture();File.WriteAllText(Path.Combine(args[0],"wait-failed.json"),failure.ToString());
   Need((string)failure["status"]=="unavailable"&&((string)failure["error"]).Contains("thread-wait-failed")&&!((string)failure["error"]).Contains("exited"),"wait-api-failure-not-exit");
   Need((int)failure["win32_error"]==5,"wait-api-error-preserved");
  }
  inject=false;using(var unknown=new OwnedThreadEvidence(owned,tid,null,h=>inject?123U:258U)){
   inject=true;var failure=unknown.Capture();Need((string)failure["status"]=="unavailable"&&((string)failure["error"]).Contains("thread-wait-unknown:123"),"unknown-wait-not-exit");
  }
  var stale=new JObject{{"status","unavailable"},{"state","Wait"},{"wait_reason","Suspended"},{"cycles",0UL}};
  Need(!Suspended(stale)&&!Flat(stale,stale),"unavailable-stale-fields-rejected");
  using(var probe=new OwnedThreadEvidence(owned,tid)){
   var row=probe.Capture();File.WriteAllText(Path.Combine(args[0],"initial.json"),row.ToString());
   Need((string)row["status"]=="observed","explicit-thread-identity-and-cpu-observed");
   Need((int)row["pid"]==pid&&(uint)row["tid"]==tid,"identity-matches-target");
   Need(row["creation_filetime"]!=null&&row["cycles"]!=null&&row["kernel_100ns"]!=null&&row["user_100ns"]!=null,"raw-counters-present");
   bool rejected=false;try{using(var wrong=new OwnedThreadEvidence(owned,tid,(ulong)row["creation_filetime"]+1)) {}}catch(InvalidOperationException e){rejected=e.Message=="thread-creation-identity-mismatch";}Need(rejected,"wrong-creation-rejected");
   for(int i=0;i<70;i++)probe.Capture();var bounded=probe.Snapshot();Need(((JArray)bounded["samples"]).Count==64&&(long)bounded["total"]==71,"bounded-ring");
   probe.Dispose();Need((string)probe.Capture()["status"]=="unavailable","disposed-probe-unavailable");
  }
  Controls(owned,args[0]);owned.Dispose();return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
 static void Controls(Process owned,string output){
  using(var mutex=new Mutex())using(var ready=new ManualResetEvent(false)){
   uint workerId=0;mutex.WaitOne();var thread=new Thread(()=>{workerId=GetCurrentThreadId();ready.Set();mutex.WaitOne();mutex.ReleaseMutex();}){IsBackground=true};thread.Start();Need(ready.WaitOne(2000),"mutex-target-ready");
   try{using(var probe=new OwnedThreadEvidence(owned,workerId)){
    bool wait=false;for(int i=0;i<20;i++){var row=probe.Capture();wait|=(string)row["status"]=="observed"&&(string)row["state"]=="Wait";Thread.Sleep(5);}
    Need(wait,"mutex-wait-observed");Need((string)probe.Snapshot()["wct"]["status"]=="unavailable","mutex-owner-chain-not-claimed");File.WriteAllText(Path.Combine(output,"mutex.json"),probe.Snapshot().ToString());
   }}finally{mutex.ReleaseMutex();Need(thread.Join(2000),"mutex-thread-cleanup");}
  }
  using(var ready=new ManualResetEvent(false))using(var release=new ManualResetEvent(false)){
   uint workerId=0;var thread=new Thread(()=>{workerId=GetCurrentThreadId();ready.Set();release.WaitOne();}){IsBackground=true};thread.Start();Need(ready.WaitOne(2000),"event-target-ready");
   try{using(var probe=new OwnedThreadEvidence(owned,workerId)){
    bool wait=false;for(int i=0;i<20;i++){var row=probe.Capture();wait|=(string)row["status"]=="observed"&&(string)row["state"]=="Wait";Need((string)row["classification"]=="indeterminate","wait-does-not-imply-scheduling-cause");Thread.Sleep(5);}
    Need(wait,"event-wait-observed");File.WriteAllText(Path.Combine(output,"event.json"),probe.Snapshot().ToString());
    release.Set();Need(thread.Join(2000),"event-thread-exited");Need((string)probe.Capture()["status"]=="unavailable","exited-target-unavailable");
   }}finally{release.Set();Need(thread.Join(2000),"event-thread-cleanup");}
  }
  using(var ready=new ManualResetEvent(false)){
   uint workerId=0;int stop=0;var thread=new Thread(()=>{workerId=GetCurrentThreadId();ready.Set();while(Volatile.Read(ref stop)==0)Thread.SpinWait(1000);}){IsBackground=true};thread.Start();Need(ready.WaitOne(2000),"busy-target-ready");
   try{using(var probe=new OwnedThreadEvidence(owned,workerId)){
    var first=probe.Capture();Thread.Sleep(100);var last=probe.Capture();Need((string)first["status"]=="observed"&&(string)last["status"]=="observed"&&(ulong)last["cycles"]>(ulong)first["cycles"],"busy-cycles-increase");
    Need((ulong)last["kernel_100ns"]+(ulong)last["user_100ns"]>=(ulong)first["kernel_100ns"]+(ulong)first["user_100ns"],"cpu-counter-monotonic");
    File.WriteAllText(Path.Combine(output,"busy.json"),probe.Snapshot().ToString());
   }}finally{Volatile.Write(ref stop,1);Need(thread.Join(2000),"busy-thread-cleanup");}
  }
  // Suspend only a separate test process: a paused CLR thread in the sampler's
  // own runtime can block GC and invalidate the observation experiment itself.
  using(var child=Process.Start(new ProcessStartInfo(Process.GetCurrentProcess().MainModule.FileName,"--busy-child"){UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true})){
   IntPtr suspend=IntPtr.Zero;bool paused=false;
   try{uint workerId=uint.Parse(child.StandardOutput.ReadLine());using(var probe=new OwnedThreadEvidence(child,workerId)){
    bool rejected=false;try{using(var wrong=new OwnedThreadEvidence(owned,workerId)){}}catch(InvalidOperationException e){rejected=e.Message=="thread-process-identity-mismatch";}Need(rejected,"wrong-process-identity-rejected");
    suspend=OpenThread(2,false,workerId);Need(suspend!=IntPtr.Zero,"test-only-suspend-handle");Need(SuspendThread(suspend)!=uint.MaxValue,"test-thread-paused");paused=true;
    bool seen=false;for(int i=0;i<20;i++){var row=probe.Capture();seen|=Suspended(row);Thread.Sleep(5);}Need(seen,"controlled-suspended-observed");
    var a=probe.Capture();Thread.Sleep(30);var b=probe.Capture();Need(Flat(a,b),"suspended-cycles-flat");
    File.WriteAllText(Path.Combine(output,"busy-suspended.json"),probe.Snapshot().ToString());
   }}finally{
    try{if(paused)Need(ResumeThread(suspend)!=uint.MaxValue,"test-thread-resumed");}
    finally{try{if(suspend!=IntPtr.Zero)Need(CloseHandle(suspend),"suspend-handle-closed");}finally{if(!child.HasExited)child.Kill();Need(child.WaitForExit(2000),"suspended-child-cleanup");}}
   }
  }
 }
 [DllImport("kernel32.dll",SetLastError=true)]static extern IntPtr OpenThread(uint access,bool inherit,uint tid);
 [DllImport("kernel32.dll",SetLastError=true)]static extern uint SuspendThread(IntPtr handle);
 [DllImport("kernel32.dll",SetLastError=true)]static extern uint ResumeThread(IntPtr handle);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll",SetLastError=true)]static extern void SetLastError(uint error);
}
