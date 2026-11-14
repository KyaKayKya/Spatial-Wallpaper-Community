using System;using System.Threading;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Supervision.Windows {
 // Exactly one small write into a dedicated empty pipe. The monitor only sets an event.
 // The child is exited before Dispose, so even a blocked write loses its reader and joins.
 public sealed class WorkerStopChannel:IDisposable {
  readonly WorkerReadPipe pipe;readonly byte[] message;readonly ManualResetEvent wake;readonly Thread sender;
  readonly Func<long> now;long published=-1;int requested,closed;bool disposed;Exception error;
  public long PublishedAt=>Interlocked.Read(ref published);public bool Failed=>Volatile.Read(ref error)!=null;
  public IntPtr ChildReader=>pipe.ChildReader;
  public WorkerStopChannel(string sid,WorkerIdentity identity,Func<long> clock){now=clock??throw new ArgumentNullException(nameof(clock));message=WorkerStopCodec.Encode(identity);try{pipe=new WorkerReadPipe(sid);wake=new ManualResetEvent(false);sender=new Thread(Send){IsBackground=true,Name="SpatialWallpaper stop"};sender.Start();}catch{pipe?.Dispose();wake?.Dispose();throw;}}
  public void CloseChildCopy()=>pipe.CloseChildCopy();
  public void Request(){if(Interlocked.Exchange(ref requested,1)==0)wake.Set();}
  void Send(){try{wake.WaitOne();if(Volatile.Read(ref requested)!=0&&Volatile.Read(ref closed)==0){pipe.Write(message,0,message.Length);Interlocked.Exchange(ref published,now());}}catch(Exception caught){Volatile.Write(ref error,caught);}}
  public void Dispose(){if(disposed)return;Interlocked.Exchange(ref closed,1);wake.Set();if(!sender.Join(1000))throw new InvalidOperationException("stop-sender-still-alive");pipe.Dispose();wake.Dispose();disposed=true;}
 }
}
