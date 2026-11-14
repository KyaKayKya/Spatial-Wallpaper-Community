using System;
using System.Threading;
using System.Threading.Tasks;
using System.Linq;
using Newtonsoft.Json.Linq;
namespace SpatialWallpaper.Content {
 public static class ContentLibraryStatus {
  public static string Row(JObject state,string display,string work,bool ready,bool still){
   if((string)state["applied"][display]?["work_id"]!=work)return "已导入 · 尚未应用";
   if(!ready)return still?"上次成功应用 · 当前为静态画面":"上次成功应用 · 当前未播放";
   return (string)state["applied"][display]["revision_id"]==(string)state["works"][work]["latest_revision"]?"正在播放":"正在播放 · 较早修订";
  }
 }
 public interface IContentPresentation {
  Task HoldStaticAsync();Task UnloadAsync();Task LoadAsync(ValidatedPackage package,JObject revision,CancellationToken cancellation);Task FirstFrameAsync(CancellationToken cancellation);void ReleaseStatic();
 }
 public interface ISandboxContentPresentation:IContentPresentation {
  Task LoadSandboxAsync(SandboxContentBuild package,JObject revision,CancellationToken cancellation);
 }
 public static class ContentStartupRecovery {
  public static async Task<string> RestoreAsync(ContentStore store,string display,IContentPresentation presentation){
   var previous=store.Snapshot["applied"][display] as JObject;if(previous==null)return null;
   try{var revision=store.Revision((string)previous["work_id"],(string)previous["revision_id"]);using(var prepared=ContentCoordinator.Prepare(store,presentation,revision,(string)previous["revision_id"]))using(var timeout=new CancellationTokenSource(30000)){await prepared.Load(timeout.Token);await presentation.FirstFrameAsync(timeout.Token);timeout.Token.ThrowIfCancellationRequested();}return null;}
   catch{try{await presentation.UnloadAsync();return "startup-content-recovery-failed";}catch{return "startup-recovery-and-cleanup-failed";}}
  }
 }
 public sealed class ContentCoordinator {
  internal sealed class Prepared:IDisposable {internal readonly Func<CancellationToken,Task> Load;readonly IDisposable snapshot;internal Prepared(Func<CancellationToken,Task> load,IDisposable owner=null){Load=load;snapshot=owner;}public void Dispose(){snapshot?.Dispose();}}
  internal static Prepared Prepare(ContentStore store,IContentPresentation presentation,JObject revision,string revisionId){
   if(store.IsSandboxBuild((string)revision["build_id"])){var sandbox=presentation as ISandboxContentPresentation;if(sandbox==null)throw new System.IO.InvalidDataException("sandbox-presentation-unavailable");var package=store.OpenSandboxBuild((string)revision["work_id"],revisionId);return new Prepared(token=>sandbox.LoadSandboxAsync(package,revision,token),package);}
   var legacy=PackageValidator.Validate(store.BuildPath((string)revision["build_id"]));return new Prepared(token=>presentation.LoadAsync(legacy,revision,token));
  }
  readonly ContentStore store;readonly IContentPresentation presentation;bool busy;
  public ContentCoordinator(ContentStore store,IContentPresentation presentation){this.store=store;this.presentation=presentation;}
  public async Task<JObject> RemoveAsync(string display,string work,Func<Guid,Task> uninstallStorage,CancellationToken cancellation){
   if(busy)throw new InvalidOperationException("content-operation-busy");busy=true;string phase="validate";bool storageDeleted=false,stopped=false;
   try{
    if(string.IsNullOrEmpty(display)||display.Length>256)throw new System.IO.InvalidDataException("display-id");
    store.Latest(work);var snapshot=store.Snapshot;
    bool sandbox=store.IsSandboxBuild((string)snapshot["works"][work]["default_build"])||((JObject)snapshot["revisions"]).Properties().Any(p=>(string)p.Value["work_id"]==work&&store.IsSandboxBuild((string)p.Value["build_id"]));
    if(sandbox&&uninstallStorage==null)throw new InvalidOperationException("uninstall-storage-unavailable");
    cancellation.ThrowIfCancellationRequested();
    if((string)snapshot["applied"][display]?["work_id"]==work){phase="unload-target";await presentation.UnloadAsync();stopped=true;phase="release-static";presentation.ReleaseStatic();}
    if(sandbox){phase="delete-storage";await uninstallStorage(Guid.ParseExact(work,"D"));storageDeleted=true;}
    cancellation.ThrowIfCancellationRequested();phase="remove-library";var result=store.Remove(work);result["succeeded"]=true;result["storage_deleted"]=storageDeleted;result["presentation_stopped"]=stopped;return result;
   }catch(Exception error){string detail=error.GetBaseException().Message;if(detail.Length>2048)detail=detail.Substring(0,2048);return new JObject{["succeeded"]=false,["removed"]=false,["work_id"]=work,["storage_deleted"]=storageDeleted,["presentation_stopped"]=stopped,["failure_phase"]=phase,["error"]="uninstall-failed",["diagnostic"]=detail};}
   finally{busy=false;}
  }
  public async Task<JObject> ApplyAsync(string display,string work,CancellationToken cancellation){
   if(busy)throw new InvalidOperationException("content-operation-busy");busy=true;bool transition=false,restored=false;string target=null;
   JObject previous=null;Prepared prepared=null;string phase="validate",recoveryError=null;
   try{
    if(string.IsNullOrEmpty(display)||display.Length>256)throw new System.IO.InvalidDataException("display-id");
    previous=store.Snapshot["applied"][display] as JObject;
    target=store.Latest(work);var revision=store.Revision(work,target);prepared=Prepare(store,presentation,revision,target);cancellation.ThrowIfCancellationRequested();
    phase="hold-static";await presentation.HoldStaticAsync();transition=true;phase="unload-old";await presentation.UnloadAsync();
    using(var timeout=CancellationTokenSource.CreateLinkedTokenSource(cancellation)){timeout.CancelAfter(30000);phase="load-new";await prepared.Load(timeout.Token);phase="first-frame";await presentation.FirstFrameAsync(timeout.Token);timeout.Token.ThrowIfCancellationRequested();}
    phase="publish-applied";
    store.RecordApplied(display,work,target);
    // Durable publication commits the verified revision. A later presentation
    // cleanup error must not restore old pixels behind the new applied record.
    string cleanupError=null;try{presentation.ReleaseStatic();}catch{cleanupError="static-release-failed";}
    return new JObject{["succeeded"]=true,["work_id"]=work,["revision_id"]=target,["restored"]=false,["cleanup_error"]=cleanupError};
   }catch(Exception e){
    if(transition){
     try{
      await presentation.UnloadAsync();
      if(previous!=null){string oldWork=(string)previous["work_id"],oldRevision=(string)previous["revision_id"];var old=store.Revision(oldWork,oldRevision);using(var restore=Prepare(store,presentation,old,oldRevision))using(var timeout=new CancellationTokenSource(30000)){await restore.Load(timeout.Token);await presentation.FirstFrameAsync(timeout.Token);timeout.Token.ThrowIfCancellationRequested();}restored=true;}
      presentation.ReleaseStatic();
     }catch{recoveryError="old-content-recovery-failed";try{await presentation.UnloadAsync();}catch{recoveryError="recovery-and-cleanup-failed";}restored=false;}
    }
    string detail=e.GetBaseException().Message;if(detail.Length>2048)detail=detail.Substring(0,2048);
    return new JObject{["succeeded"]=false,["work_id"]=work,["revision_id"]=target,["restored"]=restored,["unchanged"]=!transition&&previous!=null,["failure_phase"]=phase,["recovery_error"]=recoveryError,["error"]=e is OperationCanceledException?"apply-cancelled":"apply-failed",["diagnostic"]=detail};
   }finally{try{prepared?.Dispose();}finally{busy=false;}}
  }
 }
}
