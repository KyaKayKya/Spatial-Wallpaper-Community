using System;using System.IO;using System.Threading;using System.Runtime.InteropServices;
namespace SpatialWallpaper.Sdk.Hosting {
 // Trusted bootstrap only. The reader never calls Unity or author code.
 public sealed class WindowsWorkerStopReader:IDisposable {
  IntPtr handle;readonly WorkerIdentity identity;readonly Thread reader;int closing,requested;Exception error;
  public bool Requested=>Volatile.Read(ref requested)!=0;
  public Exception Error=>Volatile.Read(ref error);
  public WindowsWorkerStopReader(IntPtr input,WorkerIdentity expected){if(input==IntPtr.Zero||input==new IntPtr(-1)||GetFileType(input)!=3)throw new ArgumentException("stop-read-pipe");identity=expected??throw new ArgumentNullException(nameof(expected));handle=input;try{reader=new Thread(Read){IsBackground=true,Name="SpatialWallpaper stop reader"};reader.Start();}catch{CloseHandle(handle);handle=IntPtr.Zero;throw;}}
  void Read(){try{var bytes=new byte[WorkerStopCodec.Bytes];int count=0;while(Volatile.Read(ref closing)==0){uint available;if(!PeekNamedPipe(handle,IntPtr.Zero,0,IntPtr.Zero,out available,IntPtr.Zero))throw new IOException("stop-pipe-closed");if(available>0){if(available>bytes.Length-count)throw new InvalidDataException("stop-extra-data");var part=new byte[available];uint got;if(!ReadFile(handle,part,available,out got,IntPtr.Zero)||got==0)throw new IOException("stop-read");Buffer.BlockCopy(part,0,bytes,count,(int)got);count+=(int)got;if(count==bytes.Length){WorkerStopCodec.Validate(bytes,identity);Volatile.Write(ref requested,1);return;}}else Thread.Sleep(2);}}catch(Exception caught){if(Volatile.Read(ref closing)==0)Volatile.Write(ref error,caught);}}
  public void Dispose(){Interlocked.Exchange(ref closing,1);if(reader!=null&&!reader.Join(1000))throw new InvalidOperationException("stop-reader-still-alive");var old=Interlocked.Exchange(ref handle,IntPtr.Zero);if(old!=IntPtr.Zero)CloseHandle(old);}
  [DllImport("kernel32.dll")]static extern uint GetFileType(IntPtr handle);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool PeekNamedPipe(IntPtr pipe,IntPtr buffer,uint length,IntPtr read,out uint available,IntPtr left);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool ReadFile(IntPtr pipe,byte[] buffer,uint length,out uint read,IntPtr overlapped);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
 }
}
