using System;using SpatialWallpaper.Supervision;
static class WatchdogChecks{
 static void Assert(bool yes,string message){if(!yes)throw new Exception(message);}
 static WatchdogPolicy Policy()=>new WatchdogPolicy(1000,100,50,1024,200,50);
 static void Main(){try{Run();}catch(Exception error){Console.Error.WriteLine(error.Message);Environment.Exit(1);}}
 static void Run(){
  var a=new WorkerWatchdog(Policy(),0);var b=new WorkerWatchdog(Policy(),0);
  Assert(a.Poll(999,true,128,0)==GuardAction.None,"startup-stopped-early");
  a.NoteHeartbeat(1000);Assert(a.Poll(1001,true,128,0)==GuardAction.Kill,"heartbeat-bypassed-startup");
  Assert(a.Reason==FaultReason.StartTimeout&&a.State==GuardState.Quarantined,"startup-not-quarantined");
  b.CompletedFrame(999,1);Assert(b.State==GuardState.Running,"first-frame-not-running");
  b.NoteHeartbeat(1100);Assert(b.Poll(1100,true,128,0)==GuardAction.Kill&&b.Reason==FaultReason.FrameTimeout,"heartbeat-hid-frozen-render");
  var healthy=new WorkerWatchdog(Policy(),0);healthy.CompletedFrame(1,1);
  for(int i=2;i<100;i++){healthy.CompletedFrame(i,i);Assert(healthy.Poll(i,true,128,0)==GuardAction.None,"other-worker-stopped");}
  healthy.RequestStop(100);Assert(healthy.Poll(149,true,128,0)==GuardAction.None,"stop-grace-lost");
  Assert(healthy.Poll(150,true,128,0)==GuardAction.Kill&&healthy.Reason==FaultReason.StopTimeout,"blocked-native-call-not-killed");
  var exited=new WorkerWatchdog(Policy(),0);Assert(exited.Poll(1,false,128,0)==GuardAction.Kill&&exited.Reason==FaultReason.ProcessExit,"unexpected-exit-cleared-fault");
  var stopped=new WorkerWatchdog(Policy(),0);stopped.RequestStop(1);Assert(stopped.Poll(2,false,128,0)==GuardAction.Stopped,"normal-stop-quarantined");
  var memory=new WorkerWatchdog(Policy(),0);Assert(memory.Poll(1,true,1025,0)==GuardAction.Kill&&memory.Reason==FaultReason.Budget,"physical-memory-not-bounded");
  var cpu=new WorkerWatchdog(Policy(),0);cpu.Poll(0,true,128,0);
  for(int t=50;t<=200;t+=50)cpu.CompletedFrame(t,t);
  Assert(cpu.Poll(200,true,128,51)==GuardAction.Kill&&cpu.Reason==FaultReason.Budget,"CPU-window-not-bounded");
  var gpuPolicy=new WatchdogPolicy(1000,100,50,1024,200,50,4096,50);
  var gpu=new WorkerWatchdog(gpuPolicy,0);Assert(gpu.Poll(1,true,128,0,4097,1)==GuardAction.Kill&&gpu.Reason==FaultReason.Budget,"actual-GPU-memory-not-bounded");
  var missingGpu=new WorkerWatchdog(gpuPolicy,0);missingGpu.CompletedFrame(1,1);missingGpu.Poll(2,true,128,0,128,2);missingGpu.CompletedFrame(53,2);
  Assert(missingGpu.Poll(53,true,128,0,-1,0)==GuardAction.Kill&&missingGpu.Reason==FaultReason.Monitoring,"stale-GPU-metric-accepted");
  var lateStart=new WorkerWatchdog(Policy(),0);
  Assert(lateStart.CompletedFrame(1001,1)==GuardAction.Kill&&lateStart.Reason==FaultReason.StartTimeout,"late-first-frame-rescued-expired-worker");
  var lateFrame=new WorkerWatchdog(Policy(),0);lateFrame.CompletedFrame(1,1);
  Assert(lateFrame.CompletedFrame(102,2)==GuardAction.Kill&&lateFrame.Reason==FaultReason.FrameTimeout,"late-frame-rescued-expired-render");
  var duplicate=new WorkerWatchdog(Policy(),0);duplicate.CompletedFrame(1,1);
  Assert(duplicate.CompletedFrame(2,1)==GuardAction.Kill&&duplicate.Reason==FaultReason.Protocol,"duplicate-frame-refreshed-progress");
  var repeatedStop=new WorkerWatchdog(Policy(),0);repeatedStop.RequestStop(1);repeatedStop.RequestStop(40);
  Assert(repeatedStop.Poll(52,true,128,0)==GuardAction.Kill,"repeated-stop-extended-deadline");
  var receivedBeforeDeadline=new WorkerWatchdog(Policy(),0);
  Assert(receivedBeforeDeadline.CompletedFrame(1001,1,999)==GuardAction.Ready,"processing-delay-rejected-on-time-received-frame");
  Assert(receivedBeforeDeadline.Poll(1001,true,128,0)==GuardAction.None,"on-time-received-frame-expired");
  var staleQueued=new WorkerWatchdog(Policy(),0);staleQueued.CompletedFrame(1,1,1);
  staleQueued.CompletedFrame(1000,2,50);
  Assert(staleQueued.Poll(1000,true,128,0)==GuardAction.Kill,"processing-stale-queued-frame-refreshed-deadline");
  Console.WriteLine("PASS first-frame/render deadlines, heartbeat rejection, independent workers, stop timeout, process exit, physical memory and CPU");
 }
}
