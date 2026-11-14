// Original MIT verification fixture. Runtime scheduling tests use controlled process boundaries.
using System;using System.IO;using System.Threading;using System.Threading.Tasks;using SpatialWallpaper.Supervision;
static class RuntimeChecks {
 sealed class Fake:IManagedWorker {
  internal uint? Exit;internal int Kills,Disposed,Stops,ThrowNextSample;internal long Published=-1;internal bool IgnoreStop,NoPublish;internal uint StopExit;internal bool Alive=true,Frames=true;long sequence;internal readonly System.Diagnostics.Stopwatch Clock;
  internal Fake(System.Diagnostics.Stopwatch clock){Clock=clock;}
  public WorkerSample Sample(){if(Interlocked.Exchange(ref ThrowNextSample,0)!=0)throw new IOException("injected-sample-failure");Thread.Sleep(3);long now=Clock.ElapsedMilliseconds;return new WorkerSample(Alive,1024,0,Frames?++sequence:sequence,Frames?now:0,false,exitCode:Exit,stopPublishedAt:Published);}
  public int ProcessId=>12345;
  public void RequestStop(){Interlocked.Increment(ref Stops);if(!NoPublish)Published=Clock.ElapsedMilliseconds;if(!IgnoreStop){Exit=StopExit;Alive=false;}}
  public void Kill(){Interlocked.Increment(ref Kills);Exit=124;Published=Clock.ElapsedMilliseconds;Alive=false;}
  public void Dispose(){Interlocked.Increment(ref Disposed);}
 }
 sealed class Launcher:IWorkerLauncher {
  internal readonly System.Diagnostics.Stopwatch Clock;internal Fake Last;internal ManualResetEventSlim Gate=new ManualResetEventSlim(true);int pid=12345;
  internal Launcher(System.Diagnostics.Stopwatch clock){Clock=clock;}
  public IManagedWorker Start(LaunchRequest request,AttemptReceipt receipt,Action<int> created){Gate.Wait();created(Interlocked.Increment(ref pid));return Last=new Fake(Clock);}
 }
 static void Require(bool value,string message){if(!value)throw new Exception(message);}
 static bool CanUninstall(SupervisorRuntime runtime,Guid installation){var method=typeof(SupervisorRuntime).GetMethod("CanUninstall");Require(method!=null,"missing-runtime-installation-release-check");return (bool)method.Invoke(runtime,new object[]{installation});}
 static void Wait(Func<bool> condition){var clock=System.Diagnostics.Stopwatch.StartNew();while(!condition()){if(clock.ElapsedMilliseconds>5000)throw new Exception("condition-timeout");Thread.Sleep(2);}}
 static int Main(string[] args){try{
  var stoppedBeforeReady=new SupervisedSession(null,null,null);stoppedBeforeReady.Stop();stoppedBeforeReady.Running();Require(!stoppedBeforeReady.Ready.IsCompleted,"ready-published-after-stop");
  var clock=System.Diagnostics.Stopwatch.StartNew();var launch=new Launcher(clock);var journal=new JournalActor(args[0],16);
  var policy=new WatchdogPolicy(1000,100,50,1024*1024,1000,1000);
  var runtime=new SupervisorRuntime(journal,launch,policy,2,2*1024*1024,()=>clock.ElapsedMilliseconds);
  Guid install=Guid.NewGuid();string build=new string('a',64);
  var healthy=runtime.Start(new LaunchRequest(install,build,Guid.NewGuid(),1,StartIntent.Manual,"healthy",1024*1024)).GetAwaiter().GetResult();
  var healthyWorker=launch.Last;Wait(()=>healthy.Ready.IsCompleted);Require(!healthy.Ready.IsFaulted,"ready-not-durable");
  Require(!CanUninstall(runtime,install)&&CanUninstall(runtime,Guid.NewGuid()),"running-installation-not-owned-or-neighbor-blocked");
  launch.Gate.Reset();var pending=runtime.Start(new LaunchRequest(install,build,Guid.NewGuid(),1,StartIntent.Manual,"blocked-launch",1024*1024));
  Thread.Sleep(200);Require(healthyWorker.Kills==0,"launch-blocked-healthy-monitor");
  Require(!CanUninstall(runtime,install),"pending-launch-allows-uninstall");
  healthyWorker.Frames=false;Wait(()=>healthyWorker.Kills>0);Require(!pending.IsCompleted,"launch-gate-not-active");
  launch.Gate.Set();var second=pending.GetAwaiter().GetResult();Wait(()=>healthy.Completion.IsCompleted);
  Require(healthy.Fault==FaultReason.FrameTimeout&&healthyWorker.Disposed==1,"fault-not-reclaimed");
  second.Stop();Wait(()=>second.Completion.IsCompleted);Require(launch.Last.Disposed==1,"normal-stop-not-cleaned");Require(launch.Last.Kills==0,"normal-stop-used-force-kill");
  Require(CanUninstall(runtime,install),"cleaned-installation-remains-owned");
  bool denied=false;try{runtime.Start(new LaunchRequest(install,build,Guid.NewGuid(),2,StartIntent.Resume,"same-build",1024*1024)).GetAwaiter().GetResult();}catch(InvalidOperationException){denied=true;}Require(denied,"actual-runtime-resumed-quarantined-build");
  var restored=runtime.Restore(new LaunchRequest(install,new string('b',64),healthy.Receipt.Instance,2,StartIntent.Resume,"healthy-old-revision",1024*1024),build).GetAwaiter().GetResult();Wait(()=>restored.Ready.IsCompleted);Require(!restored.Ready.IsFaulted,"rollback-never-ready");var restoredWorker=launch.Last;restoredWorker.Frames=false;Wait(()=>restored.Completion.IsCompleted);
  bool loop=false;try{runtime.Restore(new LaunchRequest(install,new string('c',64),healthy.Receipt.Instance,3,StartIntent.Resume,"third-revision",1024*1024),new string('b',64)).GetAwaiter().GetResult();}catch(InvalidOperationException error){loop=error.Message=="rollback-not-available";}Require(loop,"failed-rollback-permitted-another-automatic-rollback");
  var budget=runtime.Start(new LaunchRequest(Guid.NewGuid(),new string('d',64),Guid.NewGuid(),1,StartIntent.Manual,"reported-budget",1024*1024)).GetAwaiter().GetResult();var budgetWorker=launch.Last;budgetWorker.Exit=32;budgetWorker.Alive=false;Wait(()=>budget.Completion.IsCompleted);Require(budget.Fault==FaultReason.Budget,"known-Worker-budget-lost");
  Require(second.GracefulStop&&second.ForcedAt==-1&&second.ExitedAt>=second.StopAcceptedAt&&second.CleanedAt>=second.ExitedAt,"normal-stop-evidence");
  var hung=runtime.Start(new LaunchRequest(Guid.NewGuid(),new string('e',64),Guid.NewGuid(),1,StartIntent.Manual,"ignore-stop",1024*1024)).GetAwaiter().GetResult();var hungWorker=launch.Last;hungWorker.IgnoreStop=true;hung.Stop();Wait(()=>hung.Completion.IsCompleted);Require(!hung.GracefulStop&&hung.Fault==FaultReason.StopTimeout&&hungWorker.Stops==1&&hung.ForcedAt-hung.StopAcceptedAt>=50,"grace-before-forced-kill");
  var crashed=runtime.Start(new LaunchRequest(Guid.NewGuid(),new string('f',64),Guid.NewGuid(),1,StartIntent.Manual,"crash-stop",1024*1024)).GetAwaiter().GetResult();launch.Last.StopExit=32;crashed.Stop();Wait(()=>crashed.Completion.IsCompleted);Require(!crashed.GracefulStop&&crashed.ForcedAt<0&&launch.Last.Kills==0&&crashed.Fault==FaultReason.Budget,"nonzero-stop-not-graceful");
  var unpublished=runtime.Start(new LaunchRequest(Guid.NewGuid(),new string('c',64),Guid.NewGuid(),1,StartIntent.Manual,"no-published-stop",1024*1024)).GetAwaiter().GetResult();launch.Last.NoPublish=true;unpublished.Stop();Wait(()=>unpublished.Completion.IsCompleted);Require(!unpublished.GracefulStop&&unpublished.Fault==FaultReason.ProcessExit&&unpublished.ForcedAt<0,"unpublished-zero-exit-not-graceful");
  var sampleFault=runtime.Start(new LaunchRequest(Guid.NewGuid(),new string('b',64),Guid.NewGuid(),1,StartIntent.Manual,"sample-failure",1024*1024)).GetAwaiter().GetResult();var sampleFaultWorker=launch.Last;Wait(()=>sampleFault.Ready.IsCompleted);Interlocked.Exchange(ref sampleFaultWorker.ThrowNextSample,1);Wait(()=>sampleFault.Completion.IsCompleted);Require((uint?)sampleFault.StopEvidence["observed_exit_code"]==124&&(long?)sampleFault.StopEvidence["stop_published_at_ms"]==sampleFaultWorker.Published,"final-sample-evidence-retained-after-monitor-error");
  launch.Gate.Reset();var lateLaunch=runtime.Start(new LaunchRequest(Guid.NewGuid(),new string('a',64),Guid.NewGuid(),1,StartIntent.Manual,"late-stop",1024*1024));Thread.Sleep(30);runtime.Close();launch.Gate.Set();var lateSession=lateLaunch.GetAwaiter().GetResult();Wait(()=>lateSession.Completion.IsCompleted);Require(launch.Last.Stops==1&&launch.Last.Kills==0&&launch.Last.Disposed==1&&lateSession.GracefulStop,"late-created-worker-owned-normal-stop");Wait(()=>runtime.Completion.IsCompleted);journal.Close();journal.Completion.GetAwaiter().GetResult();
  Console.WriteLine("PASS independent launch/monitor/cleanup, durable-ready, stop, quarantine and shutdown");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
