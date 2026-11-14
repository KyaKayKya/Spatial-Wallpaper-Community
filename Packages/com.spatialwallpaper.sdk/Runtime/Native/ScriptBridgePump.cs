using System;
using System.Threading;
using SpatialWallpaper.Sdk.Capabilities;

namespace SpatialWallpaper.Sdk.Native {
 public enum QueueResult {Accepted,Full,Stopped,NoTick,Rejected}
 public interface INativeCommandTarget {
  SceneSnapshot Snapshot { get; }
  bool TryPrepare(CommandBatch batch,out object prepared);
  BatchAck Commit(CommandBatch batch,object prepared);
  void CancelPreparation();
 }
 // The only native consumer runs on its creating thread. Background IO merely
 // installs immutable values into one slot; it never invokes the native target.
 public sealed class ScriptBridgePump:IDisposable {
  readonly object gate=new object();readonly SessionIdentity identity;readonly INativeCommandTarget target;readonly int ownerThread;
  SceneSnapshot latest;CommandBatch queued;TickInput issued;BatchAck ack;
  long nextSequence=1,nextTick=1,consumedEvent,deliverySequence;bool stopped,cancelled,awaitingDelivery;
  byte[] inputAuthority;long inputAuthorityNow;readonly Func<InputAuthority> authoritativeInput;readonly Func<long> machineClock;
  public string Error { get; private set; }
  public bool Stopped { get { lock(gate)return stopped; } }
  public SceneSnapshot LatestSnapshot { get { lock(gate)return latest; } }
  public ScriptBridgePump(SessionIdentity identity,INativeCommandTarget target,Func<InputAuthority> authoritativeInput=null,Func<long> machineClock=null){
   this.identity=identity??throw new ArgumentNullException(nameof(identity));this.target=target??throw new ArgumentNullException(nameof(target));identity.Require(identity);
   this.authoritativeInput=authoritativeInput;this.machineClock=machineClock;
   ownerThread=Thread.CurrentThread.ManagedThreadId;latest=target.Snapshot??throw new ArgumentException("native-target-snapshot");
  }
  public void MarkTickIssued(TickInput input){
   if(input==null)throw new ArgumentNullException(nameof(input));
   lock(gate){
    if(stopped||queued!=null||issued!=null||awaitingDelivery||ack!=null||input.Tick!=nextTick)throw new InvalidOperationException("native-tick-not-publishable");
    issued=input;
   }
  }
  public QueueResult TryEnqueue(CommandBatch batch){
   lock(gate){
    if(stopped)return QueueResult.Stopped;
    if(queued!=null||awaitingDelivery||ack!=null)return QueueResult.Full;
    if(issued==null)return QueueResult.NoTick;
    try{identity.Require(batch?.Identity);}catch{stopped=true;Error="native-session-mismatch";return QueueResult.Rejected;}
    if(batch.Sequence!=nextSequence||batch.Tick!=nextTick){stopped=true;Error="native-batch-order";return QueueResult.Rejected;}
    queued=batch;return QueueResult.Accepted;
   }
  }
  void RequireOwner(){if(Thread.CurrentThread.ManagedThreadId!=ownerThread)throw new InvalidOperationException("native-pump-thread");}
  // Supplied by the trusted native owner before pumping, including while an
  // earlier Tick is preparing. A revoked or expired Tick must never commit.
  public void PublishInputAuthority(byte[] current,long machineNow){
   RequireOwner();if(machineNow<0)throw new ArgumentOutOfRangeException(nameof(machineNow));if(current!=null)InputSnapshotCodec.RequireShape(current);
   inputAuthority=current==null?null:(byte[])current.Clone();inputAuthorityNow=machineNow;
  }
  void RequireCurrentInput(TickInput input){
   var packet=input.Inputs;if(packet.Length==0||InputSnapshotCodec.U32(packet,4)==0)return;
   var source=authoritativeInput==null?default(InputAuthority):authoritativeInput();
   var current=inputAuthority;ulong now=(ulong)(machineClock==null?inputAuthorityNow:machineClock()),issuedStamp=InputSnapshotCodec.U64(packet,16);
   JsonData.Require(current!=null&&InputSnapshotCodec.U32(current,4)==1&&InputSnapshotCodec.U64(current,8)==InputSnapshotCodec.U64(packet,8)&&issuedStamp<=now&&now-issuedStamp<=250,"native-issued-input-revoked-or-expired:current="+(current==null?"none":InputSnapshotCodec.U32(current,4)+"/"+InputSnapshotCodec.U64(current,8)+"/"+InputSnapshotCodec.U64(current,16))+",issued="+InputSnapshotCodec.U64(packet,8)+"/"+issuedStamp+",now="+now);
   ulong currentStamp=InputSnapshotCodec.U64(current,16);JsonData.Require(currentStamp>=issuedStamp&&currentStamp<=now&&now-currentStamp<=250,"native-current-input-stale");
   if(authoritativeInput!=null)JsonData.Require(source.Epoch!=0&&source.Epoch==InputSnapshotCodec.U64(packet,8)&&source.Time>=issuedStamp&&source.Time<=now&&now-source.Time<=250,"native-broker-input-revoked-or-expired:authority="+source.Epoch+"/"+source.Time+",issued="+InputSnapshotCodec.U64(packet,8)+"/"+issuedStamp+",now="+now);
  }
  void Cancel(){if(cancelled)return;target.CancelPreparation();cancelled=true;}
  public void Pump(){
   RequireOwner();
   CommandBatch batch;TickInput input;
   lock(gate){if(stopped){batch=null;input=null;}else{latest=target.Snapshot;batch=queued;input=issued;}}
   if(Stopped){Cancel();return;}
   if(batch==null)return;
   try{
    long published=input.Events.Count==0?consumedEvent:Math.Max(consumedEvent,input.Events[input.Events.Count-1].Id);
    SceneCommandProtocol.Validate(batch,identity,latest,nextSequence,nextTick,consumedEvent,published);
    RequireCurrentInput(input);
    if(!target.TryPrepare(batch,out var prepared))return;
    lock(gate){
     if(stopped)return;
     // Final authorization is this commit's linearization point. A completed
     // Broker revoke before this read rejects the batch, even if IO is stalled.
     // Revocation after this point does not roll back an in-progress commit.
     RequireCurrentInput(input);
     var result=target.Commit(batch,prepared);identity.Require(result?.Identity);
     long revision=checked(batch.BaseRevision+(SceneCommandProtocol.AdvancesRevision(batch)?1:0));
     JsonData.Require(result.Accepted&&result.Tick==batch.Tick&&result.Sequence==batch.Sequence&&result.Snapshot!=null&&result.Snapshot.SceneRevision==revision&&result.Snapshot.SnapshotSequence>latest.SnapshotSequence,"native-commit-ack");
     latest=result.Snapshot;ack=result;queued=null;issued=null;deliverySequence=batch.Sequence;awaitingDelivery=true;
     nextSequence=checked(nextSequence+1);nextTick=checked(nextTick+1);consumedEvent=batch.ConsumedEvent;
    }
   }catch(Exception failure){
    lock(gate){
     stopped=true;Error=failure.GetType().Name+":"+failure.Message;
     if(Error.Length>256)Error=Error.Substring(0,256);
     ack=new BatchAck(identity,batch.Tick,batch.Sequence,false,latest);queued=null;issued=null;deliverySequence=batch.Sequence;awaitingDelivery=true;
    }
    Cancel();
   }
  }
  public bool TryTakeAck(out BatchAck value){lock(gate){value=ack;if(value==null)return false;ack=null;return true;}}
  // Taking an ack off the mailbox is not delivery: pipe backpressure must not
  // permit a guessed next Tick to slip in before its input has been published.
  public void ConfirmAckDelivered(long sequence){lock(gate){if(!awaitingDelivery||sequence!=deliverySequence)throw new InvalidOperationException("native-ack-delivery-order");awaitingDelivery=false;}}
  public void Stop(){lock(gate){stopped=true;queued=null;issued=null;}}
  public void Dispose(){Stop();if(Thread.CurrentThread.ManagedThreadId==ownerThread)Cancel();}
 }
}
