using System;using System.Diagnostics;using System.Reflection;using System.Threading;using System.Threading.Tasks;
static class InputThreadChecks {
 static void Need(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static bool Done(Func<bool> condition){var clock=Stopwatch.StartNew();while(!condition()&&clock.ElapsedMilliseconds<3000)Thread.Sleep(1);return condition();}
 static int Main(){try{
  var type=typeof(InputThreadChecks).Assembly.GetType("SpatialWallpaper.Supervision.Interactive.OwnedInputThread");Need(type!=null,"input-owner-thread-available");
  int caller=Thread.CurrentThread.ManagedThreadId,owner=0,ticks=0,commands=0,cleanupThread=0;bool release=false;
  dynamic thread=Activator.CreateInstance(type,new object[]{(Action)(()=>owner=Thread.CurrentThread.ManagedThreadId),(Action)(()=>Interlocked.Increment(ref ticks)),(Func<bool>)(()=>{cleanupThread=Thread.CurrentThread.ManagedThreadId;return Volatile.Read(ref release);})});
  try{Need(((Task)thread.Started).Wait(3000),"owner-started");Need(owner!=caller,"owner-is-independent-thread");
   thread.Post((Action)(()=>{Need(Thread.CurrentThread.ManagedThreadId==owner,"command-on-owner");Interlocked.Increment(ref commands);}));Need(Done(()=>Volatile.Read(ref commands)==1&&Volatile.Read(ref ticks)>0),"owner-pumps-commands-and-ticks");
   thread.Dispose();Need(Done(()=>Volatile.Read(ref cleanupThread)!=0),"cleanup-attempted");Need(cleanupThread==owner,"cleanup-on-owner");Need(!((Task)thread.Completion).IsCompleted,"stop-retains-owner-until-release-obligations-clear");
   bool rejected=false;try{thread.Post((Action)(()=>commands++));}catch(ObjectDisposedException){rejected=true;}Need(rejected,"stopping-owner-rejects-new-work");
  }finally{Volatile.Write(ref release,true);thread.Dispose();Need(((Task)thread.Completion).Wait(3000),"owner-final-release-and-exit");}
  int failureCleanup=0;dynamic failed=Activator.CreateInstance(type,new object[]{(Action)(()=>{throw new InvalidOperationException("injected-start-failure");}),(Action)(()=>{}),(Func<bool>)(()=>{Interlocked.Increment(ref failureCleanup);return true;})});
  bool startFailed=false,completionFailed=false;
  try{((Task)failed.Started).GetAwaiter().GetResult();}catch(InvalidOperationException e){startFailed=e.Message=="injected-start-failure";}
  try{((Task)failed.Completion).GetAwaiter().GetResult();}catch(InvalidOperationException e){completionFailed=e.Message=="injected-start-failure";}
  Need(startFailed&&completionFailed&&failureCleanup==1,"start-failure-keeps-original-error-and-cleans-on-owner");failed.Dispose();
  int cleanupAttempts=0;long firstCleanup=0,secondCleanup=0;
  dynamic retry=Activator.CreateInstance(type,new object[]{(Action)(()=>{}),(Action)(()=>{}),(Func<bool>)(()=>{int attempt=++cleanupAttempts;if(attempt==1){firstCleanup=Stopwatch.GetTimestamp();throw new InvalidOperationException("injected-release-failure");}secondCleanup=Stopwatch.GetTimestamp();return true;})});
  Need(((Task)retry.Started).Wait(3000),"cleanup-retry-owner-started");retry.Dispose();bool retainedFailure=false;
  try{((Task)retry.Completion).GetAwaiter().GetResult();}catch(InvalidOperationException e){retainedFailure=e.Message=="injected-release-failure";}
  Need(retainedFailure&&cleanupAttempts==2,"cleanup-retry-retains-first-failure");Need((secondCleanup-firstCleanup)*1000.0/Stopwatch.Frequency>=5,"cleanup-failure-does-not-busy-spin");
  return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
