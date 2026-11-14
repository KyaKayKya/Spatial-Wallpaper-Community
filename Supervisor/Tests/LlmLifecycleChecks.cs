using System;
using System.Threading.Tasks;
using SpatialWallpaper.Supervision;
using SpatialWallpaper.Supervision.Broker;
static class LlmLifecycleChecks {
 static void Need(bool value,string reason){if(!value)throw new Exception(reason);Console.WriteLine("PASS "+reason);}
 public static void Run(string origin,FaultJournal journal){
  foreach(string mode in new[]{"cancel","close","dispose","timeout","after-terminal"})Boundary(origin,journal,mode);
 }
 static void Boundary(string origin,FaultJournal journal,string mode){
  var session=new BrokerSession(journal.Begin(Guid.NewGuid(),new string('a',64),Guid.NewGuid(),1,StartIntent.Manual),false,new[]{"llm-proxy"});
  var broker=new LlmBroker(new LlmConfiguration(new Uri(origin+"/ok"),"fixed-model","test-secret-never-in-result",mode=="timeout"?300:5000));
  var arrived=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);var release=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  Action barrier=()=>{arrived.TrySetResult(true);if(!release.Task.Wait(3000))throw new Exception("result-barrier-release-missing");};
  if(mode=="after-terminal")LlmBroker.AfterResult=barrier;else LlmBroker.BeforeResult=barrier;
  Task<LlmResult> pending=null;
  try{
   pending=broker.Submit(session,1,"fixed terminal race");Need(arrived.Task.Wait(3000),"reply-parsed-boundary-"+mode);
   LlmFailure expected;
   if(mode=="after-terminal"){Need(!broker.Cancel(session,1),"cancel-rejected-after-terminal-commit");expected=LlmFailure.None;}
   else if(mode=="close"){session.Close();expected=LlmFailure.Closed;}
   else if(mode=="dispose"){broker.Dispose();broker.Dispose();Need(!broker.Completion.IsCompleted,"dispose-waits-for-running-request");expected=LlmFailure.Closed;}
   else if(mode=="timeout"){System.Threading.Thread.Sleep(350);Need(!broker.Cancel(session,1),"cancel-does-not-override-existing-timeout");expected=LlmFailure.Timeout;}
   else{Need(broker.Cancel(session,1),"cancel-accepted-before-terminal-commit");Need(broker.Cancel(session,1),"repeat-cancel-confirms-latched-cancellation");expected=LlmFailure.Cancelled;}
   release.TrySetResult(true);
   Need(pending.Wait(3000)&&pending.Result.Failure==expected&&(expected==LlmFailure.None?pending.Result.Text=="reply-as-data":pending.Result.Text==null),"terminal-result-"+mode);
   Need(!broker.Cancel(session,1),"completed-request-cannot-be-cancelled");
  }finally{release.TrySetResult(true);LlmBroker.BeforeResult=null;LlmBroker.AfterResult=null;broker.Dispose();Need(broker.Completion.Wait(3000),"broker-actual-io-completion-"+mode);}
 }
}
