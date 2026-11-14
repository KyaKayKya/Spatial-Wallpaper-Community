using System;using System.Reflection;using System.Threading;using System.Threading.Tasks;
static class InputHeartbeatChecks {
 static void Need(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static int Main(){try{
  var open=typeof(InputHeartbeatChecks).Assembly.GetType("SpatialWallpaper.Supervision.Interactive.InputHeartbeat`1");Need(open!=null,"bounded-heartbeat-coordinator-present");
  dynamic sample=Activator.CreateInstance(open.MakeGenericType(typeof(string)));var release=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);int calls=0;
  Need(sample.Begin((Func<string>)(()=>{Interlocked.Increment(ref calls);return release.Task.GetAwaiter().GetResult();})),"first-query-started");
  sample.Invalidate();Need(!sample.Begin((Func<string>)(()=>"overlap")),"invalidated-query-still-owns-single-slot");release.SetResult("old-held-state");Need(((Task)sample.Completion).Wait(1000),"old-query-actually-finished");string value;
  Need(!sample.TryTake(out value)&&value==null&&calls==1,"event-invalidated-heartbeat-never-publishes");
  Need(sample.Begin((Func<string>)(()=>"unchanged-held-state")),"next-query-after-actual-completion");Need(((Task)sample.Completion).Wait(1000),"new-query-complete");Need(sample.TryTake(out value)&&value=="unchanged-held-state"&&!sample.TryTake(out value),"one-result-consumed-once");
  var late=new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);Need(sample.Begin((Func<string>)(()=>late.Task.GetAwaiter().GetResult())),"late-query-started");sample.Close();Need(!((Task)sample.Completion).IsCompleted,"close-retains-inflight-task");late.SetResult("late-success");Need(((Task)sample.Completion).Wait(1000),"late-query-reclaimed");Need(!sample.TryTake(out value)&&value==null,"closed-owner-never-publishes-late-success");Need(!sample.Begin((Func<string>)(()=>"restart")),"closed-owner-cannot-restart");
  dynamic failed=Activator.CreateInstance(open.MakeGenericType(typeof(string)));Need(failed.Begin((Func<string>)(()=>{throw new InvalidOperationException("sample-failure");})),"fault-query-started");failed.Invalidate();try{((Task)failed.Completion).GetAwaiter().GetResult();}catch(InvalidOperationException){}bool observed=false;try{failed.TryTake(out value);}catch(InvalidOperationException error){observed=error.Message=="sample-failure";}Need(observed,"obsolete-query-does-not-hide-failure");failed.Close();
  return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
