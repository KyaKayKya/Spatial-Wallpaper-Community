using System;using System.IO;using System.Threading;using SpatialWallpaper.Supervision;
static class HostExitTraceChecks {
 sealed class Owned:IDisposable {internal int Calls;internal Exception Failure;public void Dispose(){Calls++;if(Failure!=null)throw Failure;}}
 static void Need(bool b,string reason){if(!b)throw new Exception(reason);}
 static int Main(string[] args){try{Directory.CreateDirectory(args[0]);
  if(args.Length>1&&args[1]=="--hold"){using(var trace=new HostExitTrace(Path.Combine(args[0],"held.trace")))trace.Run(HostExitPhase.RuntimeWait,()=>Thread.Sleep(5000));return 0;}
  using(var trace=new HostExitTrace(Path.Combine(args[0],"normal.trace"))){trace.Run(HostExitPhase.RuntimeWait,()=>Thread.Sleep(40));trace.Run(HostExitPhase.GpuCloseRequest,()=>{},HostExitEvent.CloseRequested);var resource=new Owned();using(trace.Track(resource,HostExitPhase.StationDispose)){}Need(resource.Calls==1,"tracked-dispose");trace.Mark(HostExitPhase.Main,HostExitEvent.MainReturning,0);}
  var original=new IOException("secret-user-path-and-prompt-must-not-appear");using(var trace=new HostExitTrace(Path.Combine(args[0],"fault.trace"))){Exception caught=null;try{trace.Run(HostExitPhase.StorageWait,()=>{throw original;});}catch(Exception e){caught=e;}Need(ReferenceEquals(caught,original),"original-error-preserved");trace.Run(HostExitPhase.JournalClose,()=>{});trace.Mark(HostExitPhase.Main,HostExitEvent.MainReturning,1);}
  var failures=new CleanupFailures();using(var trace=new HostExitTrace(Path.Combine(args[0],"write-fault.trace"),failures.Record)){trace.BeforeWrite=()=>{throw new IOException("trace-write-fault");};var resource=new Owned{Failure=original};Exception caught=null;try{using(trace.Track(resource,HostExitPhase.InputDispose)){}}catch(Exception e){caught=e;}Need(resource.Calls==1&&ReferenceEquals(caught,original)&&trace.DiagnosticFailed&&ReferenceEquals(failures.Failure,original),"diagnostic-failure-cannot-skip-or-replace-cleanup");}
  using(var trace=new HostExitTrace(Path.Combine(args[0],"overflow.trace"))){for(int i=0;i<HostExitTrace.Capacity+3;i++)trace.Mark(HostExitPhase.Main,HostExitEvent.Begin);}
  using(var trace=new HostExitTrace(Path.Combine(args[0],"missing","no.trace"))){int actions=0;trace.Run(HostExitPhase.RuntimeClose,()=>actions++);Need(trace.DiagnosticFailed&&actions==1,"trace-open-failure-cannot-skip-action");}
  Console.WriteLine("PASS bounded exit trace, delay, original fault, failed diagnostics and overflow");return 0;
 }catch(Exception e){Console.Error.WriteLine(e);return 1;}}
}
