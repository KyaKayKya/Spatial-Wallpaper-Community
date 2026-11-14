using System;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Supervision.Windows {
 public sealed class PixelFramePipe:IDisposable {
  readonly WorkerWritePipe pipe;readonly WorkerIdentity identity;readonly int width,height;readonly byte[] header=new byte[WorkerFrameCodec.HeaderBytes];
  public byte[] Pixels{get;}public long Sequence{get;private set;}public long ProjectionRevision{get;private set;}
  public IntPtr ChildWriter=>pipe.ChildWriter;
  public PixelFramePipe(string sid,WorkerIdentity session,int pixelWidth,int pixelHeight){
   identity=session??throw new ArgumentNullException(nameof(session));width=pixelWidth;height=pixelHeight;int bytes=WorkerFrameCodec.PayloadBytes(width,height);
   Pixels=new byte[bytes];pipe=new WorkerWritePipe(sid);
  }
  public void CloseChildCopy()=>pipe.CloseChildCopy();
  public bool Receive(){
   if(!pipe.ReadExact(header,true))return false;
   long revision;long next=WorkerFrameCodec.Validate(header,identity,width,height,Sequence,out revision);
   pipe.ReadExact(Pixels);Sequence=next;ProjectionRevision=revision;return true;
  }
  public void Dispose()=>pipe.Dispose();
 }
}
