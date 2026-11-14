using System;using System.IO;using System.Threading;using System.Diagnostics;using System.Collections.Concurrent;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Windows;
static class ProcessSupervisionChecks {
 readonly struct Observation {internal readonly long Sequence,At;internal Observation(long sequence,long at){Sequence=sequence;At=at;}}
 sealed class Running:IDisposable {
  internal readonly BlockingCollection<Observation> Frames=new BlockingCollection<Observation>(16);internal readonly WorkerWatchdog Guard;internal readonly AttemptReceipt Receipt;internal RestrictedProcess Child;internal FrameSignalPipe Pipe;internal Process Os;internal int Error,Count;internal long MaxGap,lastAt;readonly Stopwatch clock;
  internal Running(PrivateStation station,string root,string mode,FaultJournal journal,Stopwatch clock){
   this.clock=clock;
   Receipt=journal.Begin(Guid.NewGuid(),new string(mode=="--healthy"?'a':'b',64),Guid.NewGuid(),1,StartIntent.Manual);
   Guard=new WorkerWatchdog(new WatchdogPolicy(1500,300,200,24L*1024*1024,200,120),clock.ElapsedMilliseconds);
   var arguments=new[]{mode,"0"};
   Child=RestrictedProcess.Start(station,Path.Combine(root,"ProcessProbe.exe"),arguments,root,128L*1024*1024,sid=>{
    Pipe=new FrameSignalPipe(sid);
    arguments[1]=Pipe.ChildWriter.ToInt64().ToString(System.Globalization.CultureInfo.InvariantCulture);
    return new[]{Pipe.ChildWriter};
   },pid=>{journal.RecordProcess(Receipt,pid);Pipe.CloseChildCopy();},Receipt.Attempt);
   Os=Process.GetProcessById(Child.ProcessId);
  }
  internal void Read(){
   var thread=new Thread(()=>{try{bool frame;long sequence;while(Pipe.Receive(out frame,out sequence)){if(frame&&!Frames.TryAdd(new Observation(sequence,clock.ElapsedMilliseconds),100)){Interlocked.Exchange(ref Error,1);break;}}}catch{Interlocked.Exchange(ref Error,1);}});
   thread.IsBackground=true;thread.Start();
  }
  internal GuardAction Poll(Stopwatch clock,FaultJournal journal){
   Observation observed;while(Frames.TryTake(out observed)){if(lastAt>0)MaxGap=Math.Max(MaxGap,observed.At-lastAt);lastAt=observed.At;var action=Guard.CompletedFrame(clock.ElapsedMilliseconds,observed.Sequence,observed.At);Count++;if(action==GuardAction.Ready)journal.MarkRunning(Receipt);if(action==GuardAction.Kill)return action;}
   if(Interlocked.CompareExchange(ref Error,0,0)!=0)throw new Exception("signal-reader-failed");
   Os.Refresh();bool alive=!Os.HasExited;return Guard.Poll(clock.ElapsedMilliseconds,alive,alive?Os.PrivateMemorySize64:0,alive?(long)Os.TotalProcessorTime.TotalMilliseconds:0);
  }
  public void Dispose(){Child?.Kill(125);Child?.Dispose();Pipe?.Dispose();Os?.Dispose();}
 }
 static string Copy(string source,string parent){
  string root=Path.Combine(parent,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(root);
  File.Copy(Path.Combine(source,"ProcessProbe.exe"),Path.Combine(root,"ProcessProbe.exe"));File.WriteAllText(Path.Combine(root,".sdk-worker-owned"),"SDK generated supervision fixture");return root;
 }
 static int Main(string[] args){try{
  var observations=new JArray();
  using(var station=new PrivateStation())using(var journal=new FaultJournal(Path.Combine(args[1],"journal"))){
   foreach(string mode in new[]{"--blocked","--heartbeat","--startup-heartbeat","--memory","--cpu"}){
    var clock=Stopwatch.StartNew();Running healthy=null,bad=null;
    try{
     healthy=new Running(station,Copy(args[0],args[1]),"--healthy",journal,clock);healthy.Read();
     bad=new Running(station,Copy(args[0],args[1]),mode,journal,clock);bad.Read();
     long killedAt=-1,killCall=0,terminated=0;int framesAtKill=0;
     while(clock.ElapsedMilliseconds<6000){
      if(healthy.Poll(clock,journal)==GuardAction.Kill)throw new Exception("healthy-worker-faulted:"+healthy.Guard.Reason);
      if(killedAt<0&&bad.Poll(clock,journal)==GuardAction.Kill){
       killedAt=clock.ElapsedMilliseconds;framesAtKill=healthy.Count;
       var termination=Stopwatch.StartNew();bad.Child.Kill(124);killCall=termination.ElapsedMilliseconds;
       if(bad.Child.Wait(1000)!=0)throw new Exception("problem-worker-did-not-terminate");
       terminated=termination.ElapsedMilliseconds;journal.Fail(bad.Receipt,bad.Guard.Reason.Value);
       if(!journal.IsQuarantined(bad.Receipt.Installation,bad.Receipt.BuildId))throw new Exception("problem-build-not-quarantined");
      }
      if(killedAt>=0&&clock.ElapsedMilliseconds-killedAt>=250)break;
      Thread.Sleep(10);
     }
     if(killedAt<0||healthy.Count<=framesAtKill+2||healthy.Os.HasExited)throw new Exception("multi-worker-isolation-failed");
     var expected=mode=="--memory"||mode=="--cpu"?FaultReason.Budget:mode=="--startup-heartbeat"?FaultReason.StartTimeout:FaultReason.FrameTimeout;
     if(bad.Guard.Reason!=expected)throw new Exception("unexpected-fault:"+mode+":"+bad.Guard.Reason);
     bool denied=false;try{journal.Begin(bad.Receipt.Installation,bad.Receipt.BuildId,Guid.NewGuid(),2,StartIntent.Resume);}catch(InvalidOperationException){denied=true;}if(!denied)throw new Exception("faulted-build-auto-resumed");
     observations.Add(new JObject{{"mode",mode},{"fault",expected.ToString()},{"fault_at_ms",killedAt},{"healthy_frames",healthy.Count},{"healthy_frames_after_fault",healthy.Count-framesAtKill},{"kill_call_ms",killCall},{"terminated_ms",terminated},{"healthy_max_received_frame_gap_ms",healthy.MaxGap}});
     healthy.Guard.RequestStop(clock.ElapsedMilliseconds);journal.MarkStopping(healthy.Receipt);healthy.Child.Kill(0);if(healthy.Child.Wait(1000)!=0)throw new Exception("healthy-cleanup-timeout");journal.Complete(healthy.Receipt);
    }finally{bad?.Dispose();healthy?.Dispose();}
   }
  }
  File.WriteAllText(Path.Combine(args[1],"results.json"),observations.ToString());Console.WriteLine(observations);return 0;
 }catch(Exception error){Console.Error.WriteLine(error.Message);return 1;}}
}
