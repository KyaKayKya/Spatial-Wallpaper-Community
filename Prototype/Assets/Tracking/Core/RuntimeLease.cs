using System;
using System.IO;
using System.Runtime.InteropServices;
namespace SpatialTracking {
 // FileShare is enforced by Windows; flock supplies an explicit cross-process
 // lifetime lock on macOS Mono. Never unlink a lock file while owners may exist.
 public sealed class RuntimeLease:IDisposable {
  public FileStream Stream{get;private set;}
  [DllImport("/usr/lib/libSystem.B.dylib",SetLastError=true)]static extern int flock(int fd,int operation);
  RuntimeLease(FileStream stream){Stream=stream;}
  public static RuntimeLease TryAcquire(string file){
   FileStream stream;
   try{stream=new FileStream(file,FileMode.OpenOrCreate,FileAccess.ReadWrite,FileShare.Read);}
   catch(IOException e){int code=e.HResult&0xffff;if(code==32||code==33)return null;throw;}
   if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX)&&flock(stream.SafeFileHandle.DangerousGetHandle().ToInt32(),2|4)!=0){int error=Marshal.GetLastWin32Error();stream.Dispose();if(error==35)return null;throw new IOException("runtime-lock-failed: "+error);}
   return new RuntimeLease(stream);
  }
  public void Dispose(){Stream?.Dispose();Stream=null;}
 }
}
