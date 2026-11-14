using System;using System.IO;using System.Runtime.InteropServices;
namespace SpatialWallpaper.Supervision.Windows {
 // Supervisor writes; the isolated Worker receives only a read handle.
 public sealed class WorkerReadPipe:IDisposable {
  IntPtr read,write;public IntPtr ChildReader=>read;
  internal IntPtr ParentWriter=>write;
  public WorkerReadPipe(string packageSid){
   IntPtr descriptor=IntPtr.Zero;try{
    string caller=System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
    PrivateStation.Check(Native.ConvertStringSecurityDescriptorToSecurityDescriptorW("D:P(A;;GA;;;"+caller+")(A;;GR;;;"+packageSid+")S:(ML;;NW;;;LW)",1,out descriptor,IntPtr.Zero));
    var attributes=new Native.SecurityAttributes{length=Marshal.SizeOf(typeof(Native.SecurityAttributes)),descriptor=descriptor,inherit=true};PrivateStation.Check(Native.CreatePipe(out read,out write,ref attributes,4096));PrivateStation.Check(Native.SetHandleInformation(write,1,0));
   }catch{Dispose();throw;}finally{if(descriptor!=IntPtr.Zero)Native.LocalFree(descriptor);}
  }
  public void CloseChildCopy(){if(read!=IntPtr.Zero){Native.CloseHandle(read);read=IntPtr.Zero;}}
  public void Write(byte[] bytes,int offset,int length){
   if(bytes==null||offset<0||length<1||offset>bytes.Length-length)throw new ArgumentException("pipe-write-buffer");var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
   try{int at=0;while(at<length){uint wrote;if(!Native.WriteFilePointer(write,IntPtr.Add(pin.AddrOfPinnedObject(),offset+at),(uint)(length-at),out wrote,IntPtr.Zero))throw new System.ComponentModel.Win32Exception(Marshal.GetLastWin32Error());if(wrote==0)throw new IOException("pipe-empty-write");at+=checked((int)wrote);}}finally{pin.Free();}
  }
  public void Dispose(){CloseChildCopy();if(write!=IntPtr.Zero){Native.CloseHandle(write);write=IntPtr.Zero;}}
 }
}
