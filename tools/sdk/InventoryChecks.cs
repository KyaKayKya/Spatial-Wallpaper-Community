using System;using System.Collections.Generic;using System.Threading;using SpatialWallpaper.Sdk.Execution;
static class InventoryChecks {
 sealed class Fake {public bool Destroyed;}
 sealed class Retirement:IRetirementQueue {
  public void RequireCapacity(int entries){}
  public readonly List<object> Retained=new List<object>();
  public readonly List<ResourceLease> Pending=new List<ResourceLease>();
  public void Retire(object native,ResourceLease lease,Action<object> destroy,object retained=null){if(native!=null)destroy(native);Pending.Add(lease);Retained.Add(retained);}
  public void Complete(){foreach(var lease in Pending)lease.Dispose();Pending.Clear();}
 }
 static void Assert(bool yes,string name){if(!yes)throw new Exception(name);}
 static void Reject(Action action){try{action();}catch(InvalidOperationException){return;}throw new Exception("inventory-operation-not-rejected");}
 static void Destroy(object native){((Fake)native).Destroyed=true;}
 [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
 static void BindManagedLayout(ResourceLedger ledger,object owner){new ManagedReservation(ledger,4096,1).Bind(owner);}
 internal static void Run(){
  using(var ledger=new ResourceLedger(new ResourceCost(1048576,1000,1000,128)))using(var domain=new OwnedDomain(Guid.NewGuid(),1,ledger)){
   var retire=new Retirement();using(var inventory=new ResourceInventory(domain,retire,Destroy)){
    int created=0;var handle=inventory.Create("mesh",new ResourceCost(64,100,100,1),()=>{created++;return new Fake();});
    var original=(Fake)inventory.Native(handle,"mesh");long nativeBefore=ledger.Used.NativeBytes;
    using(var other=new ResourceInventory(domain,retire,Destroy)){Reject(()=>other.Native(handle,"mesh"));Reject(()=>other.Release(handle));}
    using(var foreignDomain=new OwnedDomain(Guid.NewGuid(),1))using(var foreign=new ResourceInventory(foreignDomain,retire,Destroy))Reject(()=>foreign.Native(handle,"mesh"));
    Reject(()=>inventory.Replace(handle,new ResourceCost(64,901,100,1),new ResourceCost(0,100,100,0),old=>{created++;return new Fake();},(old,value)=>{}));
    Assert(created==1&&ReferenceEquals(original,inventory.Native(handle,"mesh"))&&!original.Destroyed&&ledger.Used.NativeBytes==nativeBefore,"reserve-failure-mutated-old-native");
    inventory.Replace(handle,new ResourceCost(64,150,150,1),new ResourceCost(0,100,100,0),old=>{created++;return new Fake();},(old,value)=>{});
    Assert(original.Destroyed&&!ReferenceEquals(original,inventory.Native(handle,"mesh"))&&ledger.Used.NativeBytes==350,"old-allocation-refunded-before-retirement");
    retire.Complete();Assert(ledger.Used.NativeBytes==150,"retirement-did-not-refund");
    var stable=inventory.Native(handle,"mesh");
    Reject(()=>inventory.Replace(handle,new ResourceCost(64,100,100,1),default,old=>new Fake(),(old,value)=>{if(ReferenceEquals(old,stable))throw new InvalidOperationException("fixture-rebind-failure");}));
    Assert(ReferenceEquals(stable,inventory.Native(handle,"mesh")),"failed-rebind-replaced-old-slot");retire.Complete();
    inventory.Release(handle);Reject(()=>inventory.Native(handle,"mesh"));Assert(ledger.Used.NativeBytes==150,"release-refunded-before-safe-frame");long beforeNativeRetirement=ledger.Used.ManagedBytes;
    retire.Complete();Assert(ledger.Used.NativeBytes==0,"released-native-not-reclaimed");
    Assert(ledger.Used.ManagedBytes==beforeNativeRetirement,"native-retirement-refunded-managed-memory-before-GC");
   }
  }
  using(var ledger=new ResourceLedger(new ResourceCost(65536,1000,1000,1)))using(var domain=new OwnedDomain(Guid.NewGuid(),1,ledger)){
   var retire=new Retirement();using(var inventory=new ResourceInventory(domain,retire,Destroy)){
    int called=0;Reject(()=>inventory.Create("mesh",new ResourceCost(64,100,100,1),()=>{called++;return new Fake();}));
    Assert(called==0&&ledger.Used.NativeBytes==0,"handle-registration-budget-failed-after-native-allocation");
   }
  }
  using(var ledger=new ResourceLedger(new ResourceCost(1048576,1000,1000,128)))using(var domain=new OwnedDomain(Guid.NewGuid(),1,ledger)){
   var retire=new Retirement();using(var inventory=new ResourceInventory(domain,retire,Destroy)){
    var partial=new Fake();Reject(()=>inventory.Create("mesh",new ResourceCost(64,100,100,1),born=>{born(partial);throw new InvalidOperationException("fixture-after-native-construction");}));
    Assert(partial.Destroyed&&ledger.Used.NativeBytes==100,"partial-native-creation-not-retained-until-destruction");retire.Complete();Assert(ledger.Used.NativeBytes==0,"tracked-partial-native-not-retired");
    var handle=inventory.Create("mesh",new ResourceCost(64,100,100,1),()=>new Fake());var old=(Fake)inventory.Native(handle,"mesh");var replacement=new Fake();
    Reject(()=>inventory.Replace(handle,new ResourceCost(64,100,100,1),default,(previous,born)=>{born(replacement);throw new InvalidOperationException("fixture-partial-replacement");},(previous,value)=>{throw new Exception("rebind-before-factory-completed");}));
    Assert(replacement.Destroyed&&!old.Destroyed&&ReferenceEquals(old,inventory.Native(handle,"mesh")),"partial-replacement-damaged-old-resource");retire.Complete();
    using(var other=new ResourceInventory(domain,retire,Destroy))Reject(()=>other.Create("mesh",new ResourceCost(64,100,100,1),()=>old));
    Assert(!old.Destroyed&&ReferenceEquals(old,inventory.Native(handle,"mesh")),"duplicate-native-wrapper-destroyed-other-owner");
    long held=ledger.Used.NativeBytes;retire.Complete();Assert(ledger.Used.NativeBytes==held,"unknown-native-failure-refunded-by-GPU-progress");
   }
  }
  using(var ledger=new ResourceLedger(new ResourceCost(1048576,1000,1000,16))){
   var queue=new RenderRetirementQueue(ledger,16,value=>((Fake)value).Destroyed);
   var native=new Fake();var lease=ledger.Reserve(new ResourceCost(64,100,100,1));lease.Commit();
   queue.Retire(native,lease,value=>{});
   queue.AdvanceCompletedRender(2);Assert(ledger.Used.NativeBytes==100,"GPU-progress-before-native-destruction-refunded");
   native.Destroyed=true;queue.AdvanceCompletedRender(2);Assert(ledger.Used.NativeBytes==100,"render-boundaries-before-observed-destruction-refunded");
   queue.AdvanceCompletedRender(3);Assert(ledger.Used.NativeBytes==100,"only-one-boundary-after-destruction-refunded");
   queue.AdvanceCompletedRender(4);Assert(ledger.Used.NativeBytes==0,"two-boundaries-after-destruction-not-reclaimed");
   var second=ledger.Reserve(new ResourceCost(64,100,100,1));second.Commit();queue.Retire(new Fake{Destroyed=true},second,value=>{});
   queue.AdvanceCompletedRender(5);Assert(ledger.Used.NativeBytes==100,"single-render-boundary-refunded");
   Reject(()=>queue.AdvanceCompletedRender(1));queue.CloseWhenDrained();
   queue.AdvanceCompletedRender(6);Assert(queue.Closed&&queue.Pending==0&&ledger.Used.ManagedBytes==0,"drained-queue-retained-reservation");
   Reject(()=>queue.Retire(null,second,value=>{}));
  }
  using(var ledger=new ResourceLedger(new ResourceCost(1048576,1000,1000,16))){
   var queue=new RenderRetirementQueue(ledger,16,value=>((Fake)value).Destroyed);
   queue.AdvanceSubmittedRender(1);queue.AdvanceSubmittedRender(2);queue.AdvanceSubmittedRender(3);
   var lease=ledger.Reserve(new ResourceCost(64,100,100,1));lease.Commit();
   queue.Retire(new Fake{Destroyed=true},lease,value=>{});
   queue.AdvanceCompletedRender(2);queue.AdvanceCompletedRender(3);
   Assert(ledger.Used.NativeBytes==100,"old-inflight-completions-refunded-new-destruction");
   queue.AdvanceSubmittedRender(4);queue.AdvanceCompletedRender(4);
   Assert(ledger.Used.NativeBytes==100,"only-one-post-destruction-submission-refunded");
   queue.AdvanceSubmittedRender(5);queue.AdvanceCompletedRender(5);
   Assert(ledger.Used.NativeBytes==0,"two-post-destruction-submissions-not-reclaimed");
   queue.CloseWhenDrained();
   var delayed=new RenderRetirementQueue(ledger,16,value=>((Fake)value).Destroyed);
   delayed.AdvanceSubmittedRender(1);delayed.AdvanceSubmittedRender(2);delayed.AdvanceSubmittedRender(3);
   var late=new Fake();var held=ledger.Reserve(new ResourceCost(64,100,100,1));held.Commit();delayed.Retire(late,held,value=>{});
   delayed.AdvanceSubmittedRender(4);delayed.AdvanceSubmittedRender(5);late.Destroyed=true;delayed.AdvanceCompletedRender(3);delayed.AdvanceCompletedRender(5);
   Assert(ledger.Used.NativeBytes==100,"delayed-destruction-used-completion-instead-of-submission-watermark");
   delayed.AdvanceSubmittedRender(6);delayed.AdvanceCompletedRender(6);Assert(ledger.Used.NativeBytes==100,"delayed-destruction-only-one-new-submission");
   delayed.AdvanceSubmittedRender(7);delayed.AdvanceCompletedRender(7);Assert(ledger.Used.NativeBytes==0,"delayed-destruction-two-new-submissions-not-reclaimed");delayed.CloseWhenDrained();
  }
  using(var ledger=new ResourceLedger(new ResourceCost(65536,0,0,4))){
   var ready=new ManualResetEvent(false);var release=new ManualResetEvent(false);Exception failure=null;
   var producer=new Thread(()=>{try{var owner=new object[4];BindManagedLayout(ledger,owner);ready.Set();release.WaitOne();GC.KeepAlive(owner);}catch(Exception error){failure=error;ready.Set();}});
   producer.Start();Assert(ready.WaitOne(5000),"managed-layout-owner-timeout");
   for(int i=0;i<3;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
   bool held=ledger.Used.ManagedBytes==4096&&ledger.Used.Objects==1;release.Set();Assert(producer.Join(5000),"managed-layout-owner-still-running");if(failure!=null)throw failure;
   Assert(held,"managed-layout-reservation-collected-before-owner");
   for(int i=0;i<3;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
   Assert(ledger.Used.ManagedBytes==0&&ledger.Used.Objects==0,"unreachable-managed-layout-not-refunded");
  }
  using(var ledger=new ResourceLedger(new ResourceCost(2*1048576,1000,1000,4))){
   var ready=new ManualResetEvent(false);var releaseOwner=new ManualResetEvent(false);Exception failure=null;
   var producer=new Thread(()=>{
    try{
     using(var domain=new OwnedDomain(Guid.NewGuid(),1,ledger)){
      var retire=new Retirement();using(var inventory=new ResourceInventory(domain,retire,Destroy)){
       var native=new Fake();var handle=inventory.Create("mesh",new ResourceCost(1048576,100,100,1),()=>native);
       inventory.Release(handle);retire.Complete();retire.Retained.Clear();ready.Set();releaseOwner.WaitOne();GC.KeepAlive(native);
      }
     }
    }catch(Exception error){failure=error;ready.Set();}
   });
   producer.Start();Assert(ready.WaitOne(5000),"managed-native-owner-probe-timeout");
   for(int i=0;i<3;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
   bool bodyHeld=ledger.Used.ManagedBytes>=1048576;releaseOwner.Set();Assert(producer.Join(5000),"managed-native-owner-probe-did-not-stop");
   if(failure!=null)throw failure;Assert(bodyHeld,"live-native-wrapper-lost-managed-charge-after-GC");
   for(int i=0;i<5;i++){GC.Collect();GC.WaitForPendingFinalizers();GC.Collect();}
   Assert(ledger.Used.ManagedBytes==0,"unreachable-native-wrapper-kept-managed-charge");
  }
  Console.WriteLine("PASS native allocation reservation, atomic replacement, failed rebind and delayed retirement");
 }
}
