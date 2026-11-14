using System;
using System.IO;
using System.Runtime.InteropServices;
namespace SpatialTracking {
 public static class RuntimePaths {
  public static string ApplicationData=>RuntimeInformation.IsOSPlatform(OSPlatform.OSX)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Personal),"Library","Application Support"):Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
 }
 internal static class RuntimeDirectory {
  [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
  [DllImport("advapi32.dll",SetLastError=true)]static extern bool OpenProcessToken(IntPtr process,uint access,out IntPtr token);
  [DllImport("advapi32.dll",SetLastError=true)]static extern bool GetTokenInformation(IntPtr token,int kind,IntPtr buffer,uint size,out uint needed);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool ConvertSidToStringSid(IntPtr sid,out IntPtr text);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool ConvertStringSecurityDescriptorToSecurityDescriptor(string text,uint revision,out IntPtr descriptor,out uint size);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]static extern bool SetFileSecurity(string path,uint information,IntPtr descriptor);
  [DllImport("kernel32.dll")]static extern IntPtr LocalFree(IntPtr pointer);
  [DllImport("kernel32.dll")]static extern bool CloseHandle(IntPtr handle);
  [DllImport("/usr/lib/libSystem.B.dylib",SetLastError=true)]static extern int chmod(string path,uint mode);
  public static void RestrictToCurrentUser(string directory){
   // Fail closed if native ACL setup fails; never publish a connection token in
   // a directory whose access scope we could not establish.
   for(string p=Path.GetFullPath(directory);p!=null;p=Path.GetDirectoryName(p))if((File.Exists(p)||Directory.Exists(p))&&(File.GetAttributes(p)&FileAttributes.ReparsePoint)!=0)throw new IOException("runtime-directory-link");
   if(RuntimeInformation.IsOSPlatform(OSPlatform.OSX)){
    if(chmod(directory,Directory.Exists(directory)?448u:384u)!=0)throw new IOException("runtime-directory-permissions-failed");return;
   }
   if(!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))throw new PlatformNotSupportedException("Runtime directory protection");
   IntPtr token=IntPtr.Zero,buffer=IntPtr.Zero,sidText=IntPtr.Zero,descriptor=IntPtr.Zero;
   try{
    if(!OpenProcessToken(GetCurrentProcess(),8,out token))throw new IOException("runtime-token-query-failed");GetTokenInformation(token,1,IntPtr.Zero,0,out uint size);if(size<8||size>65536)throw new IOException("runtime-token-size");buffer=Marshal.AllocHGlobal((int)size);
    if(!GetTokenInformation(token,1,buffer,size,out _)||!ConvertSidToStringSid(Marshal.ReadIntPtr(buffer),out sidText))throw new IOException("runtime-user-query-failed");string sid=Marshal.PtrToStringUni(sidText);
    string sddl="D:P(A;OICI;FA;;;SY)(A;OICI;FA;;;"+sid+")";
    if(!ConvertStringSecurityDescriptorToSecurityDescriptor(sddl,1,out descriptor,out _)||!SetFileSecurity(directory,0x80000004,descriptor))throw new IOException("runtime-directory-acl-failed");
   }finally{if(descriptor!=IntPtr.Zero)LocalFree(descriptor);if(sidText!=IntPtr.Zero)LocalFree(sidText);if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);if(token!=IntPtr.Zero)CloseHandle(token);}
  }
 }
}
