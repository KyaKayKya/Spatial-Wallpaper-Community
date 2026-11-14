using System;using System.IO;using System.Runtime.InteropServices;using System.Text;
namespace SpatialWallpaper.Sdk.Hosting {
 // This is an entry precondition, not the sandbox implementation. Supervisor creates the token/Job/desktop.
 public static class WindowsWorkerBoundary {
  [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
  [DllImport("kernel32.dll")]static extern uint SetErrorMode(uint mode);
  [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
  [DllImport("advapi32.dll",SetLastError=true)]static extern bool GetTokenInformation(IntPtr token,int information,out uint value,uint size,out uint needed);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool IsProcessInJob(IntPtr process,IntPtr job,out bool inJob);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessMitigationPolicy(IntPtr process,int policy,out uint flags,UIntPtr size);
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool QueryInformationJobObject(IntPtr job,int information,out uint value,uint size,IntPtr returned);
  [DllImport("kernel32.dll")]static extern uint GetFileType(IntPtr handle);
  [DllImport("user32.dll")]static extern IntPtr GetProcessWindowStation();
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool GetUserObjectInformationW(IntPtr value,int information,StringBuilder text,uint length,out uint needed);
  public static void Require(IntPtr frameWriter,string allPackagesCanary,string restrictedPackagesControl){
   if(Environment.OSVersion.Platform!=PlatformID.Win32NT||IntPtr.Size!=8)throw new PlatformNotSupportedException("Worker-platform-unverified");
   // No interactive hard-error/WER/OpenFile dialog from this hidden process or
   // a denied child launch. Failure still returns through the bounded channel.
   SetErrorMode(0x8003);
   IntPtr token;if(!OpenProcessToken(GetCurrentProcess(),8,out token))throw new InvalidOperationException("Worker-token-query");
   try{uint size,value;if(!GetTokenInformation(token,29,out value,4,out size)||value!=1)throw new InvalidOperationException("Worker-requires-AppContainer");}finally{CloseHandle(token);}
   // Win32 class 46 is rejected with ERROR_INVALID_PARAMETER on the verified Windows 10 host.
   // Prove the access-check distinction instead: ARAP control must open, AAP-only twin must deny.
   using(var control=new FileStream(restrictedPackagesControl,FileMode.Open,FileAccess.Read,FileShare.Read))if(control.Length!=1||control.ReadByte()!=83)throw new InvalidOperationException("Worker-LPAC-control");
   bool denied=false;try{using(var canary=new FileStream(allPackagesCanary,FileMode.Open,FileAccess.Read,FileShare.Read)){}}catch(UnauthorizedAccessException){denied=true;}
   if(!denied)throw new InvalidOperationException("Worker-requires-LPAC-access-check");
   bool inJob;if(!IsProcessInJob(GetCurrentProcess(),IntPtr.Zero,out inJob)||!inJob)throw new InvalidOperationException("Worker-requires-Job");
   uint flags;if(!QueryInformationJobObject(IntPtr.Zero,4,out flags,4,IntPtr.Zero)||flags!=255)throw new InvalidOperationException("Worker-Job-UI-policy");
   if(!GetProcessMitigationPolicy(GetCurrentProcess(),10,out flags,new UIntPtr(4))||(flags&3)!=3)throw new InvalidOperationException("Worker-image-load-policy");
   var name=new StringBuilder(512);uint needed;if(!GetUserObjectInformationW(GetProcessWindowStation(),2,name,1024,out needed)||string.Equals(name.ToString(),"WinSta0",StringComparison.OrdinalIgnoreCase))throw new InvalidOperationException("Worker-requires-private-station");
   if(frameWriter==IntPtr.Zero||frameWriter==new IntPtr(-1)||GetFileType(frameWriter)!=3)throw new InvalidOperationException("Worker-frame-pipe");
  }
 }
}
