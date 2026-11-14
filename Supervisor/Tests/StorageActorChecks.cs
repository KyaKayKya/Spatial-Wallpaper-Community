using System;using System.IO;using System.Threading;using System.Collections.Generic;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Broker;
static class StorageActorChecks {
 static void Need(bool value,string code){if(!value)throw new Exception(code);}
 static void Wait(Func<bool> value){var time=System.Diagnostics.Stopwatch.StartNew();while(!value()){if(time.ElapsedMilliseconds>5000)throw new Exception("wait-timeout");Thread.Sleep(2);}}
 static Dictionary<string,byte[]> Data(byte value)=>new Dictionary<string,byte[]>{{"save",new[]{value}}};
 static System.Threading.Tasks.Task<StorageResult> Uninstall(StorageBroker actor,Guid installation){var method=typeof(StorageBroker).GetMethod("Uninstall",new[]{typeof(Guid)});Need(method!=null,"missing-production-storage-uninstall");return (System.Threading.Tasks.Task<StorageResult>)method.Invoke(actor,new object[]{installation});}
 static int Main(string[] args){try{
  string root=args[0];Directory.CreateDirectory(root);using(var journal=new FaultJournal(Path.Combine(root,"journal"))){
   Guid install=Guid.NewGuid();Func<BrokerSession> create=()=>new BrokerSession(journal.Begin(install,new string('a',64),Guid.NewGuid(),1,StartIntent.Manual),false,new[]{"private-storage"});
   var one=create();var two=create();var actor=new StorageBroker(Path.Combine(root,"store"),2);actor.Initialized.GetAwaiter().GetResult();
   try{
    var written=actor.Commit(one,0,Data(42)).GetAwaiter().GetResult();Need(written.Failure==StorageFailure.None&&written.Version==1,"actor-write");
    var read=actor.Read(two,"save").GetAwaiter().GetResult();Need(read.Value[0]==42&&read.Version==1,"actor-shared-read");
    Need(actor.Commit(two,0,Data(7)).GetAwaiter().GetResult().Failure==StorageFailure.Conflict,"actor-CAS");
    Guid removed=Guid.NewGuid();
    var live=new BrokerSession(journal.Begin(removed,new string('b',64),Guid.NewGuid(),1,StartIntent.Manual),false,new[]{"private-storage"});
    var preview=new BrokerSession(journal.Begin(removed,new string('b',64),Guid.NewGuid(),1,StartIntent.Manual),true,new[]{"private-storage"});
    Need(actor.Commit(live,0,Data(1)).Result.Failure==StorageFailure.None&&actor.Commit(preview,0,Data(2)).Result.Failure==StorageFailure.None,"uninstall-fixture");live.Close();preview.Close();
    Need(Uninstall(actor,Guid.Empty).Result.Failure==StorageFailure.Invalid,"uninstall-empty-identity");
    string liveDirectory=Path.Combine(root,"store","live-"+removed.ToString("N")),previewDirectory=Path.Combine(root,"store","preview-"+removed.ToString("N"));
    using(var held=new FileStream(Path.Combine(liveDirectory,"state.bin"),FileMode.Open,FileAccess.Read,FileShare.Read))Need(Uninstall(actor,removed).Result.Failure==StorageFailure.Io,"uninstall-lock-not-reported");
    Need(Uninstall(actor,removed).Result.Failure==StorageFailure.None&&!Directory.Exists(liveDirectory)&&!Directory.Exists(previewDirectory),"uninstall-live-preview");
    Need(Uninstall(actor,removed).Result.Failure==StorageFailure.None&&actor.Read(one,"save").Result.Value[0]==42&&!actor.Completion.IsCompleted,"uninstall-idempotence-neighbor-or-service");
    string stateFile=Path.Combine(root,"store","live-"+install.ToString("N"),"state.bin");byte[] intact=File.ReadAllBytes(stateFile);var corrupt=(byte[])intact.Clone();corrupt[corrupt.Length-1]^=1;File.WriteAllBytes(stateFile,corrupt);var invalid=actor.Read(one,"save");Need(invalid.Wait(1000)&&invalid.Result.Failure==StorageFailure.Io&&!actor.Completion.IsCompleted,"corrupt-save-stranded-task-or-killed-service");File.WriteAllBytes(stateFile,intact);
    using(var entered=new ManualResetEventSlim())using(var release=new ManualResetEventSlim()){
     PrivateStore.BeforeReplace=()=>{entered.Set();release.Wait();};var first=actor.Commit(one,1,Data(50));Need(entered.Wait(5000),"disk-gate-not-entered");var queued=actor.Commit(two,1,Data(60));
     var watch=System.Diagnostics.Stopwatch.StartNew();var busy=actor.Read(two,"save");Need(busy.IsCompleted&&busy.Result.Failure==StorageFailure.Busy&&watch.ElapsedMilliseconds<100,"queue-full-blocked-caller");
     Need(Uninstall(actor,removed).Result.Failure==StorageFailure.Busy,"uninstall-bypassed-capacity");
     one.Close();two.Close();watch.Restart();actor.Close();Need(watch.ElapsedMilliseconds<100&&!actor.Completion.IsCompleted,"close-blocked-or-falsely-completed");release.Set();
     Need(first.GetAwaiter().GetResult().Failure==StorageFailure.Closed&&queued.GetAwaiter().GetResult().Failure==StorageFailure.Closed,"closed-session-executed-queued-write");actor.Completion.GetAwaiter().GetResult();PrivateStore.BeforeReplace=null;
     Need(Uninstall(actor,removed).Result.Failure==StorageFailure.Closed,"uninstall-after-close");
    }
   }finally{PrivateStore.BeforeReplace=null;actor.Close();actor.Completion.GetAwaiter().GetResult();}
   using(var reopened=new PrivateStore(Path.Combine(root,"store")))Need(reopened.Read(create()).Get("save")[0]==42,"cancelled-actor-replaced-old-data");
   string large=Path.Combine(root,"large");var session=create();using(var store=new PrivateStore(large))store.Commit(session,0,new Dictionary<string,byte[]>{{"x",new byte[PrivateStore.QuotaBytes-1]}});
   var bounded=new StorageBroker(large,2);bounded.Initialized.GetAwaiter().GetResult();
   try{Need(bounded.Read(session,"x").GetAwaiter().GetResult().Failure==StorageFailure.TooLarge,"oversized-reply-not-rejected");Need(bounded.Commit(session,1,new Dictionary<string,byte[]>{{"y",new byte[1]}}).GetAwaiter().GetResult().Failure==StorageFailure.Quota,"actor-quota");}finally{bounded.Close();bounded.Completion.GetAwaiter().GetResult();}
   var fatal=new StorageBroker(Path.Combine(root,"fatal"),2);fatal.Initialized.GetAwaiter().GetResult();PrivateStore.BeforeReplace=()=>{throw new NotSupportedException("unexpected fixture failure");};try{var failed=fatal.Commit(create(),0,Data(1));Need(failed.Wait(1000)&&failed.Result.Failure==StorageFailure.Io,"fatal-job-left-pending");Wait(()=>fatal.Completion.IsCompleted);Need(fatal.Completion.IsFaulted,"unexpected-failure-not-visible");var observed=fatal.Completion.Exception;}finally{PrivateStore.BeforeReplace=null;fatal.Close();}
  }
  Console.WriteLine("PASS bounded storage actor, async backpressure, session cancellation, old-data preservation and reply limits");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
