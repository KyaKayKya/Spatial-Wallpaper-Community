using System;using System.IO;using System.Threading;using System.Threading.Tasks;using SpatialWallpaper.Supervision;
static class JournalActorChecks{
 static T Await<T>(Task<T> task){if(!task.Wait(5000))throw new Exception("actor-timeout");return task.GetAwaiter().GetResult();}
 static bool Rejected<T>(Task<T> task){try{Await(task);return false;}catch(AggregateException){return true;}catch(InvalidOperationException){return true;}}
 static int Main(string[] args){try{
  Guid install=Guid.NewGuid();string build=new string('a',64);var entered=new ManualResetEvent(false);var release=new ManualResetEvent(false);int writes=0,actorThread=0;int caller=Thread.CurrentThread.ManagedThreadId;
  FaultJournal.BeforeReplace=()=>{actorThread=Thread.CurrentThread.ManagedThreadId;if(Interlocked.Increment(ref writes)==1){entered.Set();if(!release.WaitOne(5000))throw new Exception("test-write-release-timeout");}};
  var actor=new JournalActor(Path.Combine(args[0],"bounded"),2);
  var start=actor.Begin(install,build,Guid.NewGuid(),1,StartIntent.Manual);
  if(!entered.WaitOne(5000))throw new Exception("test-writer-not-entered");
  var first=actor.IsQuarantined(install,build);var second=actor.IsQuarantined(install,build);var overflow=actor.IsQuarantined(install,build);
  if(!overflow.IsFaulted||start.IsCompleted||first.IsCompleted||actorThread==caller)throw new Exception("persistence-lane-not-independent-or-bounded");
  release.Set();var receipt=Await(start);if(Await(first)||Await(second))throw new Exception("unexpected-quarantine");
  Await(actor.RecordProcess(receipt,1234));Await(actor.Fail(receipt,FaultReason.ScriptFault));
  if(!Rejected(actor.Begin(install,build,Guid.NewGuid(),2,StartIntent.Resume)))throw new Exception("blocked-resume-accepted");
  if(!Await(actor.IsQuarantined(install,build)))throw new Exception("logical-rejection-poisoned-persistence-actor");
  actor.Close();if(!actor.Completion.Wait(5000)||!Rejected(actor.IsQuarantined(install,build)))throw new Exception("actor-close");
  FaultJournal.BeforeReplace=()=>{throw new IOException("synthetic-durable-write-failure");};
  var failed=new JournalActor(Path.Combine(args[0],"failed"),2);if(!Rejected(failed.Begin(install,build,Guid.NewGuid(),1,StartIntent.Manual)))throw new Exception("write-failure-hidden");
  try{failed.Completion.Wait(5000);}catch(AggregateException){}
  if(!failed.Completion.IsFaulted||!Rejected(failed.IsQuarantined(install,build)))throw new Exception("failed-journal-still-admits-work");
  FaultJournal.BeforeReplace=null;Console.WriteLine("PASS independent bounded persistence, saturation rejection, logical rejection, durable failure and close");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
