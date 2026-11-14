using System;using System.Diagnostics;using System.IO;using System.IO.MemoryMappedFiles;using System.Runtime.InteropServices;using System.Threading;using Newtonsoft.Json.Linq;
using SpatialWallpaper.Supervision.Interactive;
static class DesktopHitThreadBindingChecks {
 [DllImport("kernel32.dll")]static extern uint GetCurrentThreadId();
 [DllImport("kernel32.dll")]static extern IntPtr GetCurrentThread();
 [DllImport("kernel32.dll")]static extern bool GetThreadTimes(IntPtr thread,out ulong creation,out ulong exit,out ulong kernel,out ulong user);
 static void Need(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static int Main(string[] args){try{using(var process=Process.GetCurrentProcess())using(var map=MemoryMappedFile.CreateNew(null,96))using(var view=map.CreateViewAccessor()){
  ulong creation,exit,kernel,user;Need(GetThreadTimes(GetCurrentThread(),out creation,out exit,out kernel,out user),"actual-thread-creation");Guid generation=Guid.NewGuid();
  view.Write(0,2);view.Write(4,1U);view.Write(8,(uint)process.Id);view.Write(12,GetCurrentThreadId());view.Write(16,(ulong)process.StartTime.ToUniversalTime().ToFileTimeUtc());view.Write(24,creation);view.WriteArray(32,generation.ToByteArray(),0,16);view.Write(48,2L);view.Write(56,1L);view.Write(64,2L);view.Write(72,Stopwatch.GetTimestamp());
  using(var binding=new DesktopHitThreadBinding(process,generation,view,()=>2,()=>"sequence=1;stages=controlled")){
   var sample=binding.Read();Need(sample.Available&&sample.Sequence==1&&sample.Generation==generation,"trusted-explicit-thread-binding");
   using(var sampler=new OwnedThreadSampler(process,binding)){
    var clock=Stopwatch.StartNew();while((long)sampler.Snapshot()["total"]<270&&clock.ElapsedMilliseconds<7000)Thread.Sleep(10);
    var result=sampler.Snapshot();File.WriteAllText(Path.Combine(args[0],"bound-samples.json"),result.ToString());Need((long)result["total"]>=270&&((JArray)result["samples"]).Count==256,"bound-ring-capacity");
    bool all=true;foreach(JObject row in (JArray)result["samples"])all&=(string)row["status"]=="observed"&&(bool)row["same_request"]&&(long)row["sequence"]==1;Need(all,"bound-sample-consistent-request");
   }
   view.Write(48,3L);Need(!binding.Read().Available,"odd-request-commit-rejected");view.Write(48,4L);
   view.Write(56,2L);Need(!binding.Read().Available,"trace-request-mismatch-rejected");view.Write(56,1L);
   view.WriteArray(32,Guid.NewGuid().ToByteArray(),0,16);Need(!binding.Read().Available,"changed-generation-rejected");
   bool rejected=false;try{using(var stale=new OwnedThreadSampler(process,binding)){}}catch(InvalidDataException){rejected=true;}Need(rejected,"old-generation-sampler-rejected");
   view.WriteArray(32,generation.ToByteArray(),0,16);
   binding.Dispose();Need(!binding.Read().Available,"disposed-binding-unavailable");
  }
  bool wrong=false;try{using(var binding=new DesktopHitThreadBinding(process,Guid.NewGuid(),view,()=>2,()=>"sequence=1;")){}}catch(InvalidDataException){wrong=true;}Need(wrong,"wrong-generation-constructor-rejected");
  view.Write(24,creation+1);wrong=false;try{using(var binding=new DesktopHitThreadBinding(process,generation,view,()=>2,()=>"sequence=1;")){}}catch(InvalidDataException){wrong=true;}Need(wrong,"actual-thread-creation-required");view.Write(24,creation);
  view.Write(8,0U);wrong=false;try{using(var binding=new DesktopHitThreadBinding(process,generation,view,()=>2,()=>"sequence=1;")){}}catch(InvalidDataException){wrong=true;}Need(wrong,"actual-process-identity-required");view.Write(8,(uint)process.Id);
  using(var changing=new DesktopHitThreadBinding(process,generation,view,()=>2,()=>{view.Write(48,view.ReadInt64(48)+2);return "sequence=1;";}))Need(!changing.Read().Available,"commit-change-during-trace-rejected");
 }
  DesktopHitThreadBinding oldBinding;Guid oldGeneration;
  foreach(string stage in new[]{"create","seed-view","seed-write","read-view","ready"}){
   bool injected=false,mapReleased=false,seedReleased=false,viewReleased=false;using(var degraded=new DesktopHitClient(args[1],at=>{if(at=="release-mapping")mapReleased=true;if(at=="release-seed")seedReleased=true;if(at=="release-view")viewReleased=true;if(at==stage){injected=true;throw new IOException("controlled-diagnostic-"+stage);}})){
    Need(injected,"optional-fault-injected-"+stage);Need(degraded.ThreadDiagnostic==null&&degraded.ThreadDiagnosticError!=null,"optional-diagnostic-unavailable-"+stage);
    Need(mapReleased==(stage!="create")&&seedReleased==(stage=="seed-write")&&viewReleased==(stage=="ready"),"allocated-diagnostic-resources-released-"+stage);
    Need(degraded.Query(0,0)!=null&&degraded.Failure==null,"normal-query-survives-"+stage);degraded.Dispose();Need(degraded.Completion.Wait(4000),"degraded-helper-cleanup-"+stage);degraded.Completion.GetAwaiter().GetResult();
   }
  }
  bool releasedSeed=false,releasedMapping=false;
  using(var cleanupFault=new DesktopHitClient(args[1],at=>{if(at=="seed-write")throw new IOException("controlled-seed-failure");if(at=="release-seed"){releasedSeed=true;throw new IOException("controlled-seed-release-failure");}if(at=="release-mapping")releasedMapping=true;})){
   Need(releasedSeed&&releasedMapping,"diagnostic-cleanup-continues-after-release-error");Need(cleanupFault.ThreadDiagnostic==null&&cleanupFault.Query(0,0)!=null,"cleanup-failure-does-not-break-query");
   cleanupFault.Dispose();bool failed=false;try{cleanupFault.Completion.Wait(4000);}catch(AggregateException){failed=true;}Need(failed&&cleanupFault.Completion.IsFaulted,"diagnostic-release-failure-remains-cleanup-failure");
  }
  using(var client=new DesktopHitClient(args[1])){
   Need(client.ThreadDiagnostic!=null,"pipe-authenticated-helper-binding");var binding=client.ThreadDiagnostic;var live=binding.Read();Need(live.Available&&live.Sequence==0,"actual-helper-warmup-identity-request");
   using(var actual=Process.GetProcessById(client.ProcessId))using(var sampler=new OwnedThreadSampler(actual,binding)){
    Thread.Sleep(80);var samples=sampler.Snapshot();File.WriteAllText(Path.Combine(args[0],"actual-helper-samples.json"),samples.ToString());Need((long)samples["total"]>0&&((JArray)samples["samples"]).Count>0,"actual-helper-explicit-thread-sampled");
    foreach(JObject row in (JArray)samples["samples"])Need((string)row["status"]=="observed"&&(uint)row["thread"]["tid"]==binding.ThreadId,"actual-helper-thread-matches-publication");
   }
   oldBinding=binding;oldGeneration=binding.Generation;
   client.Dispose();Need(client.Completion.Wait(4000),"actual-helper-cleanup-completed");client.Completion.GetAwaiter().GetResult();Need(!binding.Read().Available,"old-helper-binding-unavailable-after-close");
  }
  using(var replacement=new DesktopHitClient(args[1])){
   Need(replacement.ThreadDiagnostic!=null&&replacement.ThreadDiagnostic.Generation!=oldGeneration,"replacement-helper-new-generation");
   using(var actual=Process.GetProcessById(replacement.ProcessId)){
    bool rejected=false;try{using(var stale=new OwnedThreadSampler(actual,oldBinding)){}}catch(ObjectDisposedException){rejected=true;}Need(rejected,"old-binding-rejected-for-new-helper");
   }
   replacement.Dispose();Need(replacement.Completion.Wait(4000),"replacement-helper-cleanup");replacement.Completion.GetAwaiter().GetResult();
  }
  return 0;}catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
