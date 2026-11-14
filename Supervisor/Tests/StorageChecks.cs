using System;using System.IO;using System.Linq;using System.Collections.Generic;using System.Threading.Tasks;using SpatialWallpaper.Supervision;using SpatialWallpaper.Supervision.Broker;
static class StorageChecks {
 static void Require(bool ok,string code){if(!ok)throw new Exception(code);}
 static void Reject<T>(Action body)where T:Exception{try{body();}catch(T){return;}throw new Exception("expected-"+typeof(T).Name);}
 static Dictionary<string,byte[]> Change(string key,byte[] value)=>new Dictionary<string,byte[]>{{key,value}};
 static int Main(string[] args){try{
   string root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);Guid install=Guid.NewGuid();
   if(args.Length==1)StorageAtomicChecks.Run(Path.Combine(root,"atomic"));
  if(args.Length==2){
   using(var crashJournal=new FaultJournal(Path.Combine(root,"crash-journal"))){var receipt=crashJournal.Begin(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),new string('a',64),Guid.NewGuid(),2,StartIntent.Manual);var active=new BrokerSession(receipt,false,new[]{"private-storage"});using(var storage=new PrivateStore(Path.Combine(root,"storage"))){
     if(args[1]=="crash"||args[1]=="crash-after"){
      Action stop=()=>{File.WriteAllText(Path.Combine(root,"ready-to-"+args[1]),args[1]);System.Threading.Thread.Sleep(System.Threading.Timeout.Infinite);};
      if(args[1]=="crash")PrivateStore.BeforeReplace=stop;else PrivateStore.AfterReplace=stop;
      storage.Commit(active,1,Change("../opaque-key",new byte[]{43}));throw new Exception("crash-fixture-returned");
     }
     bool after=args[1]=="recover-after";Require(storage.Read(active).Version==(after?2:1)&&storage.Read(active).Get("../opaque-key")[0]==(after?43:42),"interrupted-write-commit-point");Require(!Directory.GetFiles(Path.Combine(root,"storage"),"write-*.tmp",SearchOption.AllDirectories).Any(),"interrupted-temporary-not-reclaimed");
   }}Console.WriteLine("PASS real process interruption "+args[1]+" preserves complete committed state and reclaims owned temporary");return 0;
  }
  using(var journal=new FaultJournal(Path.Combine(root,"journal"))){
   Func<Guid,uint,bool,string[],BrokerSession> session=(id,gen,preview,caps)=>new BrokerSession(journal.Begin(id,new string('a',64),Guid.NewGuid(),gen,StartIntent.Manual),preview,caps);
   var one=session(install,1,false,new[]{"private-storage"});var two=session(install,2,false,new[]{"private-storage"});
   var foreign=session(Guid.NewGuid(),1,false,new[]{"private-storage"});var preview=session(install,1,true,new[]{"private-storage"});var denied=session(install,1,false,new string[0]);
   using(var store=new PrivateStore(Path.Combine(root,"storage"))){
    Require(PrivateStore.QuotaBytes==16777216,"quota-not-fixed-16MiB");Reject<IOException>(()=>{using(var other=new PrivateStore(Path.Combine(root,"storage"))){}});
    Reject<UnauthorizedAccessException>(()=>store.Read(denied));
    var value=new byte[]{1,2,3};Require(store.Commit(one,0,Change("save",value))==1,"initial-revision");value[0]=9;
    var snapshot=store.Read(two);Require(snapshot.Version==1&&snapshot.Get("save")[0]==1,"cross-version-screen-shared-or-input-mutated");
    var exposed=snapshot.Get("save");exposed[0]=9;Require(store.Read(one).Get("save")[0]==1,"read-not-defensive");
    Require(store.Read(foreign).Get("save")==null&&store.Read(preview).Get("save")==null,"namespace-leak");store.Commit(foreign,0,Change("private",new byte[]{99}));store.Commit(preview,0,Change("preview",new byte[]{77}));Require(store.Read(one).Get("preview")==null,"preview-wrote-real-namespace");
    long revision=store.Read(one).Version;int won=0,conflicts=0;
    Parallel.For(0,2,i=>{try{store.Commit(i==0?one:two,revision,Change("race",new[]{(byte)i}));System.Threading.Interlocked.Increment(ref won);}catch(StoreConflictException){System.Threading.Interlocked.Increment(ref conflicts);}});
    Require(won==1&&conflicts==1,"CAS-lost-update");Reject<StoreConflictException>(()=>store.Commit(one,revision,Change("race",new byte[]{8})));
    var boundary=session(Guid.NewGuid(),1,false,new[]{"private-storage"});
    store.Commit(boundary,0,Change("a",new byte[PrivateStore.QuotaBytes-1]));
    Require(store.Read(boundary).ChargedBytes==PrivateStore.QuotaBytes,"exact-byte-boundary-rejected");
    Reject<StoreQuotaException>(()=>store.Commit(boundary,1,Change("a",new byte[PrivateStore.QuotaBytes])));
    Require(store.Read(boundary).Version==1&&store.Read(boundary).Get("a").Length==PrivateStore.QuotaBytes-1,"quota-failure-lost-old");
    Reject<StoreQuotaException>(()=>store.Commit(boundary,1,Change("b",new byte[0])));
    var keys=session(Guid.NewGuid(),1,false,new[]{"private-storage"});var many=new Dictionary<string,byte[]>();for(int i=0;i<PrivateStore.MaxKeys;i++)many.Add("k"+i,new byte[0]);store.Commit(keys,0,many);
    Reject<StoreQuotaException>(()=>store.Commit(keys,1,Change("overflow",new byte[0])));
    Reject<ArgumentException>(()=>store.Commit(keys,1,Change(new string('界',86),new byte[0])));
    Reject<ArgumentException>(()=>store.Commit(keys,1,Change("\ud800",new byte[0])));
    long old=store.Read(one).Version;
    string target=Path.Combine(root,"storage","live-"+install.ToString("N"),"state.bin");byte[] intact=File.ReadAllBytes(target);
    using(var locked=new FileStream(target,FileMode.Open,FileAccess.Read,FileShare.Read))Reject<System.ComponentModel.Win32Exception>(()=>store.Commit(one,old,Change("save",new byte[]{6})));
    Require(File.ReadAllBytes(target).SequenceEqual(intact),"real-rename-failure-changed-old-bytes");
    var corrupt=(byte[])intact.Clone();corrupt[corrupt.Length-1]^=1;File.WriteAllBytes(target,corrupt);Reject<InvalidDataException>(()=>store.Read(one));Require(File.ReadAllBytes(target).SequenceEqual(corrupt),"corruption-silently-rewritten");File.WriteAllBytes(target,intact);
    PrivateStore.BeforeReplace=()=>{throw new IOException("injected-write-failure");};
    try{Reject<IOException>(()=>store.Commit(one,old,Change("save",new byte[]{7})));}finally{PrivateStore.BeforeReplace=null;}
    Require(store.Read(two).Version==old&&store.Read(two).Get("save")[0]==1,"failed-atomic-write-lost-old");
    PrivateStore.BeforeReplace=()=>one.Close();try{Reject<InvalidOperationException>(()=>store.Commit(one,old,Change("save",new byte[]{8})));}finally{PrivateStore.BeforeReplace=null;}
    Reject<InvalidOperationException>(()=>store.Read(one));Require(store.Read(two).Version==old,"closed-session-committed");
    preview.Close();two.Close();store.Uninstall(install);Require(store.Read(session(install,3,false,new[]{"private-storage"})).Get("save")==null,"uninstall-retained-data");
    Require(store.Read(foreign).Version==1&&store.Read(foreign).Get("private")[0]==99,"uninstall-other-installation");
    // Keep one committed identity for restart and interrupted-temp recovery.
    var durable=session(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),1,false,new[]{"private-storage"});store.Commit(durable,0,Change("../opaque-key",new byte[]{42}));durable.Close();
   }
   using(var reopened=new PrivateStore(Path.Combine(root,"storage"))){var check=session(Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa"),2,false,new[]{"private-storage"});Require(reopened.Read(check).Get("../opaque-key")[0]==42,"restart-lost-data");}
  }
  Console.WriteLine("PASS fixed quota/CAS/identity/preview/cancellation/atomic failure/restart/uninstall");return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
