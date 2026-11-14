using System;using System.Drawing;using System.Drawing.Imaging;using System.IO;using System.Runtime.InteropServices;using System.Threading;using System.Windows.Forms;using Newtonsoft.Json.Linq;using SpatialWallpaper.Sdk.Hosting;
namespace SpatialWallpaper.Supervision.Presentation {
 // Trusted target frame submission. Visibility is tested independently;
 // normal desktop occlusion is not a runtime failure.
 public sealed class WindowsFramePresenter:IDisposable {
  [DllImport("winmm.dll")]static extern uint timeBeginPeriod(uint milliseconds);
  [DllImport("winmm.dll")]static extern uint timeEndPeriod(uint milliseconds);
  bool precisionTimer;
  [StructLayout(LayoutKind.Sequential)]struct BitmapInfo {public uint Size;public int Width,Height;public ushort Planes,Bits;public uint Compression,ImageBytes;public int Xppm,Yppm;public uint Colors,Important;}
  [DllImport("gdi32.dll")]static extern uint SetDIBitsToDevice(IntPtr dc,int x,int y,uint width,uint height,int sourceX,int sourceY,uint firstScan,uint scans,IntPtr bits,ref BitmapInfo info,uint usage);
  public sealed class TargetStamp {
   internal readonly Guid Target;internal readonly long Revision,ProjectionRevision;internal readonly IntPtr Handle;internal readonly Rectangle Screen;
   internal TargetStamp(Guid target,long revision,long projectionRevision,IntPtr handle,Rectangle screen){Target=target;Revision=revision;ProjectionRevision=projectionRevision;Handle=handle;Screen=screen;}
  }
  readonly Control target;readonly RuntimeSession session;readonly WorkerIdentity identity;readonly string build;readonly int width,height,thread;readonly Guid targetId=Guid.NewGuid();readonly int[] bgra;GCHandle pixelsPin;
  readonly EventHandler changed;readonly PaintEventHandler painted;Bitmap bitmap;long revision=1,geometryRevision=1,presentedGeometryRevision,lastSequence,activeProjectionRevision;bool disposed,displayed;readonly bool scaleTarget;TargetStamp displayedStamp,submittedStamp;
  public WindowsFramePresenter(Control target,RuntimeSession session,string build,int width,int height,bool scaleTarget=false){
   if(target==null||session==null||build==null||build.Length!=64)throw new ArgumentException("presenter-binding");RuntimeLimits.RequireRenderSize(width,height);
   this.target=target;this.session=session;this.build=build;this.width=width;this.height=height;this.scaleTarget=scaleTarget;thread=Thread.CurrentThread.ManagedThreadId;
   var snapshot=session.Snapshot();if(snapshot.State!=RuntimeSessionState.Running||snapshot.Identity==null)throw new InvalidOperationException("presenter-session-ended");if(session.BuildId!=build)throw new InvalidDataException("presenter-build-identity");identity=snapshot.Identity;
   RequireTarget();using(var graphics=target.CreateGraphics())if(graphics.DpiX!=96||graphics.DpiY!=96)throw new PlatformNotSupportedException("presenter-unverified-DPI");
   bgra=new int[checked(width*height)];pixelsPin=GCHandle.Alloc(bgra,GCHandleType.Pinned);try{bitmap=new Bitmap(width,height,width*4,PixelFormat.Format32bppArgb,pixelsPin.AddrOfPinnedObject());}catch{pixelsPin.Free();throw;}changed=(sender,args)=>{revision=checked(revision+1);geometryRevision=checked(geometryRevision+1);presentedGeometryRevision=0;displayed=false;};painted=(sender,args)=>{if(CanDraw())Draw(args.Graphics);};
   target.Resize+=changed;target.LocationChanged+=changed;target.HandleDestroyed+=changed;target.Paint+=painted;
   precisionTimer=timeBeginPeriod(1)==0;
  }
  void Draw(Graphics graphics){
   if(!scaleTarget){var info=new BitmapInfo{Size=40,Width=width,Height=-height,Planes=1,Bits=32,ImageBytes=checked((uint)(width*height*4))};var dc=graphics.GetHdc();try{if(SetDIBitsToDevice(dc,0,0,(uint)width,(uint)height,0,0,0,(uint)height,pixelsPin.AddrOfPinnedObject(),ref info,0)==0)throw new InvalidOperationException("presenter-native-blit-failed");}finally{graphics.ReleaseHdc(dc);}return;}
   var previous=graphics.CompositingMode;try{graphics.CompositingMode=System.Drawing.Drawing2D.CompositingMode.SourceCopy;graphics.DrawImage(bitmap,target.ClientRectangle);}finally{graphics.CompositingMode=previous;}
  }
  bool TargetSizeValid()=>scaleTarget?target.ClientSize.Width>0&&target.ClientSize.Height>0&&target.ClientSize.Width<=8192&&target.ClientSize.Height<=8192&&Math.Abs((double)target.ClientSize.Width/target.ClientSize.Height-(double)width/height)<=.0001:target.ClientSize.Width==width&&target.ClientSize.Height==height;
  bool CanDraw(){return displayed&&!disposed&&session.State==RuntimeSessionState.Running&&!target.IsDisposed&&target.IsHandleCreated&&target.Visible&&displayedStamp!=null&&displayedStamp.Revision==revision&&displayedStamp.Handle==target.Handle&&displayedStamp.Screen==Screen();}
  void RequireTarget(){if(disposed||Thread.CurrentThread.ManagedThreadId!=thread||target.IsDisposed||!target.IsHandleCreated||!target.Visible||!TargetSizeValid())throw new InvalidOperationException("presenter-target-unavailable");}
  Rectangle Screen(){return new Rectangle(target.PointToScreen(Point.Empty),target.ClientSize);}
  public TargetStamp Bind(){return BindProjection(0);}
  internal void RequireInputBinding(Control value,RuntimeSession candidate){RequireTarget();if(!ReferenceEquals(target,value))throw new ArgumentException("preview-input-presentation-target");if(!ReferenceEquals(session,candidate))throw new ArgumentException("preview-input-presentation-session");}
  // Input binds to this live target after its first frame submission.
  // Native hit/focus checks decide whether a pointer may interact with it.
  public bool TryGetPresentedRegion(out Rectangle region){
   region=Rectangle.Empty;if(disposed||Thread.CurrentThread.ManagedThreadId!=thread||presentedGeometryRevision!=geometryRevision||submittedStamp==null||session.State!=RuntimeSessionState.Running||target.IsDisposed||!target.IsHandleCreated||!target.Visible||!TargetSizeValid()||submittedStamp.Handle!=target.Handle||submittedStamp.Screen!=Screen())return false;region=submittedStamp.Screen;return true;
  }
  internal bool TargetGeometryChanged(TargetStamp stamp){RequireTarget();return stamp!=null&&stamp.Target==targetId&&stamp.Handle==target.Handle&&(stamp.Revision!=revision||stamp.Screen!=Screen());}
  public TargetStamp BindProjection(long projectionRevision){RequireTarget();if(projectionRevision<0)throw new ArgumentException("presenter-projection-revision");if(projectionRevision<activeProjectionRevision)throw new InvalidOperationException("presenter-projection-rollback");if(submittedStamp!=null&&submittedStamp.Revision==revision&&submittedStamp.Screen!=Screen()){revision=checked(revision+1);geometryRevision=checked(geometryRevision+1);presentedGeometryRevision=0;displayed=false;}if(projectionRevision>activeProjectionRevision){revision=checked(revision+1);activeProjectionRevision=projectionRevision;displayed=false;}return new TargetStamp(targetId,revision,projectionRevision,target.Handle,Screen());}
  void Match(TargetStamp stamp){RequireTarget();if(stamp==null||stamp.Target!=targetId||stamp.Revision!=revision||stamp.ProjectionRevision!=activeProjectionRevision||stamp.Handle!=target.Handle||stamp.Screen!=Screen())throw new InvalidOperationException("presenter-stale-target");}
  public JObject Present(TargetStamp stamp,RuntimeFrame frame){
   Match(stamp);if(frame==null||frame.Identity.Attempt!=identity.Attempt||frame.Identity.Instance!=identity.Instance||frame.Identity.Generation!=identity.Generation||frame.BuildId!=build||frame.Width!=width||frame.Height!=height)throw new InvalidDataException("presenter-frame-identity");
   if(frame.ProjectionRevision!=stamp.ProjectionRevision)throw new InvalidDataException("presenter-projection-revision");long sequence=frame.Sequence;if(sequence<=lastSequence)throw new InvalidDataException("presenter-frame-sequence-or-size");
   if(session.State!=RuntimeSessionState.Running)throw new InvalidOperationException("presenter-session-ended");
   frame.CopyPixelWords(bgra);for(int i=0;i<bgra.Length;i++){uint p=(uint)bgra[i];bgra[i]=unchecked((int)(0xff000000u|(p&0x0000ff00u)|((p&0xffu)<<16)|((p>>16)&0xffu)));}JObject receipt=null;
   // Native painting stays outside the short final session commit.
   // Painting and mutation share this UI owner. The Bitmap wraps the pinned
   // words, so no second full-frame native copy is needed before submission.
   Match(stamp);
    displayedStamp=stamp;displayed=true;target.Refresh();
     if(!session.TryUseRunning(identity,()=>{Match(stamp);lastSequence=sequence;submittedStamp=stamp;presentedGeometryRevision=geometryRevision;receipt=new JObject{{"attempt",identity.Attempt.ToString("N")},{"instance",identity.Instance.ToString("N")},{"generation",identity.Generation},{"build",build},{"sequence",sequence},{"projection_revision",frame.ProjectionRevision},{"target",targetId.ToString("N")},{"target_revision",stamp.Revision},{"width",width},{"height",height},{"scope","owned-target-frame-submission"}};}))throw new InvalidOperationException("presenter-session-ended");

   return receipt;
  }
  public void Dispose(){if(disposed)return;if(Thread.CurrentThread.ManagedThreadId!=thread)throw new InvalidOperationException("presenter-owner-thread");bitmap.Dispose();bitmap=null;pixelsPin.Free();if(precisionTimer){timeEndPeriod(1);precisionTimer=false;}disposed=true;target.Resize-=changed;target.LocationChanged-=changed;target.HandleDestroyed-=changed;target.Paint-=painted;}
 }
}
