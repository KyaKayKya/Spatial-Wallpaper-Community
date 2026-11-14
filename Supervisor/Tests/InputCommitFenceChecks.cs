using System;using System.Reflection;using System.Threading;using System.Threading.Tasks;
static class InputCommitFenceChecks {
 static void Need(bool value,string name){if(!value)throw new Exception(name);Console.WriteLine("PASS "+name);}
 static int Main(){try{var type=typeof(InputCommitFenceChecks).Assembly.GetType("SpatialWallpaper.Supervision.Interactive.InputCommitFence");Need(type!=null,"input-commit-fence-available");dynamic fence=Activator.CreateInstance(type);int sent=0,revoked=0;
  long first=fence.Capture();Need(first>0&&fence.TryCommit(first,(Action)(()=>sent++)),"current-token-can-commit");
  using(var querying=new ManualResetEventSlim())using(var finish=new ManualResetEventSlim()){
   var late=Task.Run(()=>{long token=fence.Capture();querying.Set();finish.Wait();return (bool)fence.TryCommit(token,(Action)(()=>sent++));});
   Need(querying.Wait(3000),"query-captured-before-invalidation");try{fence.Invalidate((Action)(()=>revoked++),false);}finally{finish.Set();}
   Need(!late.GetAwaiter().GetResult()&&sent==1&&revoked==1,"ui-invalidation-rejects-in-flight-result-without-waiting-for-query");
  }
  long second=fence.Capture();Need(second>first&&!fence.TryCommit(first,(Action)(()=>sent++)),"old-generation-cannot-revive");
  bool threw=false;try{fence.Invalidate((Action)(()=>{throw new InvalidOperationException("revoke-failure");}),false);}catch(InvalidOperationException e){threw=e.Message=="revoke-failure";}
  Need(threw&&!fence.TryCommit(second,(Action)(()=>sent++)),"revocation-error-does-not-restore-old-token");
  long last=fence.Capture();fence.Invalidate((Action)(()=>revoked++),true);Need(fence.Capture()==0&&!fence.TryCommit(last,(Action)(()=>sent++))&&!fence.TryCommit(0L,(Action)(()=>sent++)),"closed-owner-rejects-old-and-new-commit");
  Need(sent==1,"no-late-input-was-dispatched");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
