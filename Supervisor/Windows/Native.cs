using System;using System.Text;using System.Runtime.InteropServices;
namespace SpatialWallpaper.Supervision.Windows {
 internal static class Native {
  [StructLayout(LayoutKind.Sequential,CharSet=CharSet.Unicode)]internal struct Startup {
   internal uint cb;internal string reserved,desktop,title;internal uint x,y,cx,cy,charsX,charsY,fill,flags;internal ushort show,reservedBytes;internal IntPtr reserved2,input,output,error;
  }
  [StructLayout(LayoutKind.Sequential)]internal struct StartupEx {internal Startup startup;internal IntPtr attributes;}
  [StructLayout(LayoutKind.Sequential)]internal struct ProcessInfo {internal IntPtr process,thread;internal uint pid,tid;}
  [StructLayout(LayoutKind.Sequential)]internal struct SecurityAttributes {internal int length;internal IntPtr descriptor;[MarshalAs(UnmanagedType.Bool)]internal bool inherit;}
  [StructLayout(LayoutKind.Sequential)]internal struct Capabilities {internal IntPtr sid,capabilities;internal uint count,reserved;}
  [StructLayout(LayoutKind.Sequential)]internal struct SidAttribute {internal IntPtr sid;internal uint attributes;}
  [StructLayout(LayoutKind.Sequential)]internal struct BasicLimits {internal long processTime,jobTime;internal uint flags;internal UIntPtr minimum,maximum;internal uint active;internal UIntPtr affinity;internal uint priority,scheduling;}
  [StructLayout(LayoutKind.Sequential)]internal struct Io {internal ulong a,b,c,d,e,f;}
  [StructLayout(LayoutKind.Sequential)]internal struct Limits {internal BasicLimits basic;internal Io io;internal UIntPtr processMemory,jobMemory,peakProcess,peakJob;}
  [StructLayout(LayoutKind.Sequential)]internal struct FileInformation {internal uint attributes,creationLow,creationHigh,accessLow,accessHigh,writeLow,writeHigh,volume,sizeHigh,sizeLow,links,indexHigh,indexLow;}
  [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool GetFileInformationByHandle(IntPtr file,out FileInformation information);
  [DllImport("userenv.dll",CharSet=CharSet.Unicode)]internal static extern int CreateAppContainerProfile(string name,string display,string description,IntPtr caps,uint count,out IntPtr sid);
  [DllImport("userenv.dll",CharSet=CharSet.Unicode)]internal static extern int DeleteAppContainerProfile(string name);
  [DllImport("userenv.dll",CharSet=CharSet.Unicode)]internal static extern int GetAppContainerFolderPath(string sid,out IntPtr path);
  [DllImport("advapi32.dll")]internal static extern IntPtr FreeSid(IntPtr sid);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern bool ConvertSidToStringSidW(IntPtr sid,out IntPtr text);
  [DllImport("kernelbase.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern bool DeriveCapabilitySidsFromName(string name,out IntPtr groups,out uint groupCount,out IntPtr values,out uint count);
  [DllImport("kernel32.dll")]internal static extern IntPtr LocalFree(IntPtr value);
  [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool CreatePipe(out IntPtr read,out IntPtr write,ref SecurityAttributes attributes,uint size);
  [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool SetHandleInformation(IntPtr handle,uint mask,uint flags);
  [DllImport("kernel32.dll",EntryPoint="WriteFile",SetLastError=true)]internal static extern bool WriteFilePointer(IntPtr handle,IntPtr buffer,uint bytes,out uint written,IntPtr overlapped);
  [DllImport("kernel32.dll",EntryPoint="ReadFile",SetLastError=true)]internal static extern bool ReadFilePointer(IntPtr handle,IntPtr buffer,uint bytes,out uint read,IntPtr overlapped);
  [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool ReadFile(IntPtr handle,byte[] buffer,uint bytes,out uint read,IntPtr overlapped);
  [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool CloseHandle(IntPtr value);
  [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool InitializeProcThreadAttributeList(IntPtr list,int count,uint flags,ref UIntPtr size);
  [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool UpdateProcThreadAttribute(IntPtr list,uint flags,UIntPtr attribute,IntPtr value,UIntPtr size,IntPtr previous,IntPtr returned);
  [DllImport("kernel32.dll")]internal static extern void DeleteProcThreadAttributeList(IntPtr list);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern bool CreateProcessW(string executable,StringBuilder command,IntPtr processAttributes,IntPtr threadAttributes,bool inherit,uint flags,IntPtr environment,string directory,ref StartupEx startup,out ProcessInfo info);
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern IntPtr CreateJobObjectW(IntPtr attributes,string name);
  [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool SetInformationJobObject(IntPtr job,int kind,IntPtr data,uint bytes);
  [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool TerminateJobObject(IntPtr job,uint code);
  [DllImport("kernel32.dll",SetLastError=true)]internal static extern uint ResumeThread(IntPtr thread);
  [DllImport("kernel32.dll",SetLastError=true)]internal static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
  [DllImport("kernel32.dll",SetLastError=true)]internal static extern bool GetExitCodeProcess(IntPtr handle,out uint code);
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern IntPtr CreateWindowStationW(string name,uint flags,uint access,IntPtr security);
  [DllImport("user32.dll")]internal static extern IntPtr GetProcessWindowStation();
  [DllImport("user32.dll",SetLastError=true)]internal static extern bool SetProcessWindowStation(IntPtr station);
  [DllImport("user32.dll",SetLastError=true)]internal static extern bool CloseWindowStation(IntPtr station);
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern bool GetUserObjectInformationW(IntPtr handle,int kind,StringBuilder data,uint bytes,out uint needed);
  [DllImport("user32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern IntPtr CreateDesktopW(string name,IntPtr device,IntPtr devmode,uint flags,uint access,ref SecurityAttributes security);
  [DllImport("user32.dll",SetLastError=true)]internal static extern bool CloseDesktop(IntPtr desktop);
  [DllImport("advapi32.dll",CharSet=CharSet.Unicode,SetLastError=true)]internal static extern bool ConvertStringSecurityDescriptorToSecurityDescriptorW(string text,uint revision,out IntPtr descriptor,IntPtr bytes);
 }
}
