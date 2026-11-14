using System;using System.Threading;using System.Runtime.InteropServices;using SpatialWallpaper.Sdk.Execution;
namespace SpatialWallpaper.Sdk.Hosting {
 // Trusted Worker transport. It accepts an inherited write handle, never an author path or URL.
 public sealed class WindowsFrameWriter:IDisposable {
  readonly FrameMailbox mailbox;readonly WorkerIdentity identity;readonly int width,height;readonly ResourceLease memory;readonly Thread thread;
  IntPtr pipe;int error;
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool WriteFile(IntPtr file,IntPtr bytes,uint count,out uint written,IntPtr overlapped);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr value);
  public int Error=>Interlocked.CompareExchange(ref error,0,0);
  public long LastSent=>mailbox.LastSent;public long Dropped=>mailbox.Dropped;
  public WindowsFrameWriter(ResourceLedger ledger,WorkerIdentity session,int pixelWidth,int pixelHeight,IntPtr inheritedWriteHandle){
   if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("Windows-frame-transport-only");
   if(session==null||inheritedWriteHandle==IntPtr.Zero||inheritedWriteHandle==new IntPtr(-1))throw new ArgumentException("worker-frame-handle");
   identity=session;width=pixelWidth;height=pixelHeight;WorkerFrameCodec.PayloadBytes(width,height);pipe=inheritedWriteHandle;
   memory=ledger.Reserve(new ResourceCost(16384,256*1024,0,1));
   try{mailbox=new FrameMailbox(ledger,width,height,1);thread=new Thread(SendLoop){IsBackground=true,Name="SDK frame writer"};memory.Commit();thread.Start();}
   catch{mailbox?.Close();memory.Dispose();if(pipe!=IntPtr.Zero){CloseHandle(pipe);pipe=IntPtr.Zero;}throw;}
  }
  ~WindowsFrameWriter(){memory?.Dispose();}
  public FrameSlot AcquireCapture()=>mailbox.AcquireLatestCapture();
  public void CancelCapture(FrameSlot slot)=>mailbox.CancelCapture(slot);
  public void Publish(FrameSlot slot,long sequence,long projectionRevision=0)=>mailbox.Publish(slot,sequence,projectionRevision);
  void Send(byte[] bytes){
   var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
   try{
    int offset=0;
    while(offset<bytes.Length){
     uint written;uint count=(uint)Math.Min(65536,bytes.Length-offset);
     if(!WriteFile(pipe,IntPtr.Add(pin.AddrOfPinnedObject(),offset),count,out written,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());
     if(written==0||written>count)throw new InvalidOperationException("frame-write-progress");offset+=(int)written;
    }
   }finally{pin.Free();}
  }
  void SendLoop(){
   try{
    var header=new byte[WorkerFrameCodec.HeaderBytes];FrameSlot slot;
    while((slot=mailbox.Take())!=null){
     WorkerFrameCodec.Encode(header,identity,width,height,slot.Sequence,slot.ProjectionRevision);Send(header);Send(slot.Pixels);mailbox.Sent(slot);GC.KeepAlive(slot);
    }
   }catch(System.ComponentModel.Win32Exception failure){Interlocked.Exchange(ref error,failure.NativeErrorCode==0?1:failure.NativeErrorCode);mailbox.Close();}
   catch{Interlocked.Exchange(ref error,1);mailbox.Close();}
   finally{if(pipe!=IntPtr.Zero){CloseHandle(pipe);pipe=IntPtr.Zero;}}
  }
  // Never wait on a potentially blocked pipe from the render/UI thread. Supervisor enforces the stop deadline.
  public void Dispose(){mailbox.Close();}
 }
}
