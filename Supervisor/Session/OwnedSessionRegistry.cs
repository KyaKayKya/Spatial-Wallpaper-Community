using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Supervision {
 public sealed class OwnedSessionStatus {
  public Guid Token{get;}public LaunchRequest Request{get;}public WorkerIdentity Identity{get;}
  public bool Absent{get;}public bool ResourcesReleased{get;}public string State{get;}
  public Exception PrimaryError{get;}public Exception CleanupError{get;}
  internal OwnedSessionStatus(Guid token,LaunchRequest request,RuntimeSession owner){
   var snapshot=owner?.Snapshot();Token=token;Request=request;Absent=request==null;ResourcesReleased=Absent||(snapshot?.ResourcesReleased??false);
   Identity=snapshot?.Identity;State=Absent?"Absent":snapshot==null?"Reserved":snapshot.State.ToString();PrimaryError=snapshot?.PrimaryError;CleanupError=snapshot?.CleanupError;
  }
 }
 // Control tokens are allocated before any asynchronous launch. An absent token
 // cannot start a Worker. Active entries are never evicted or expired; only
 // explicit Forget after cleanup can remove them.
 public sealed class OwnedSessionRegistry {
  sealed class Entry {internal LaunchRequest Request;internal long Expires;internal RuntimeSession Owner;internal SupervisedSession Session;}
  readonly object gate=new object();readonly Dictionary<Guid,Entry> entries=new Dictionary<Guid,Entry>();
  readonly SupervisorRuntime runtime;readonly int maximum;readonly Func<long> now;bool closed;
  public OwnedSessionRegistry(SupervisorRuntime supervisor,int capacity,Func<long> clock){
   if(supervisor==null||clock==null||capacity<1||capacity>64)throw new ArgumentException("session-registry");runtime=supervisor;maximum=capacity;now=clock;
  }
  void Expire(){long at=now();foreach(var token in entries.Where(p=>p.Value.Owner==null&&at>=p.Value.Expires).Select(p=>p.Key).ToArray())entries.Remove(token);}
  public bool ContainsInstance(Guid instance){lock(gate){Expire();return entries.Values.Any(e=>e.Request.Instance==instance);}}
  public bool ContainsInstallation(Guid installation){lock(gate){Expire();return entries.Values.Any(e=>e.Request.Installation==installation&&(e.Owner==null||!e.Owner.ResourcesReleased));}}
  public Guid Reserve(LaunchRequest request){if(request==null)throw new ArgumentNullException(nameof(request));lock(gate){
   Expire();if(closed||entries.Count>=maximum)throw new InvalidOperationException("session-reservation-capacity-or-closed");
   if(entries.Values.Any(e=>e.Request.Instance==request.Instance))throw new InvalidOperationException("session-instance-owned");
   var token=Guid.NewGuid();entries.Add(token,new Entry{Request=request,Expires=checked(now()+15000)});return token;
  }}
  public RuntimeSession Start(Guid token){lock(gate){
   Expire();if(closed||!entries.TryGetValue(token,out var entry))throw new InvalidOperationException("session-reservation-unavailable");
   if(entry.Owner==null)entry.Owner=RuntimeSession.Start(entry.Request.Instance,entry.Request.Generation,async()=>{
    var session=await runtime.Start(entry.Request).ConfigureAwait(false);lock(gate)entry.Session=session;return SupervisedRuntimeSession.Own(session);
   },entry.Request.BuildId);return entry.Owner;
  }}
  internal SupervisedSession Find(WorkerIdentity identity){lock(gate){return entries.Values.Where(e=>e.Session!=null&&e.Owner.State==RuntimeSessionState.Running&&e.Request.Instance==identity.Instance&&e.Request.Generation==identity.Generation&&e.Session.Attempt==identity.Attempt).Select(e=>e.Session).SingleOrDefault();}}
  public OwnedSessionStatus Status(Guid token){lock(gate){Expire();return entries.TryGetValue(token,out var entry)?new OwnedSessionStatus(token,entry.Request,entry.Owner):new OwnedSessionStatus(token,null,null);}}
  public OwnedSessionStatus Stop(Guid token){lock(gate){
   Expire();if(entries.TryGetValue(token,out var entry)){if(entry.Owner==null)entries.Remove(token);else entry.Owner.Stop();}return Status(token);
  }}
  public Task RetryCleanupAsync(Guid token){lock(gate){if(!entries.TryGetValue(token,out var entry)||entry.Owner==null)throw new InvalidOperationException("session-cleanup-unavailable");return entry.Owner.RetryCleanupAsync();}}
  public void Forget(Guid token){lock(gate){if(!entries.TryGetValue(token,out var entry))return;if(entry.Owner==null||!entry.Owner.ResourcesReleased)throw new InvalidOperationException("session-still-owned");entries.Remove(token);}}
  public Task CloseAsync(){RuntimeSession[] owners;lock(gate){closed=true;owners=entries.Values.Where(e=>e.Owner!=null).Select(e=>e.Owner).ToArray();foreach(var owner in owners)owner.Stop();foreach(var token in entries.Where(e=>e.Value.Owner==null).Select(e=>e.Key).ToArray())entries.Remove(token);}return Task.WhenAll(owners.Select(Closed));}
  static async Task Closed(RuntimeSession owner){try{await owner.Completion.ConfigureAwait(false);}catch{if(!owner.ResourcesReleased)throw;}if(!owner.ResourcesReleased)throw new InvalidOperationException("session-close-resources-unproven");}
 }
}
