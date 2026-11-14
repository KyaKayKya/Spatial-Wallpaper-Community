using System;using System.IO;using System.Text;using System.Threading;using System.Diagnostics;using System.Collections.Concurrent;using Newtonsoft.Json.Linq;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Windows;using SpatialWallpaper.Sdk.Hosting;
static class UnityStreamChecks{
 readonly struct Received{internal readonly long Sequence,At;internal Received(long sequence,long at){Sequence=sequence;At=at;}}
 static int Main(string[] args){var diagnostic=new StringBuilder();try{
  string root=Path.GetFullPath(args[0]),output=Path.GetFullPath(args[1]);var identity=new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1);
  var clock=Stopwatch.StartNew();PixelFramePipe frames=null;WorkerWritePipe logs=null;long logHandle=0;int frameError=0,logError=0;long count=0;
  var received=new BlockingCollection<Received>(16);var frameDone=new ManualResetEvent(false);var logDone=new ManualResetEvent(false);
  var command=new[]{"-batchmode","-force-d3d11","-screen-width","640","-screen-height","360","-screen-fullscreen","0","-logFile","-","--sdk-package",Path.Combine(root,"scene.spatial"),"--sdk-attempt",identity.Attempt.ToString("N"),"--sdk-instance",identity.Instance.ToString("N"),"--sdk-generation","1","--sdk-frame-handle","0"};
  using(var station=new PrivateStation())using(var child=RestrictedProcess.Start(station,Path.Combine(root,"StageD.exe"),command,root,512L*1024*1024,sid=>{
   frames=new PixelFramePipe(sid,identity,640,360);logs=new WorkerWritePipe(sid);logHandle=logs.ChildWriter.ToInt64();command[command.Length-1]=frames.ChildWriter.ToInt64().ToString();return new[]{frames.ChildWriter,logs.ChildWriter};
  },pid=>{frames.CloseChildCopy();logs.CloseChildCopy();},identity.Attempt,()=>new IntPtr(logHandle))){
   var frameReader=new Thread(()=>{try{while(frames.Receive()){Interlocked.Increment(ref count);if(!received.TryAdd(new Received(frames.Sequence,clock.ElapsedMilliseconds),1000))throw new Exception("frame-consumer-capacity");}}catch{Interlocked.Exchange(ref frameError,1);}finally{frameDone.Set();}}){IsBackground=true};
   var logReader=new Thread(()=>{try{var bytes=new byte[4096];int n;while((n=logs.Read(bytes,0,bytes.Length))>0){lock(diagnostic){if(diagnostic.Length<65536){string text=Encoding.UTF8.GetString(bytes,0,n);diagnostic.Append(text,0,Math.Min(text.Length,65536-diagnostic.Length));}}}}catch{Interlocked.Exchange(ref logError,1);}finally{logDone.Set();}}){IsBackground=true};
   frameReader.Start();logReader.Start();
   var guard=new WorkerWatchdog(new WatchdogPolicy(15000,1000,500,512L*1024*1024,2000,8000),0);var process=Process.GetProcessById(child.ProcessId);
   while(clock.ElapsedMilliseconds<30000){
    Received observation;while(received.TryTake(out observation))if(guard.CompletedFrame(clock.ElapsedMilliseconds,observation.Sequence,observation.At)==GuardAction.Kill)throw new Exception("frame-watchdog:"+guard.Reason);
    process.Refresh();if(process.HasExited)break;
    if(guard.Poll(clock.ElapsedMilliseconds,true,process.PrivateMemorySize64,(long)process.TotalProcessorTime.TotalMilliseconds)==GuardAction.Kill)throw new Exception("worker-watchdog:"+guard.Reason);
    if(Interlocked.CompareExchange(ref frameError,0,0)!=0)throw new Exception("pixel-frame-reader-failed");
    Thread.Sleep(10);
   }
   var exitWait=Stopwatch.StartNew();uint finalWait=child.Wait(1000);if(finalWait!=0)throw new Exception("streaming-worker-deadline wait="+finalWait+" exit="+child.ExitCode+" elapsed="+clock.ElapsedMilliseconds);
   if(child.ExitCode!=0)throw new Exception("streaming-worker-exit:"+child.ExitCode);
   if(!frameDone.WaitOne(2000)||frameError!=0||Interlocked.Read(ref count)<1)throw new Exception("missing-complete-frames");
   if(!logDone.WaitOne(2000)||logError!=0)throw new Exception("log-reader-failed");
   File.WriteAllBytes(Path.Combine(output,"last-frame.rgba"),frames.Pixels);
   File.WriteAllText(Path.Combine(output,"result.json"),new JObject{{"frames_received",Interlocked.Read(ref count)},{"final_sequence",frames.Sequence},{"exit_code",child.ExitCode},{"elapsed_ms",clock.ElapsedMilliseconds},{"exit_handle_wait_ms",exitWait.ElapsedMilliseconds},{"read_only_runtime",true},{"lpac",true},{"private_window_station",true},{"frame_identity_checked",true}}.ToString());
   Console.WriteLine("PASS readonly LPAC Unity Worker, actual GPU pixel frames and identity-bound shared codec");
  }
  lock(diagnostic)File.WriteAllText(Path.Combine(output,"worker.log"),diagnostic.ToString());
  frames?.Dispose();logs?.Dispose();return 0;
 }catch(Exception error){Console.Error.WriteLine(error.Message);lock(diagnostic)File.WriteAllText(Path.Combine(Path.GetFullPath(args[1]),"worker.log"),diagnostic.ToString());return 1;}}
}
