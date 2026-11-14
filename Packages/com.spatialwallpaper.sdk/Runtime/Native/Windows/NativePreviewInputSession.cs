using System;
using System.Threading;
using System.Threading.Tasks;
using SpatialWallpaper.Sdk.Capabilities;
using SpatialWallpaper.Sdk.Editor.Native;
using SpatialWallpaper.Sdk.Hosting;
using SpatialWallpaper.Supervision.Broker;

namespace SpatialWallpaper.Sdk.Native.Windows {
 public sealed class NativePreviewInputSession:INativeInputSession {
  readonly object sync=new object();readonly int thread;readonly IntPtr window;readonly int[] region;
  readonly WorkerIdentity identity;readonly PinnedProcessOwner renderer;readonly string descriptor;readonly bool externalCollector;
  readonly CancellationTokenSource stop=new CancellationTokenSource();
  WindowsPreviewInput collector;AsyncPreviewInputTarget capture;NativeInputAuthorityReader authority;
  OrderedInputBatch latest;long accepted;bool enabled=true;volatile bool stopping,connected,released;string error,cleanupError;
  public long SourceRevocationSequence{get;private set;}
  public long SourceRevocationTime{get;private set;}
  public string SourceRevocationReason{get;private set;}
  void CaptureRevocation(){if(collector==null)return;SourceRevocationSequence=collector.SourceRevocationSequence;SourceRevocationTime=collector.SourceRevocationTime;SourceRevocationReason=collector.SourceRevocationReason;}
  public Task Completion{get;}
  public bool Ready=>!stopping&&enabled&&connected&&capture!=null&&capture.Ready;
  public bool Connected=>connected;
  public bool ResourcesReleased=>Completion.IsCompleted&&released&&collector==null&&CleanupError==null;
  public string Error=>Volatile.Read(ref error)??capture?.Error;
  public string CleanupError=>Volatile.Read(ref cleanupError);
  public IPreviewInputTarget CaptureTarget=>!stopping&&enabled?Volatile.Read(ref capture):null;
  public bool TransportPending=>Volatile.Read(ref capture)?.TransportPending??false;
  public int PendingCount=>Volatile.Read(ref capture)?.PendingCount??0;
  public int PeakPendingCount=>Volatile.Read(ref capture)?.PeakPendingCount??0;
  public int ProcessId=>0;
  public long CreationFileTime=>0;
  public int[] HelperProcessIds=>new int[0];
  public OrderedInputBatch Latest{get{lock(sync)return latest==null?null:new OrderedInputBatch(latest.Snapshot,latest.Events,latest.Cursor);}}
  public NativePreviewInputSession(string managerDescriptor,Guid instance,uint generation,PinnedProcessOwner owner,IntPtr ownedWindow,int[] physicalRegion,bool useExternalCollector=false){
   if(owner==null)throw new ArgumentNullException(nameof(owner));if(physicalRegion==null||physicalRegion.Length!=4||physicalRegion[2]<=0||physicalRegion[3]<=0||ownedWindow==IntPtr.Zero)throw new ArgumentException("native-preview-binding");
   thread=Thread.CurrentThread.ManagedThreadId;window=ownedWindow;region=(int[])physicalRegion.Clone();descriptor=managerDescriptor;externalCollector=useExternalCollector;
   identity=new WorkerIdentity(Guid.NewGuid(),instance,generation);renderer=PinnedProcessOwner.Open(owner.Id,owner.CreationFileTime);Completion=Task.Run(Run);
  }
  void Owner(){if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("native-preview-owner-thread");}
  bool Proof(bool focus,out int left,out int top,out int width,out int height){left=region[0];top=region[1];width=region[2];height=region[3];return Ready;}
  public void Pump(){
   Owner();CaptureRevocation();if(stopping)return;
   var input=Volatile.Read(ref capture);if(Error!=null||Completion.IsCompleted){Stop();return;}if(input==null||!input.Ready)return;
   if(!externalCollector&&collector==null)collector=new WindowsPreviewInput(window,input,Proof,null);
  }
  public InputAuthority ReadAuthority(){
   if(!Ready)return default(InputAuthority);var local=capture.ReadAuthority();var current=Volatile.Read(ref authority)?.Read()??default(InputAuthority);
   return local.Epoch!=0&&local.Epoch==current.Epoch?current:default(InputAuthority);
  }
  public void SetEnabled(bool value){Owner();if(stopping)return;if(enabled&&!value)capture?.Revoke();enabled=value;}
  public void SetRegion(int[] value){
   Owner();if(value==null||value.Length!=4||value[2]<=0||value[3]<=0)throw new ArgumentException("native-preview-region");
   bool changed=false;for(int i=0;i<4;i++)changed|=region[i]!=value[i];if(!changed)return;
   capture?.Revoke();Array.Copy(value,region,4);
  }
  public void Accept(long cursor){lock(sync){if(latest==null||cursor<accepted||cursor>latest.Cursor)throw new ArgumentException("native-preview-cursor");accepted=cursor;}}
  public void Stop(){Owner();if(stopping)return;stopping=true;CaptureRevocation();collector?.Dispose();collector=null;capture?.Dispose();stop.Cancel();}
  public void Dispose()=>Stop();
  async Task Run(){
   SupervisorManagerConnection connection=null;PinnedProcessOwner host=null;bool registered=false;
   try{
    connection=new SupervisorManagerConnection(descriptor);
    if(!connection.HostProcessId.HasValue||!connection.HostCreationFileTime.HasValue)throw new InvalidOperationException("native-preview-host-pin-required");
    host=PinnedProcessOwner.Open(connection.HostProcessId.Value,connection.HostCreationFileTime.Value);registered=true;
    await connection.RegisterNativeOrderedInput(identity,true,renderer).ConfigureAwait(false);
    stop.Token.ThrowIfCancellationRequested();authority=new NativeInputAuthorityReader(await connection.OpenNativeInputAuthority(identity,renderer).ConfigureAwait(false),identity,host,renderer);
    var input=new AsyncPreviewInputTarget(()=>new PreviewInputChannel(connection.OpenNativePreviewInputStream(identity,renderer,stop.Token).GetAwaiter().GetResult()));
    Volatile.Write(ref capture,input);if(stopping)input.Dispose();
    while(!stopping&&!input.Completion.IsCompleted){
     if(host.HasExited||renderer.HasExited)throw new InvalidOperationException("native-preview-owner-ended");
     long cursor;lock(sync)cursor=accepted;var batch=await connection.ReadNativeOrderedInput(identity,renderer,cursor).ConfigureAwait(false);
     lock(sync)latest=batch;connected=true;await Task.Delay(10,stop.Token).ConfigureAwait(false);
    }
    if(!stopping&&input.Error!=null)throw new InvalidOperationException(input.Error);
   }catch(OperationCanceledException)when(stopping){}catch(Exception failure){error=failure.GetType().Name+":"+failure.Message;}
   finally{
    connected=false;var input=Volatile.Read(ref capture);input?.Dispose();if(input!=null)await input.Completion.ConfigureAwait(false);
    while(registered&&!host.HasExited){
     try{await connection.UnregisterNativeInput(identity).ConfigureAwait(false);registered=false;cleanupError=null;}
     catch(Exception failure){cleanupError="native-preview-unregister:"+failure.Message;await Task.Delay(250).ConfigureAwait(false);}
    }
    Interlocked.Exchange(ref authority,null)?.Dispose();renderer.Dispose();host?.Dispose();released=true;
   }
  }
 }
}
