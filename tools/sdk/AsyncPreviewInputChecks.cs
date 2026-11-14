using System;
using System.Diagnostics;
using System.Threading;
using SpatialWallpaper.Sdk.Capabilities;
using SpatialWallpaper.Sdk.Hosting;

static class AsyncPreviewInputChecks {
 static int checks;
 static void Need(bool value,string message){if(!value)throw new Exception(message);checks++;Console.WriteLine("PASS "+message);}
 static void Until(Func<bool> condition,string message){Need(SpinWait.SpinUntil(condition,2000),message);}
 sealed class Transport:IPreviewInputTarget,IPreviewInputKeepAlive,IDisposable {
  internal readonly InputTarget Target;
  internal readonly ManualResetEventSlim Entered=new ManualResetEventSlim(),Release=new ManualResetEventSlim(true);
  internal bool Block,Fail;internal long LastTime;internal int Calls,KeepAlives;
  internal Transport(InputTarget target){Target=target;}
  public ulong CaptureEpoch=>Target.CaptureEpoch;
  public void Observe(InputObservation value){Entered.Set();if(Block)Release.Wait();if(Fail)throw new System.IO.IOException("injected-disconnect");LastTime=value.Time;Interlocked.Increment(ref Calls);Target.Observe(value);}
  public void Revoke()=>Target.Revoke();
  public void KeepAlive(){if(Fail)throw new System.IO.IOException("injected-idle-disconnect");Interlocked.Increment(ref KeepAlives);}
  public void Dispose(){Release.Set();Target.Revoke();}
 }
 static InputObservation Sample(AsyncPreviewInputTarget source,long sequence,long time,uint held,bool primary=false)=>new InputObservation(sequence,time,true,true,true,true,true,false,primary,held,.4f,.6f,0,source.CaptureEpoch);
 static int Main(){try{
  long now=1000;
  using(var broker=new InputBroker(()=>Interlocked.Read(ref now)))using(var target=broker.Register(new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),false,true)){
   var transport=new Transport(target);
   using(var source=new AsyncPreviewInputTarget(()=>transport,()=>Interlocked.Read(ref now))){
    Until(()=>source.Ready,"background transport initialized");
    Until(()=>Volatile.Read(ref transport.KeepAlives)>=5,"idle-background-transport-remains-live-without-input-source");
    Need(transport.Calls==0&&!source.Authority.Focused&&target.OrderedSnapshot(0).Events.Length==0,"transport-keepalive-cannot-acquire-authority-or-create-events");
    transport.Block=true;transport.Release.Reset();
    var before=Stopwatch.StartNew();source.Observe(Sample(source,1,1000,1,true));
    Need(before.ElapsedMilliseconds<50,"native capture returns without waiting for blocked transport");
    Until(()=>transport.Entered.IsSet,"first observation reached blocked transport");
    Need(source.TransportPending,"blocked-transport-has-real-outstanding-request");
    source.Observe(Sample(source,2,1000,0));transport.Release.Set();
    Until(()=>Volatile.Read(ref transport.Calls)==2,"queued click down and up both delivered");
    var batch=target.OrderedSnapshot(0);
    Until(()=>target.OrderedSnapshot(0).Events.Length==2,"healthy same-session positive has complete ordered event pair");
    batch=target.OrderedSnapshot(0);Need(batch.Events[0].Pressed==1&&batch.Events[1].Released==1,"acquisition epoch prediction retains queued release");
    Need(batch.Events[0].Time==1000&&batch.Events[1].Time==1000&&transport.LastTime==1000,"queued original capture times preserved");
    transport.Entered.Reset();transport.Release.Reset();source.Observe(Sample(source,3,1000,0));Until(()=>transport.Entered.IsSet,"second transport stall active");
    source.Observe(Sample(source,4,1000,4));source.Observe(Sample(source,5,1000,0));
    before.Restart();source.Revoke();Need(before.ElapsedMilliseconds<50&&!source.Ready&&!source.Authority.Focused,"revoke immediately removes local authority without transport wait");
    transport.Release.Set();Until(()=>source.Ready,"revocation acknowledged before reuse");
    Need(transport.Calls==3&&target.OrderedSnapshot(0).Events.Length==0,"revoke cancels queued old generation events");
    source.Observe(Sample(source,6,1000,1,true));source.Observe(Sample(source,7,1000,0));
    Until(()=>target.OrderedSnapshot(0).Events.Length==2,"fresh click after revoke delivered completely");
    Until(()=>transport.Calls==5,"fresh click transport acknowledged");
    Interlocked.Exchange(ref now,1300);Need(!source.Authority.Focused&&!source.Ready,"local expiry starts asynchronous revocation instead of predicting remote expiry");
    Until(()=>source.Ready,"local expiry resynchronizes with remote acknowledgement");
    source.Observe(Sample(source,8,1300,1,true));source.Observe(Sample(source,9,1300,0));
    Until(()=>target.OrderedSnapshot(0).Events.Length==2,"fresh click after local-only expiry remains healthy");
    Until(()=>transport.Calls==7,"local expiry fresh click transport acknowledged");
    Interlocked.Exchange(ref now,1600);target.Snapshot();source.Observe(Sample(source,10,1600,0));
    Until(()=>source.Ready&&transport.Calls==8,"remote-only expiry resynchronizes without terminal fault");
    Need(source.Error==null,"independent freshness observation does not permanently stop healthy transport");
    source.Observe(Sample(source,11,1600,1,true));source.Observe(Sample(source,12,1600,0));
    Until(()=>target.OrderedSnapshot(0).Events.Length==2,"fresh click after remote-only expiry remains complete");
    transport.Fail=true;source.Observe(Sample(source,13,1600,0));Until(()=>source.Completion.IsCompleted,"transport disconnect terminates background owner");
    Need(source.Error!=null&&!source.Ready&&!source.Authority.Focused,"transport failure cannot retain input authority");
   }
  }
  now=2000;
  using(var broker=new InputBroker(()=>Interlocked.Read(ref now)))using(var target=broker.Register(new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),false,true)){
   var transport=new Transport(target);
   using(var source=new AsyncPreviewInputTarget(()=>transport,()=>Interlocked.Read(ref now))){
    Until(()=>source.Ready,"old-generation fixture connected");var old=Sample(source,1,now,1,true);long sequence=1;
    for(int iteration=0;iteration<4;iteration++){
     source.Observe(Sample(source,sequence++,now,1,true));source.Observe(Sample(source,sequence++,now,0));
     int expected=(iteration+1)*2;Until(()=>Volatile.Read(ref transport.Calls)==expected,"old-generation fixture positive acknowledged");
     Interlocked.Add(ref now,300);Need(!source.Authority.Focused,"old-generation local evidence expires");Until(()=>source.Ready,"old-generation remote revoke acknowledged");
    }
    int calls=transport.Calls;bool rejectedWithoutEscape=true;try{source.Observe(old);}catch{rejectedWithoutEscape=false;}
    Need(rejectedWithoutEscape,"old generation rejects without unsigned epoch mapping escape");
    Until(()=>source.Ready,"old-generation rejection completes revoke");Need(source.Error==null&&transport.Calls==calls&&target.OrderedSnapshot(0).Events.Length==0,"old generation never reaches remote event dispatch");
   }
  }
  now=3500;
  using(var broker=new InputBroker(()=>now))using(var target=broker.Register(new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),false,true)){
   var transport=new Transport(target);
   using(var source=new AsyncPreviewInputTarget(()=>transport,()=>now)){
    Until(()=>source.Ready&&Volatile.Read(ref transport.KeepAlives)>0,"idle-disconnect-fixture-live");transport.Fail=true;Until(()=>source.Completion.IsCompleted,"idle-transport-failure-terminates-background-owner");Need(source.Error!=null&&!source.Ready&&!source.Authority.Focused&&target.OrderedSnapshot(0).Events.Length==0,"idle-failure-retains-no-authority-or-events");
   }
  }
  now=4000;
  using(var broker=new InputBroker(()=>now))using(var target=broker.Register(new WorkerIdentity(Guid.NewGuid(),Guid.NewGuid(),1),false,true)){
   var transport=new Transport(target){Block=true};transport.Release.Reset();
   using(var source=new AsyncPreviewInputTarget(()=>transport,()=>now)){
    Until(()=>source.Ready,"backpressure fixture connected");source.Observe(Sample(source,1,now,1,true));Until(()=>transport.Entered.IsSet,"backpressure transport suspended");
    for(int index=0;index<64;index++)source.Observe(Sample(source,index+2,now,1));
    Need(source.PendingCount==64&&source.PeakPendingCount==64&&source.TransportPending,"actual-sixty-four-slots-full-behind-one-outstanding-request");
    bool rejected=false;var timer=Stopwatch.StartNew();try{source.Observe(Sample(source,66,now,1));}catch(InvalidOperationException){rejected=true;}
    Need(rejected&&timer.ElapsedMilliseconds<50&&source.Error=="async-preview-mailbox-full"&&!source.Ready&&!source.Authority.Focused,"saturated mailbox faults explicitly without blocking or retaining authority");
    transport.Release.Set();Until(()=>source.Completion.IsCompleted,"backpressure cleanup completed");Need(transport.Calls==1&&target.OrderedSnapshot(0).Events.Length==0,"overflow cancels queued events and revokes remote state");
   }
  }
  Console.WriteLine("async preview checks passed: "+checks);return 0;
 }catch(Exception error){Console.Error.WriteLine(error);return 1;}}
}
