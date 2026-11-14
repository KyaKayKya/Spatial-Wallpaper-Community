using System;
using System.Linq;
using System.Collections.Generic;
using System.Threading;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Sdk.Capabilities;

namespace SpatialWallpaper.Supervision.Windows {
 // Registered only through the existing authenticated manager endpoint. Native
 // targets share the SAME InputBroker/focus lock as legacy/preview targets.
 // Ownership is the renderer's original kernel process, not an author message.
 public sealed class NativeInputRegistry:IDisposable {
  sealed class Entry:IDisposable {
   internal PinnedProcessOwner Owner;internal InputTarget Target;internal NativeInputAuthoritySource Authority;internal bool Preview,Ordered;
   public void Dispose(){try{Target?.Dispose();}finally{Authority?.Dispose();Owner?.Dispose();}}
  }
  readonly object gate=new object();readonly InputBroker broker;readonly Dictionary<Guid,Entry> entries=new Dictionary<Guid,Entry>();
  readonly ManualResetEventSlim stop=new ManualResetEventSlim();readonly Thread reaper;bool disposed;
  public NativeInputRegistry(InputBroker sharedBroker){
   broker=sharedBroker??throw new ArgumentNullException(nameof(sharedBroker));
   reaper=new Thread(()=>{while(!stop.Wait(50)){lock(gate){if(disposed)return;Sweep();}}}){IsBackground=true,Name="SpatialWallpaper native input ownership"};reaper.Start();
  }
  void Sweep(){foreach(var key in entries.Where(pair=>pair.Value.Owner.HasExited).Select(pair=>pair.Key).ToArray())RemoveCore(key);}
  void RemoveCore(Guid key){var value=entries[key];entries.Remove(key);value.Dispose();}
  public void Register(WorkerIdentity identity,bool preview,int ownerPid,long ownerCreation)=>RegisterCore(identity,preview,ownerPid,ownerCreation,false);
  public void RegisterOrdered(WorkerIdentity identity,bool preview,int ownerPid,long ownerCreation)=>RegisterCore(identity,preview,ownerPid,ownerCreation,true);
  void RegisterCore(WorkerIdentity identity,bool preview,int ownerPid,long ownerCreation,bool ordered){lock(gate){
   if(disposed)throw new ObjectDisposedException(nameof(NativeInputRegistry));if(identity==null)throw new ArgumentNullException(nameof(identity));Sweep();
   if(entries.Count>=RuntimeLimits.MaxInstances||entries.ContainsKey(identity.Attempt)||entries.Values.Any(e=>e.Target.Identity.Instance==identity.Instance||e.Owner.Id==ownerPid))throw new InvalidOperationException("native-input-registration-capacity-or-duplicate");
   var entry=new Entry{Preview=preview,Ordered=ordered,Owner=PinnedProcessOwner.Open(ownerPid,ownerCreation)};
   try{entry.Target=broker.Register(identity,!preview,ordered);entries.Add(identity.Attempt,entry);}catch{entry.Dispose();throw;}
  }}
  public InputTarget Find(WorkerIdentity identity,bool preview,int ownerPid,long ownerCreation){lock(gate){
   if(disposed||identity==null)return null;Sweep();if(!entries.TryGetValue(identity.Attempt,out var entry)||entry.Preview!=preview||!Matches(entry,identity,ownerPid,ownerCreation))return null;return entry.Target;
  }}
  static bool Matches(Entry entry,WorkerIdentity identity,int ownerPid,long ownerCreation)=>entry.Target.Matches(identity)&&entry.Owner.Id==ownerPid&&entry.Owner.CreationFileTime==ownerCreation;
  public string OpenAuthority(WorkerIdentity identity,int ownerPid,long ownerCreation){lock(gate){
   if(disposed||identity==null)throw new InvalidOperationException("native-input-target-unavailable");Sweep();
   if(!entries.TryGetValue(identity.Attempt,out var entry)||!entry.Ordered||!Matches(entry,identity,ownerPid,ownerCreation))throw new InvalidOperationException("native-input-target-unavailable");
   if(entry.Authority==null){var source=new NativeInputAuthoritySource(identity,entry.Owner);try{broker.BindAuthority(entry.Target,source.Publish);entry.Authority=source;}catch{source.Dispose();throw;}}
   return entry.Authority.Name;
  }}
  public byte[] Snapshot(WorkerIdentity identity,int ownerPid,long ownerCreation){lock(gate){
   if(disposed||identity==null)throw new InvalidOperationException("native-input-target-unavailable");Sweep();
   if(!entries.TryGetValue(identity.Attempt,out var entry)||entry.Ordered||!Matches(entry,identity,ownerPid,ownerCreation))throw new InvalidOperationException("native-input-target-unavailable");return entry.Target.Snapshot();
  }}
  public OrderedInputBatch OrderedSnapshot(WorkerIdentity identity,int ownerPid,long ownerCreation,long acknowledged){lock(gate){
   if(disposed||identity==null)throw new InvalidOperationException("native-input-target-unavailable");Sweep();
   if(!entries.TryGetValue(identity.Attempt,out var entry)||!entry.Ordered||!Matches(entry,identity,ownerPid,ownerCreation))throw new InvalidOperationException("native-input-target-unavailable");return entry.Target.OrderedSnapshot(acknowledged);
  }}
  public bool Remove(WorkerIdentity identity){lock(gate){
   if(disposed||identity==null)return false;if(!entries.TryGetValue(identity.Attempt,out var entry)||!entry.Target.Matches(identity))return false;RemoveCore(identity.Attempt);return true;
  }}
  public void Dispose(){
   stop.Set();if(!reaper.Join(1000))throw new InvalidOperationException("native-input-owner-reaper-unreleased");
   lock(gate){if(disposed)return;disposed=true;foreach(var entry in entries.Values)entry.Dispose();entries.Clear();}stop.Dispose();
  }
 }
}
