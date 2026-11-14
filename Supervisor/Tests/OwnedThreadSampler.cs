using System;using System.Diagnostics;using System.Threading;using Newtonsoft.Json.Linq;
using SpatialWallpaper.Supervision.Interactive;
// Test-only bounded observations. No authority decisions, stacks, or ETW claims.
sealed class OwnedThreadSampler:IDisposable {
 readonly Process process;readonly Thread worker;readonly object gate=new object();readonly JObject[] ring=new JObject[256];readonly bool stale;int count,next,closed;long total;string error;
 readonly DesktopHitThreadBinding binding;readonly OwnedThreadEvidence evidence;
 public OwnedThreadSampler(Process owned,bool negativeCachedSnapshot=false):this(owned,negativeCachedSnapshot,null,false){}
 public OwnedThreadSampler(Process owned,DesktopHitThreadBinding explicitBinding):this(owned,false,explicitBinding,true){}
 OwnedThreadSampler(Process owned,bool negativeCachedSnapshot,DesktopHitThreadBinding explicitBinding,bool requireBinding){
  if(owned==null||owned.Handle==IntPtr.Zero||owned.HasExited)throw new ArgumentException("owned-process-not-live");
  process=Process.GetProcessById(owned.Id);
  try{if(process.Handle==IntPtr.Zero||owned.HasExited||process.StartTime!=owned.StartTime)throw new InvalidOperationException("owned-process-identity-changed");
   if(requireBinding){if(explicitBinding==null)throw new InvalidOperationException("helper-thread-binding-missing");explicitBinding.ValidateTarget(process);binding=explicitBinding;evidence=new OwnedThreadEvidence(process,binding.ThreadId,binding.ThreadCreated);}
  }
  catch{process.Dispose();throw;}
  stale=negativeCachedSnapshot;
  try{worker=new Thread(Sample){IsBackground=true,Name="Test-owned helper thread sampler"};worker.Start();}catch{try{evidence?.Dispose();}finally{process.Dispose();}throw;}
 }
 void Sample(){try{while(Volatile.Read(ref closed)==0&&!process.HasExited){
  if(binding!=null){SampleBound();Thread.Sleep(2);continue;}
  long began=Stopwatch.GetTimestamp();if(!stale)process.Refresh();var threads=new JArray();var collection=process.Threads;
  if(collection.Count>64)throw new InvalidOperationException("thread-sample-capacity");
  foreach(ProcessThread thread in collection){
   var state=thread.ThreadState;threads.Add(new JObject{{"id",thread.Id},{"state",state.ToString()},{"wait",state==System.Diagnostics.ThreadState.Wait?thread.WaitReason.ToString():null}});
  }
  // Held process identity must still be live; never attribute a reused PID.
  if(process.HasExited)break;
  var sample=new JObject{{"begin_qpc",began},{"end_qpc",Stopwatch.GetTimestamp()},{"threads",threads}};
  lock(gate){ring[next]=sample;next=(next+1)%ring.Length;count=Math.Min(count+1,ring.Length);total++;}
  Thread.Sleep(2);
 }}catch(Exception failure){lock(gate)error=failure.GetType().Name+":"+failure.Message;}}
 void SampleBound(){
  long began=Stopwatch.GetTimestamp();var before=binding.Read();var observation=evidence.Capture();var after=binding.Read();
  bool same=before.Available&&after.Available&&before.Generation==after.Generation&&before.Sequence==after.Sequence&&before.CommitVersion==after.CommitVersion&&before.TraceVersion==after.TraceVersion;
  var sample=new JObject{{"begin_qpc",began},{"end_qpc",Stopwatch.GetTimestamp()},{"status",same&&(string)observation["status"]=="observed"?"observed":"unavailable"},{"helper_generation",binding.Generation.ToString("N")},{"sequence",after.Sequence},{"request_commit_version",after.CommitVersion},{"trace_version",after.TraceVersion},{"request_commit_qpc",after.CommitQpc},{"trace",after.Trace},{"same_request",same},{"before_error",before.Error},{"after_error",after.Error},{"thread",observation}};
  lock(gate){ring[next]=sample;next=(next+1)%ring.Length;count=Math.Min(count+1,ring.Length);total++;}
 }
 public JObject Snapshot(){lock(gate){var samples=new JArray();for(int i=0;i<count;i++)samples.Add(ring[(next-count+i+ring.Length)%ring.Length].DeepClone());return new JObject{{"scope",binding==null?"legacy test-owned process thread state polling":"explicit helper query thread; same-request bracketing, not atomic scheduler history"},{"pid",process.Id},{"helper_generation",binding?.Generation.ToString("N")},{"frequency",Stopwatch.Frequency},{"capacity",ring.Length},{"total",total},{"error",error},{"samples",samples}};}}
 public void Dispose(){Interlocked.Exchange(ref closed,1);if(!worker.Join(3000))throw new TimeoutException("thread-sampler-stop");try{evidence?.Dispose();}finally{process.Dispose();}}
}
