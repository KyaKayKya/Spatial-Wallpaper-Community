// Original MIT test harness. Separate launch, frame-monitor and bounded persistence lanes.
using System;using System.IO;using System.Text;using System.Threading;using System.Diagnostics;using System.Collections.Concurrent;using System.Security.Cryptography;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Windows;using SpatialWallpaper.Sdk.Hosting;
static class UnityIsolationChecks{
 readonly struct Observation{internal readonly long Sequence,At;internal Observation(long sequence,long at){Sequence=sequence;At=at;}}
 internal sealed class Running:IDisposable{
  internal RestrictedProcess Child;internal PixelFramePipe Pipe;internal WorkerWritePipe Logs;internal Process Os;internal AttemptReceipt Receipt;internal WorkerWatchdog Guard;
  readonly BlockingCollection<Observation> frames=new BlockingCollection<Observation>(16);readonly Stopwatch clock;readonly StringBuilder log=new StringBuilder();Thread reader,logger,monitor;int monitoringStopped,fault;System.Threading.Tasks.Task<bool> readyWrite;
  internal int Count,Error,Heartbeats;internal long MaxGap,PeakPrivate;long lastAt;
  internal FaultReason? Reason=>fault==0?(FaultReason?)null:(FaultReason)(fault-1);
  internal Running(PrivateStation station,string root,string mode,string build,Guid installation,JournalActor journal,Stopwatch time,int launchDelay=0){
   clock=time;Receipt=Await(journal.Begin(installation,build,Guid.NewGuid(),1,StartIntent.Manual));var identity=new WorkerIdentity(Receipt.Attempt,Receipt.Instance,Receipt.Generation);
   Guard=new WorkerWatchdog(new WatchdogPolicy(15000,750,500,512L*1024*1024,2000,8000),clock.ElapsedMilliseconds);
   var args=new[]{"-batchmode","-force-d3d11","-screen-width","640","-screen-height","360","-screen-fullscreen","0","-logFile","-","--sdk-package",Path.Combine(root,"scene.spatial"),"--sdk-attempt",identity.Attempt.ToString("N"),"--sdk-instance",identity.Instance.ToString("N"),"--sdk-generation","1","--sdk-fixture-mode",mode,"--sdk-frame-handle","0"};
   long logHandle=0;
   Child=RestrictedProcess.Start(station,Path.Combine(root,"StageD.exe"),args,root,512L*1024*1024,sid=>{
    Pipe=new PixelFramePipe(sid,identity,640,360);Logs=new WorkerWritePipe(sid);args[args.Length-1]=Pipe.ChildWriter.ToInt64().ToString();logHandle=Logs.ChildWriter.ToInt64();if(launchDelay>0)Thread.Sleep(launchDelay);return new[]{Pipe.ChildWriter,Logs.ChildWriter};
   },pid=>{Await(journal.RecordProcess(Receipt,pid));Pipe.CloseChildCopy();Logs.CloseChildCopy();},Receipt.Attempt,()=>new IntPtr(logHandle));
   Os=Process.GetProcessById(Child.ProcessId);
   reader=new Thread(()=>{try{while(Pipe.Receive())if(!frames.TryAdd(new Observation(Pipe.Sequence,clock.ElapsedMilliseconds),100))throw new Exception("frame-queue-full");}catch(Exception error){lock(log)log.AppendLine("FIXTURE_READER_ERROR:"+error.Message);Interlocked.Exchange(ref Error,1);}}){IsBackground=true};
   logger=new Thread(()=>{try{var bytes=new byte[4096];int n;while((n=Logs.Read(bytes,0,bytes.Length))>0){string text=Encoding.UTF8.GetString(bytes,0,n);if(text.Contains("SDK_D_FIXTURE_HEARTBEAT"))Interlocked.Increment(ref Heartbeats);lock(log)if(log.Length<65536)log.Append(text,0,Math.Min(text.Length,65536-log.Length));}}catch{Interlocked.Exchange(ref Error,1);}}){IsBackground=true};
   reader.Start();logger.Start();
   monitor=new Thread(()=>{
    try{while(Volatile.Read(ref monitoringStopped)==0){
     if(PollCore(journal)==GuardAction.Kill){Interlocked.Exchange(ref fault,1+(int)(Guard.Reason??FaultReason.Protocol));break;}
     Thread.Sleep(1);
    }}catch(Exception error){lock(log)log.AppendLine("FIXTURE_MONITOR_ERROR:"+error.Message);Interlocked.Exchange(ref fault,1+(int)FaultReason.Monitoring);}
   }){IsBackground=true};monitor.Start();
  }
  internal GuardAction Poll()=>Volatile.Read(ref fault)==0?GuardAction.None:GuardAction.Kill;
  GuardAction PollCore(JournalActor journal){
   Observation frame;while(frames.TryTake(out frame)){
    if(lastAt>0)MaxGap=Math.Max(MaxGap,frame.At-lastAt);lastAt=frame.At;Count++;
    var action=Guard.CompletedFrame(clock.ElapsedMilliseconds,frame.Sequence,frame.At);if(action==GuardAction.Ready)readyWrite=journal.MarkRunning(Receipt);if(action==GuardAction.Kill)return action;
   }
   bool alive=Child.Wait(0)==258;long memory=0,cpu=0;
   if(alive){try{Os.Refresh();memory=Os.PrivateMemorySize64;cpu=(long)Os.TotalProcessorTime.TotalMilliseconds;PeakPrivate=Math.Max(PeakPrivate,memory);}catch(InvalidOperationException){alive=Child.Wait(0)==258;if(alive)throw;}}
   if((Error!=0||readyWrite!=null&&readyWrite.IsFaulted)&&alive)return GuardAction.Kill;
   return Guard.Poll(clock.ElapsedMilliseconds,alive,memory,cpu);
  }
  internal void Save(string path){lock(log)File.WriteAllText(path,log.ToString());}
  internal string DrainLog(){if(!logger.Join(1000))throw new Exception("log-drain-timeout");lock(log)return log.ToString();}
  public void Dispose(){Interlocked.Exchange(ref monitoringStopped,1);monitor?.Join(1000);Child?.Kill(125);Child?.Dispose();reader?.Join(1000);logger?.Join(1000);Pipe?.Dispose();Logs?.Dispose();Os?.Dispose();frames.Dispose();}
 }
 static T Await<T>(System.Threading.Tasks.Task<T> task){if(!task.Wait(5000))throw new Exception("journal-actor-timeout");return task.GetAwaiter().GetResult();}
 static int Main(string[] args){try{
  string healthyRoot=args[0],badRoot=args[1],output=args[2],build=args[3];var results=new JArray();Guid installation=Guid.NewGuid();
  var journal=new JournalActor(Path.Combine(output,"journal"),32);
  try{using(var station=new PrivateStation()){
   foreach(string mode in new[]{"render-stall","bridge-block","cpu"}){
    var clock=Stopwatch.StartNew();Running healthy=null,bad=null;
    try{
     healthy=new Running(station,healthyRoot,"continuous",build,installation,journal,clock);
     while(healthy.Count<5&&clock.ElapsedMilliseconds<16000){if(healthy.Poll()==GuardAction.Kill)throw new Exception("healthy-start-failed:"+healthy.Reason);Thread.Sleep(10);}
     if(healthy.Count<5)throw new Exception("healthy-missing-real-frames");
     int beforeLaunch=healthy.Count;bad=new Running(station,badRoot,mode,build,installation,journal,clock,1000);int duringLaunch=healthy.Count-beforeLaunch;
     if(duringLaunch<10)throw new Exception("healthy-monitor-stalled-during-worker-launch");
     long faultAt=-1,termination=0,killCall=0;int framesAtFault=0,heartbeatsAtFault=0;
     while(clock.ElapsedMilliseconds<30000){
      if(healthy.Poll()==GuardAction.Kill)throw new Exception("healthy-fault:"+healthy.Reason);
      if(faultAt<0&&bad.Poll()==GuardAction.Kill){
       if(bad.Reason!=FaultReason.FrameTimeout||bad.Count<5)throw new Exception("wrong-bad-fault:"+bad.Reason+":"+bad.Count);
       faultAt=clock.ElapsedMilliseconds;framesAtFault=healthy.Count;heartbeatsAtFault=bad.Heartbeats;var timer=Stopwatch.StartNew();bad.Child.Kill(124);killCall=timer.ElapsedMilliseconds;
       if(bad.Child.Wait(1000)!=0)throw new Exception("bad-termination-timeout");termination=timer.ElapsedMilliseconds;Await(journal.Fail(bad.Receipt,FaultReason.FrameTimeout));
      }
      if(faultAt>=0&&clock.ElapsedMilliseconds-faultAt>=500)break;Thread.Sleep(10);
     }
     if(faultAt<0||healthy.Count<framesAtFault+3||healthy.Child.Wait(0)!=258||heartbeatsAtFault<1)throw new Exception("Unity-isolation-or-live-heartbeat-missing");
     if(!Await(journal.IsQuarantined(installation,build)))throw new Exception("faulted-build-not-quarantined");
     bool denied=false;try{Await(journal.Begin(installation,build,Guid.NewGuid(),2,StartIntent.Resume));}catch(AggregateException error){denied=error.GetBaseException().Message=="build-quarantined";}if(!denied)throw new Exception("faulted-build-auto-restart");
     results.Add(new JObject{{"mode",mode},{"fault","FrameTimeout"},{"bad_real_frames",bad.Count},{"bad_heartbeats",heartbeatsAtFault},{"healthy_frames_after_fault",healthy.Count-framesAtFault},{"healthy_frames_during_1s_launch_delay",duringLaunch},{"fault_at_ms",faultAt},{"kill_call_ms",killCall},{"kernel_exit_ms",termination},{"healthy_max_frame_gap_ms",healthy.MaxGap},{"healthy_peak_private_bytes",healthy.PeakPrivate},{"bad_peak_private_bytes",bad.PeakPrivate},{"automatic_resume_denied",true}});
     Await(journal.MarkStopping(healthy.Receipt));healthy.Child.Kill(0);if(healthy.Child.Wait(1000)!=0)throw new Exception("healthy-stop-timeout");Await(journal.Complete(healthy.Receipt));
    }finally{bad?.Dispose();healthy?.Dispose();healthy?.Save(Path.Combine(output,mode+"-healthy.log"));bad?.Save(Path.Combine(output,mode+"-bad.log"));}
   }
  }}finally{journal.Close();try{journal.Completion.Wait(5000);}catch{}}
  File.WriteAllText(Path.Combine(output,"results.json"),results.ToString());Console.WriteLine(results);return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
