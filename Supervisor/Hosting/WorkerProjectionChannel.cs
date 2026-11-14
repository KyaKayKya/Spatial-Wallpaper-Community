using System;using System.Threading;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Supervision.Windows {
 // One in-flight packet and one replaceable latest packet. The monitor never
 // waits on a pipe; cleanup joins only after the Worker has exited.
 public sealed class WorkerProjectionChannel:IDisposable {
  readonly WorkerReadPipe pipe;readonly WorkerIdentity identity;readonly object gate=new object();readonly AutoResetEvent wake=new AutoResetEvent(false);readonly Thread sender;
  byte[] pending;long revision;bool closed,disposed;Exception error;
  public bool Failed=>Volatile.Read(ref error)!=null;public IntPtr ChildReader=>pipe.ChildReader;
  public WorkerProjectionChannel(string sid,WorkerIdentity expected){identity=expected??throw new ArgumentNullException(nameof(expected));try{pipe=new WorkerReadPipe(sid);sender=new Thread(Send){IsBackground=true,Name="SpatialWallpaper projection sender"};sender.Start();}catch{pipe?.Dispose();wake.Dispose();throw;}}
  public void CloseChildCopy()=>pipe.CloseChildCopy();
  public void Update(ProjectionConfiguration value){if(value==null)throw new ArgumentNullException(nameof(value));lock(gate){if(closed||Failed)throw new InvalidOperationException("projection-channel-closed");if(value.Revision<=revision)throw new InvalidOperationException("projection-revision");pending=WorkerProjectionCodec.Encode(identity,value);revision=value.Revision;wake.Set();}}
  void Send(){try{while(true){wake.WaitOne();byte[] bytes;lock(gate){if(closed)return;bytes=pending;pending=null;}if(bytes!=null)pipe.Write(bytes,0,bytes.Length);}}
   catch(Exception caught){lock(gate)if(!closed)Volatile.Write(ref error,caught);}}
  public void Close(){lock(gate){if(closed)return;closed=true;pending=null;wake.Set();}}
  public void Dispose(){if(disposed)return;Close();if(!sender.Join(1000))throw new InvalidOperationException("projection-sender-still-alive");pipe.Dispose();wake.Dispose();disposed=true;}
 }
}
