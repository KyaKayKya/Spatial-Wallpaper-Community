using System;using System.IO;using SpatialWallpaper.Supervision;
class FaultJournalChecks {
 static void Assert(bool yes,string name){if(!yes)throw new Exception(name);}
 static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("operation-not-rejected");}
 static void Ready(FaultJournal journal,AttemptReceipt receipt){journal.RecordProcess(receipt,12345);Assert(journal.MarkRunning(receipt),"ready-after-process-binding");}
 static string Digest(char value)=>new string(value,64);
 static int Main(string[] args){
  try{
   string directory=args[0];Directory.CreateDirectory(directory);Guid installation=Guid.NewGuid(),instance=Guid.NewGuid();AttemptReceipt first;
   using(var journal=new FaultJournal(directory)){
    first=journal.Begin(installation,Digest('a'),instance,1,StartIntent.Manual);Assert(journal.ActiveCount==1,"start-not-persisted");
    Assert(!journal.MarkRunning(first),"ready-before-process-binding");journal.RecordProcess(first,12345);Assert(journal.MarkRunning(first),"first-frame-not-recorded");
    // Dispose simulates losing the Supervisor; the active attempt is deliberately unfinished.
   }
   using(var journal=new FaultJournal(directory)){
    Assert(journal.IsQuarantined(installation,Digest('a'))&&journal.ActiveCount==0,"unclean-start-was-not-quarantined");
    Reject(()=>journal.Begin(installation,Digest('a'),instance,2,StartIntent.Resume));
    var retry=journal.Begin(installation,Digest('a'),instance,2,StartIntent.Manual);Assert(journal.IsQuarantined(installation,Digest('a')),"manual-start-cleared-fault-before-first-frame");
    Ready(journal,retry);Assert(!journal.IsQuarantined(installation,Digest('a')),"healthy-manual-retry-not-recognized");
    Reject(()=>journal.Complete(retry));journal.MarkStopping(retry);journal.Complete(retry);
    Assert(!journal.Fail(retry,FaultReason.ProcessExit),"stale-exit-quarantined-new-session");
    var failed=journal.Begin(installation,Digest('b'),instance,3,StartIntent.Manual);journal.Fail(failed,FaultReason.FrameTimeout);Assert(journal.RecordDiagnostic(failed,"bounded diagnostic"),"diagnostic-not-stored");var diagnostic=journal.Diagnostic(failed.Attempt);Assert((string)diagnostic["diagnostic"]=="bounded diagnostic"&&(string)diagnostic["instance"]==instance.ToString("N")&&(long)diagnostic["generation"]==3,"diagnostic-identity-lost");Assert(!journal.RecordDiagnostic(failed,"overwrite"),"terminal-diagnostic-overwritten");
    var ticket=journal.ReserveRollback(installation,Digest('b'),Digest('a'));Assert(ticket!=null,"healthy-rollback-not-reserved");
    Assert(journal.ReserveRollback(installation,Digest('b'),Digest('a'))==null,"rollback-reserved-twice");
    var rollback=journal.BeginRollback(ticket,instance,4);Reject(()=>journal.BeginRollback(ticket,instance,5));journal.Fail(rollback,FaultReason.ProcessExit);
    Assert(journal.IsQuarantined(installation,Digest('a'))&&journal.IsQuarantined(installation,Digest('b')),"double-failure-not-quarantined");
    Assert(journal.ReserveRollback(installation,Digest('a'),Digest('c'))==null,"rollback-chain-looped");
   }
   Console.WriteLine("PASS durable startup, unclean recovery, manual retry, stale events and one rollback");
   // A later success from a previously-started second screen must not clear a newer fault.
   using(var journal=new FaultJournal(Path.Combine(directory,"two-screens"))){
    var one=journal.Begin(installation,Digest('c'),Guid.NewGuid(),1,StartIntent.Manual);var two=journal.Begin(installation,Digest('c'),Guid.NewGuid(),1,StartIntent.Manual);
    journal.Fail(one,FaultReason.Budget);Ready(journal,two);Assert(journal.IsQuarantined(installation,Digest('c')),"older-other-screen-success-cleared-new-fault");
    journal.MarkStopping(two);journal.Complete(two);
   }
   string failureDirectory=Path.Combine(directory,"write-failure");
   using(var journal=new FaultJournal(failureDirectory)){
    var healthy=journal.Begin(installation,Digest('d'),instance,1,StartIntent.Manual);Ready(journal,healthy);journal.MarkStopping(healthy);journal.Complete(healthy);
    byte[] before=File.ReadAllBytes(Path.Combine(failureDirectory,"state.json"));
    FaultJournal.BeforeReplace=()=>{throw new IOException("fixture-write-failure");};
    bool failed=false;try{journal.Begin(installation,Digest('e'),instance,2,StartIntent.Manual);}catch(IOException){failed=true;}finally{FaultJournal.BeforeReplace=null;}
    Assert(failed&&Convert.ToBase64String(before)==Convert.ToBase64String(File.ReadAllBytes(Path.Combine(failureDirectory,"state.json"))),"failed-write-damaged-old-state");
    Reject(()=>journal.Begin(installation,Digest('f'),instance,3,StartIntent.Manual));
   }
   using(var journal=new FaultJournal(failureDirectory))Assert(journal.ActiveCount==0&&!journal.IsQuarantined(installation,Digest('e')),"failed-start-created-active-worker-record");
   string corrupt=Path.Combine(directory,"corrupt");Directory.CreateDirectory(corrupt);File.WriteAllText(Path.Combine(corrupt,"state.json"),"{bad");
   bool corruptionRejected=false;try{using(var journal=new FaultJournal(corrupt)){} }catch(Exception error)when(error is Newtonsoft.Json.JsonException||error is InvalidDataException){corruptionRejected=true;}
   Assert(corruptionRejected,"corrupt-journal-enabled-auto-start");
   using(var journal=new FaultJournal(Path.Combine(directory,"lock"))){bool locked=false;try{using(var other=new FaultJournal(Path.Combine(directory,"lock"))){} }catch(IOException){locked=true;}Assert(locked,"two-supervisors-owned-same-journal");}
   Console.WriteLine("PASS cross-screen fault ordering and write-failure preservation");return 0;
  }catch(Exception error){Console.Error.WriteLine(error);return 1;}
 }
}
