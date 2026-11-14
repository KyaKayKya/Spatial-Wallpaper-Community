using System;using System.IO;using System.Runtime.InteropServices;
namespace SpatialWallpaper.Supervision.Windows {
 public sealed class WorkerWritePipe:IDisposable {
  IntPtr read,write;public IntPtr ChildWriter=>write;
  internal IntPtr ParentReader=>read;
  public WorkerWritePipe(string packageSid){
   IntPtr descriptor=IntPtr.Zero;
   try{
    string caller=System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
    PrivateStation.Check(Native.ConvertStringSecurityDescriptorToSecurityDescriptorW("D:P(A;;GA;;;"+caller+")(A;;GW;;;"+packageSid+")S:(ML;;NW;;;LW)",1,out descriptor,IntPtr.Zero));
    var attributes=new Native.SecurityAttributes{length=Marshal.SizeOf(typeof(Native.SecurityAttributes)),descriptor=descriptor,inherit=true};
    PrivateStation.Check(Native.CreatePipe(out read,out write,ref attributes,4096));PrivateStation.Check(Native.SetHandleInformation(read,1,0));
   }catch{Dispose();throw;}finally{if(descriptor!=IntPtr.Zero)Native.LocalFree(descriptor);}
  }
  public void CloseChildCopy(){if(write!=IntPtr.Zero){Native.CloseHandle(write);write=IntPtr.Zero;}}
  public int Read(byte[] bytes,int offset,int count){
   if(bytes==null||offset<0||count<1||offset>bytes.Length-count)throw new ArgumentException("pipe-read-buffer");
   var pin=GCHandle.Alloc(bytes,GCHandleType.Pinned);
   try{
    uint received;
    if(!Native.ReadFilePointer(read,IntPtr.Add(pin.AddrOfPinnedObject(),offset),(uint)Math.Min(count,65536),out received,IntPtr.Zero)){
     int code=Marshal.GetLastWin32Error();if(code==109)return 0;throw new System.ComponentModel.Win32Exception(code);
    }
    return checked((int)received);
   }finally{pin.Free();}
  }
  public bool ReadExact(byte[] bytes,bool allowEmptyEof=false){
   int offset=0;while(offset<bytes.Length){int n=Read(bytes,offset,bytes.Length-offset);if(n==0){if(offset==0&&allowEmptyEof)return false;throw new EndOfStreamException("partial-worker-message");}offset+=n;}return true;
  }
  public void Dispose(){CloseChildCopy();if(read!=IntPtr.Zero){Native.CloseHandle(read);read=IntPtr.Zero;}}
 }
}
