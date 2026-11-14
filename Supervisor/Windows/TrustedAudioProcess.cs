using System;using System.IO;using System.Text;using System.Collections.Generic;using System.Runtime.InteropServices;using System.Globalization;using System.ComponentModel;
namespace SpatialWallpaper.Supervision.Windows {
 // Trusted fixed native helper, atomically placed in a one-process kill-on-close job.
 internal sealed class TrustedAudioProcess:IDisposable {
  readonly PrivateStation station;IntPtr job,process,desktop,read;FileStream image;bool registered,disposed;
  internal int ProcessId{get;private set;}internal long CreationFileTime{get;private set;}
  static IntPtr Pack<T>(T value,List<IntPtr> memory)where T:struct{var p=Marshal.AllocHGlobal(Marshal.SizeOf(typeof(T)));memory.Add(p);Marshal.StructureToPtr(value,p,false);return p;}
  public TrustedAudioProcess(PrivateStation owner,string executable,int pid,ulong creation,Action<Exception> cleanupFailure=null){
   station=owner??throw new ArgumentNullException(nameof(owner));if(station.Handle==IntPtr.Zero||pid<1||creation==0)throw new ArgumentException("audio-source");
   string exe=Path.GetFullPath(executable);if(exe.IndexOf('"')>=0)throw new ArgumentException("audio-image-path");
   IntPtr write=IntPtr.Zero,attributes=IntPtr.Zero,environment=IntPtr.Zero;bool ready=false;Native.ProcessInfo child=default;var memory=new List<IntPtr>();
   try{
    image=new FileStream(exe,FileMode.Open,FileAccess.Read,FileShare.Read);
    var security=new Native.SecurityAttributes{length=Marshal.SizeOf(typeof(Native.SecurityAttributes)),inherit=true};PrivateStation.Check(Native.CreatePipe(out read,out write,ref security,4096));PrivateStation.Check(Native.SetHandleInformation(read,1,0));
    security.inherit=false;string desktopName="SpatialWallpaper_Audio_"+Guid.NewGuid().ToString("N");desktop=Native.CreateDesktopW(desktopName,IntPtr.Zero,IntPtr.Zero,0,0x10000000,ref security);PrivateStation.Check(desktop!=IntPtr.Zero);
    job=Native.CreateJobObjectW(IntPtr.Zero,null);PrivateStation.Check(job!=IntPtr.Zero);
    var limits=new Native.Limits{basic=new Native.BasicLimits{flags=0x2000|8|0x100,active=1},processMemory=new UIntPtr(128*1024*1024)};
    PrivateStation.Check(Native.SetInformationJobObject(job,9,Pack(limits,memory),(uint)Marshal.SizeOf(typeof(Native.Limits))));
    UIntPtr size=UIntPtr.Zero;Native.InitializeProcThreadAttributeList(IntPtr.Zero,2,0,ref size);attributes=Marshal.AllocHGlobal(checked((int)size.ToUInt64()));PrivateStation.Check(Native.InitializeProcThreadAttributeList(attributes,2,0,ref size));ready=true;
    PrivateStation.Check(Native.UpdateProcThreadAttribute(attributes,0,new UIntPtr(0x20002),Pack(write,memory),new UIntPtr((uint)IntPtr.Size),IntPtr.Zero,IntPtr.Zero));
    PrivateStation.Check(Native.UpdateProcThreadAttribute(attributes,0,new UIntPtr(0x2000d),Pack(job,memory),new UIntPtr((uint)IntPtr.Size),IntPtr.Zero,IntPtr.Zero));
    string root=Environment.GetEnvironmentVariable("SystemRoot");if(string.IsNullOrEmpty(root))throw new InvalidOperationException("missing-system-root");environment=Marshal.StringToHGlobalUni("SystemRoot="+root+"\0WINDIR="+root+"\0\0");
    var startup=new Native.StartupEx{startup=new Native.Startup{cb=(uint)Marshal.SizeOf(typeof(Native.StartupEx)),desktop=station.Name+"\\"+desktopName,flags=0x100,output=write,error=write},attributes=attributes};
    var command=new StringBuilder("\""+exe+"\" "+pid.ToString(CultureInfo.InvariantCulture)+" "+creation.ToString(CultureInfo.InvariantCulture));
    PrivateStation.Check(Native.CreateProcessW(exe,command,IntPtr.Zero,IntPtr.Zero,true,0x80000|0x08000000|0x400,environment,Path.GetDirectoryName(exe),ref startup,out child));
    process=child.process;station.ChildCreated();registered=true;
    ProcessId=checked((int)child.pid);CreationFileTime=RestrictedProcess.ReadCreationFileTime(process);
   }catch{try{Dispose();}catch(Exception error){cleanupFailure?.Invoke(error);throw;}throw;}
   finally{if(child.thread!=IntPtr.Zero)Native.CloseHandle(child.thread);if(write!=IntPtr.Zero)Native.CloseHandle(write);if(ready)Native.DeleteProcThreadAttributeList(attributes);if(attributes!=IntPtr.Zero)Marshal.FreeHGlobal(attributes);if(environment!=IntPtr.Zero)Marshal.FreeHGlobal(environment);foreach(var p in memory)Marshal.FreeHGlobal(p);}
  }
  internal int Read(byte[] bytes,int offset,int count){
   var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);try{uint got;
    if(!Native.ReadFilePointer(read,IntPtr.Add(pin.AddrOfPinnedObject(),offset),(uint)count,out got,IntPtr.Zero)){int error=Marshal.GetLastWin32Error();if(error==109)return 0;throw new Win32Exception(error);}return checked((int)got);
   }finally{pin.Free();}
  }
  internal void Kill(){if(job!=IntPtr.Zero)PrivateStation.Check(Native.TerminateJobObject(job,125));}
  public void Dispose(){if(disposed)return;Kill();if(process!=IntPtr.Zero&&Native.WaitForSingleObject(process,1000)!=0)throw new InvalidOperationException("audio-process-stop-deadline");disposed=true;
   if(process!=IntPtr.Zero){Native.CloseHandle(process);process=IntPtr.Zero;}if(job!=IntPtr.Zero){Native.CloseHandle(job);job=IntPtr.Zero;}if(read!=IntPtr.Zero){Native.CloseHandle(read);read=IntPtr.Zero;}if(desktop!=IntPtr.Zero){Native.CloseDesktop(desktop);desktop=IntPtr.Zero;}image?.Dispose();if(registered){station.ChildReleased();registered=false;}
  }
 }
}
