using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace SpatialWallpaper.Sdk.Capabilities {
 public interface IPreviewInputKeepAlive {
  void KeepAlive();
 }
 public sealed class AsyncPreviewInputTarget:IPreviewInputTarget,IDisposable {
  readonly object sync=new object();readonly Queue<Pending> queue=new Queue<Pending>();
  readonly InputGate local=new InputGate();readonly Func<long> clock;readonly Func<IPreviewInputTarget> connect;
  bool closed,ready,transportPending;int peakPendingCount;long generation;ulong localBase,remoteBase;string error;
  sealed class Pending {internal InputObservation Observation;internal bool Revoke;internal long Generation;internal ulong ExpectedEpoch;}
  public Task Completion{get;}
  public string Error{get{lock(sync)return error;}}
  public bool Ready{get{lock(sync)return ready&&!closed;}}
  public bool TransportPending{get{lock(sync)return transportPending;}}
  public int PendingCount{get{lock(sync)return queue.Count;}}
  public int PeakPendingCount{get{lock(sync)return peakPendingCount;}}
  public ulong CaptureEpoch{get{lock(sync)return local.CaptureEpoch;}}
  InputState ReadLocal(){if(closed||!ready)return default(InputState);ulong epoch=local.CaptureEpoch;var current=local.Read(clock());if(local.CaptureEpoch!=epoch){Invalidate();return default(InputState);}return current;}
  public InputState Authority{get{lock(sync)return ReadLocal();}}
  public InputAuthority ReadAuthority(){lock(sync){var current=ReadLocal();return current.Focused?new InputAuthority(RemoteEpoch(current.Epoch),(ulong)current.EvidenceTime):default(InputAuthority);}}
  public AsyncPreviewInputTarget(Func<IPreviewInputTarget> connect,Func<long> machineClock=null){
   this.connect=connect??throw new ArgumentNullException(nameof(connect));clock=machineClock??WindowsMachineClock.Milliseconds;
   Completion=Task.Run(Run);
  }
  ulong RemoteEpoch(ulong epoch)=>checked(remoteBase+epoch-localBase);
  public void Observe(InputObservation value){lock(sync){
   if(closed)throw new InvalidOperationException("async-preview-closed:"+error);
   if(!ready)return;
   if(value.CaptureEpoch!=local.CaptureEpoch){Invalidate();return;}
   if(queue.Count>=64){Fail("async-preview-mailbox-full");throw new InvalidOperationException(error);}
   ulong remote=RemoteEpoch(value.CaptureEpoch);local.Observe(value,clock());
   var observation=new InputObservation(value.Sequence,value.Time,value.Target,value.Foreground,value.ShellFocus,value.Session,value.Adapter,value.Modifier,value.PrimaryDown,value.Held,value.X,value.Y,value.Wheel,remote,value.Heartbeat);
   queue.Enqueue(new Pending{Observation=observation,Generation=generation,ExpectedEpoch=RemoteEpoch(local.CaptureEpoch)});peakPendingCount=Math.Max(peakPendingCount,queue.Count);Monitor.PulseAll(sync);
  }}
  public void Revoke(){lock(sync){
   if(closed||!ready)return;
   Invalidate();
  }}
  void Invalidate(){
   ready=false;local.Revoke(clock());generation=checked(generation+1);queue.Clear();
   queue.Enqueue(new Pending{Revoke=true,Generation=generation});Monitor.PulseAll(sync);
  }
  void Fail(string reason){
   if(error==null)error=reason;closed=true;ready=false;local.Revoke(clock());queue.Clear();Monitor.PulseAll(sync);
  }
  void Run(){
   IPreviewInputTarget transport=null;
   try{
    transport=connect();lock(sync){if(closed)return;localBase=local.CaptureEpoch;remoteBase=transport.CaptureEpoch;ready=true;}
    while(true){
     Pending pending=null;lock(sync){while(!closed&&queue.Count==0){Monitor.Wait(sync,50);if(transport is IPreviewInputKeepAlive)break;}if(closed)break;if(queue.Count!=0){pending=queue.Dequeue();if(pending.Generation!=generation)continue;}transportPending=true;}
     if(pending==null){((IPreviewInputKeepAlive)transport).KeepAlive();lock(sync){transportPending=false;if(!closed&&ready&&transport.CaptureEpoch!=RemoteEpoch(local.CaptureEpoch))Invalidate();}continue;}
     if(pending.Revoke)transport.Revoke();else transport.Observe(pending.Observation);
     ulong received=transport.CaptureEpoch;
     lock(sync){
      transportPending=false;
      if(closed||pending.Generation!=generation)continue;
      if(pending.Revoke){localBase=local.CaptureEpoch;remoteBase=received;ready=true;}
      else if(received!=pending.ExpectedEpoch)Invalidate();
     }
    }
   }catch(Exception failure){lock(sync)Fail(failure.GetType().Name+":"+failure.Message);}
   finally{
    lock(sync){closed=true;ready=false;transportPending=false;local.Revoke(clock());queue.Clear();}
    if(transport!=null){
     try{transport.Revoke();}catch(Exception failure){lock(sync)if(error==null)error="async-preview-cleanup:"+failure.Message;}
     try{(transport as IDisposable)?.Dispose();}catch(Exception failure){lock(sync)if(error==null)error="async-preview-dispose:"+failure.Message;}
    }
   }
  }
  public void Dispose(){lock(sync){if(closed)return;closed=true;ready=false;local.Revoke(clock());queue.Clear();Monitor.PulseAll(sync);}}
 }
}
