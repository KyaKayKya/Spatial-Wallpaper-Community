using System;using System.IO;using System.Text;using System.Threading;using System.Threading.Tasks;using System.Collections.Generic;using System.Collections.Concurrent;
namespace SpatialWallpaper.Supervision.Broker {
 public enum StorageFailure {None,Busy,Conflict,Quota,Invalid,Closed,Io,TooLarge,Denied,Unavailable}
 public sealed class StorageResult {
  readonly byte[] value;public StorageFailure Failure{get;}public long Version{get;}public byte[] Value=>value==null?null:(byte[])value.Clone();
  internal StorageResult(StorageFailure failure,long version=0,byte[] data=null){Failure=failure;Version=version;value=data;}
 }
 // Bounded asynchronous persistence lane. No accepted continuation runs on the disk thread.
 public sealed class StorageBroker {
#if SDK_SUPERVISOR_TESTS
  internal static Action<BrokerSession,string,StorageResult> BeforeReadReply{get;set;}
  internal static Action<BrokerSession,string,StorageResult,Action> PublishReadReply{get;set;}
  internal static Action<BrokerSession,IDictionary<string,byte[]>,StorageResult> BeforeWriteReply{get;set;}
#endif
  public const int MaxValueBytes=32768,MaxChanges=8;
  sealed class Work {
   internal BrokerSession Session;internal bool Write;internal Guid RemoveInstallation;internal string Key;internal long Version;internal Dictionary<string,byte[]> Changes;
   internal readonly TaskCompletionSource<StorageResult> Result=new TaskCompletionSource<StorageResult>(TaskCreationOptions.RunContinuationsAsynchronously);
  }
  readonly bool sharedProcess;
  readonly BlockingCollection<Work> queue;readonly SemaphoreSlim credits;readonly string root;int closed;
  readonly TaskCompletionSource<bool> initialized=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously),completion=new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
  public Task Initialized=>initialized.Task;public Task Completion=>completion.Task;
  public StorageBroker(string directory,int capacity=16,bool sharedProcess=false){this.sharedProcess=sharedProcess;if(string.IsNullOrEmpty(directory)||capacity<1||capacity>64)throw new ArgumentException("storage-broker-policy");root=Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar,Path.AltDirectorySeparatorChar);queue=new BlockingCollection<Work>(capacity);credits=new SemaphoreSlim(capacity,capacity);new Thread(Run){IsBackground=true,Name="SpatialWallpaper private storage"}.Start();}
  static bool Key(string key){if(string.IsNullOrEmpty(key)||key.Length>PrivateStore.MaxKeyBytes)return false;try{return new UTF8Encoding(false,true).GetByteCount(key)<=PrivateStore.MaxKeyBytes;}catch(EncoderFallbackException){return false;}}
  static Task<StorageResult> Failed(StorageFailure failure)=>Task.FromResult(new StorageResult(failure));
  Task<StorageResult> Submit(BrokerSession session,string key,long version,IDictionary<string,byte[]> changes){
   bool write=changes!=null;if(session==null||version<0||!write&&!Key(key)||write&&changes.Count>MaxChanges)return Failed(StorageFailure.Invalid);
   try{session.Demand("private-storage");}catch(UnauthorizedAccessException){return Failed(StorageFailure.Denied);}catch(InvalidOperationException){return Failed(StorageFailure.Closed);}
   if(Volatile.Read(ref closed)!=0)return Failed(StorageFailure.Closed);if(!credits.Wait(0))return Failed(StorageFailure.Busy);
   try{
    var work=new Work{Session=session,Write=write,Key=key,Version=version};
    if(write){work.Changes=new Dictionary<string,byte[]>(StringComparer.Ordinal);long total=0;foreach(var pair in changes){if(!Key(pair.Key)||pair.Value!=null&&pair.Value.Length>MaxValueBytes||(total+=Encoding.UTF8.GetByteCount(pair.Key)+(pair.Value?.Length??0))>MaxValueBytes){credits.Release();return Failed(StorageFailure.TooLarge);}work.Changes.Add(pair.Key,pair.Value==null?null:(byte[])pair.Value.Clone());}}
    if(Volatile.Read(ref closed)!=0||!queue.TryAdd(work)){credits.Release();return Failed(StorageFailure.Closed);}return work.Result.Task;
   }catch(InvalidOperationException){credits.Release();return Failed(StorageFailure.Closed);}catch{credits.Release();throw;}
  }
  public Task<StorageResult> Read(BrokerSession session,string key)=>Submit(session,key,0,null);
  public Task<StorageResult> Commit(BrokerSession session,long expectedVersion,IDictionary<string,byte[]> changes){if(changes==null)return Failed(StorageFailure.Invalid);return Submit(session,null,expectedVersion,changes);}
  // Trusted management only; Worker capability messages cannot submit this operation.
  public Task<StorageResult> Uninstall(Guid installation){
   if(installation==Guid.Empty)return Failed(StorageFailure.Invalid);
   if(Volatile.Read(ref closed)!=0)return Failed(StorageFailure.Closed);if(!credits.Wait(0))return Failed(StorageFailure.Busy);
   try{var work=new Work{RemoveInstallation=installation};if(Volatile.Read(ref closed)!=0||!queue.TryAdd(work)){credits.Release();return Failed(StorageFailure.Closed);}return work.Result.Task;}
   catch(InvalidOperationException){credits.Release();return Failed(StorageFailure.Closed);}catch{credits.Release();throw;}
  }
  static StorageResult Execute(PrivateStore store,Work work){
   try{
    if(work.RemoveInstallation!=Guid.Empty){store.Uninstall(work.RemoveInstallation);return new StorageResult(StorageFailure.None);}
    work.Session.Demand("private-storage");if(work.Write)return new StorageResult(StorageFailure.None,store.Commit(work.Session,work.Version,work.Changes));
    var snapshot=store.Read(work.Session);if(snapshot.ValueBytes(work.Key)>MaxValueBytes)return new StorageResult(StorageFailure.TooLarge,snapshot.Version);return new StorageResult(StorageFailure.None,snapshot.Version,snapshot.Get(work.Key));
   }catch(StoreQuotaException){return new StorageResult(StorageFailure.Quota);}catch(StoreConflictException){return new StorageResult(StorageFailure.Conflict);}
   catch(UnauthorizedAccessException){return new StorageResult(StorageFailure.Denied);}catch(ArgumentException){return new StorageResult(StorageFailure.Invalid);}
   catch(InvalidOperationException){return new StorageResult(StorageFailure.Closed);}catch(InvalidDataException){return new StorageResult(StorageFailure.Io);}catch(IOException){return new StorageResult(StorageFailure.Io);}catch(System.ComponentModel.Win32Exception){return new StorageResult(StorageFailure.Io);}
  }
  StorageResult SharedExecute(Work work){
   // Only this bounded disk actor may acquire the cross-process serialization gate.
   string key;using(var sha=System.Security.Cryptography.SHA256.Create())key=BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(root.ToUpperInvariant()))).Replace("-","");
   using(var mutex=new Mutex(false,"Local\\SpatialWallpaper.PrivateStore."+key)){bool acquired=false;try{try{acquired=mutex.WaitOne(2000);}catch(AbandonedMutexException){acquired=true;}if(!acquired)return new StorageResult(StorageFailure.Busy);using(var store=new PrivateStore(root))return Execute(store,work);}finally{if(acquired)mutex.ReleaseMutex();}}
  }
  void Run(){
   Exception failure=null;
   try{using(var store=sharedProcess?null:new PrivateStore(root)){initialized.TrySetResult(true);foreach(var work in queue.GetConsumingEnumerable()){try{var result=sharedProcess?SharedExecute(work):Execute(store,work);
#if SDK_SUPERVISOR_TESTS
    if(work.RemoveInstallation==Guid.Empty){
     if(!work.Write)BeforeReadReply?.Invoke(work.Session,work.Key,result);
     else BeforeWriteReply?.Invoke(work.Session,work.Changes,result);
     if(!work.Write&&PublishReadReply!=null){PublishReadReply(work.Session,work.Key,result,()=>work.Result.TrySetResult(result));continue;}
    }
#endif
    work.Result.TrySetResult(result);}catch{work.Result.TrySetResult(new StorageResult(StorageFailure.Io));throw;}finally{credits.Release();}}}}
   catch(Exception error){failure=error;}
   finally{
    Interlocked.Exchange(ref closed,1);queue.CompleteAdding();Work pending;while(queue.TryTake(out pending)){pending.Result.TrySetResult(new StorageResult(StorageFailure.Io));credits.Release();}
    if(!initialized.Task.IsCompleted)initialized.TrySetException(failure??new InvalidOperationException("storage-not-initialized"));
    if(failure==null)completion.TrySetResult(true);else completion.TrySetException(failure);
   }
  }
  public void Close(){if(Interlocked.Exchange(ref closed,1)==0)queue.CompleteAdding();}
 }
}
