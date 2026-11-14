using System;using System.IO;using System.Runtime.InteropServices;using System.Diagnostics;
namespace SpatialWallpaper.Supervision.Windows {
 // This bounded signal lane is a supervision fixture. Production frames require the bounded pixel protocol.
 public sealed class FrameSignalPipe:IDisposable {
  IntPtr read,write;readonly byte[] packet=new byte[16],scratch=new byte[16];readonly Stopwatch rate=Stopwatch.StartNew();int packets;
  public IntPtr ChildWriter=>write;
  public FrameSignalPipe(string packageSid){
   IntPtr descriptor=IntPtr.Zero;
   try{
    string caller=System.Security.Principal.WindowsIdentity.GetCurrent().User.Value;
    PrivateStation.Check(Native.ConvertStringSecurityDescriptorToSecurityDescriptorW("D:P(A;;GA;;;"+caller+")(A;;GW;;;"+packageSid+")S:(ML;;NW;;;LW)",1,out descriptor,IntPtr.Zero));
    var attributes=new Native.SecurityAttributes{length=Marshal.SizeOf(typeof(Native.SecurityAttributes)),descriptor=descriptor,inherit=true};
    PrivateStation.Check(Native.CreatePipe(out read,out write,ref attributes,4096));PrivateStation.Check(Native.SetHandleInformation(read,1,0));
   }catch{Dispose();throw;}finally{if(descriptor!=IntPtr.Zero)Native.LocalFree(descriptor);}
  }
  public void CloseChildCopy(){if(write!=IntPtr.Zero){Native.CloseHandle(write);write=IntPtr.Zero;}}
  public bool Receive(out bool frame,out long sequence){
   frame=false;sequence=0;int offset=0;
   while(offset<16){
    uint count;if(!Native.ReadFile(read,scratch,(uint)(16-offset),out count,IntPtr.Zero)){
     int code=Marshal.GetLastWin32Error();if(code==109)return false;throw new System.ComponentModel.Win32Exception(code);
    }
    if(count==0)return false;Buffer.BlockCopy(scratch,0,packet,offset,(int)count);offset+=(int)count;
   }
   if(rate.ElapsedMilliseconds>=1000){rate.Restart();packets=0;}
   if(++packets>256)throw new InvalidDataException("signal-lane-rate");
   uint magic=BitConverter.ToUInt32(packet,0),kind=BitConverter.ToUInt32(packet,4);sequence=BitConverter.ToInt64(packet,8);
   if(magic!=0x53575331||kind!=1&&kind!=2||kind==1&&sequence<1||kind==2&&sequence!=0)throw new InvalidDataException("signal-lane-packet");
   frame=kind==1;return true;
  }
  public void Dispose(){CloseChildCopy();if(read!=IntPtr.Zero){Native.CloseHandle(read);read=IntPtr.Zero;}}
 }
}
