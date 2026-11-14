using System;using System.IO;using System.Threading;using System.Diagnostics;using System.Runtime.InteropServices;
namespace SpatialWallpaper.Sdk.Hosting {
 // Dedicated inherited read-only lane. It never invokes Unity or author code.
 public sealed class WindowsWorkerProjectionReader:IDisposable {
  IntPtr handle;readonly WorkerIdentity identity;readonly Thread reader;readonly object gate=new object();ProjectionConfiguration pending;int closing;Exception error;
  public Exception Error=>Volatile.Read(ref error);
  public WindowsWorkerProjectionReader(IntPtr input,WorkerIdentity expected){if(input==IntPtr.Zero||input==new IntPtr(-1)||GetFileType(input)!=3)throw new ArgumentException("projection-read-pipe");identity=expected??throw new ArgumentNullException(nameof(expected));handle=input;try{reader=new Thread(Read){IsBackground=true,Name="SpatialWallpaper projection reader"};reader.Start();}catch{CloseHandle(handle);handle=IntPtr.Zero;throw;}}
  public ProjectionConfiguration Take(){lock(gate){var value=pending;pending=null;return value;}}
  void Read(){var bytes=new byte[WorkerProjectionCodec.Bytes];var clock=Stopwatch.StartNew();int count=0;long began=-1,revision=0;var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);try{
   while(Volatile.Read(ref closing)==0){
    uint available;if(!PeekNamedPipe(handle,IntPtr.Zero,0,IntPtr.Zero,out available,IntPtr.Zero))throw new IOException("projection-pipe-closed");
    if(available>0){if(count==0)began=clock.ElapsedMilliseconds;uint length=(uint)Math.Min(available,bytes.Length-count),got;
     if(!ReadFile(handle,IntPtr.Add(pin.AddrOfPinnedObject(),count),length,out got,IntPtr.Zero)||got==0)throw new IOException("projection-read");count+=checked((int)got);
     if(count==bytes.Length){var value=WorkerProjectionCodec.Decode(bytes,identity,revision);revision=value.Revision;lock(gate)pending=value;count=0;began=-1;}
    }else{if(count>0&&clock.ElapsedMilliseconds-began>=RuntimeLimits.ManagerIoMs)throw new TimeoutException("projection-partial-deadline");Thread.Sleep(2);}
   }
  }catch(Exception caught){if(Volatile.Read(ref closing)==0)Volatile.Write(ref error,caught);}finally{pin.Free();}}
  public void Dispose(){Interlocked.Exchange(ref closing,1);if(reader!=null&&!reader.Join(1000))throw new InvalidOperationException("projection-reader-still-alive");var old=Interlocked.Exchange(ref handle,IntPtr.Zero);if(old!=IntPtr.Zero)CloseHandle(old);lock(gate)pending=null;}
  [DllImport("kernel32.dll")]static extern uint GetFileType(IntPtr handle);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool PeekNamedPipe(IntPtr pipe,IntPtr buffer,uint length,IntPtr read,out uint available,IntPtr left);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadFile(IntPtr pipe,IntPtr bytes,uint length,out uint read,IntPtr overlapped);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 }
}
