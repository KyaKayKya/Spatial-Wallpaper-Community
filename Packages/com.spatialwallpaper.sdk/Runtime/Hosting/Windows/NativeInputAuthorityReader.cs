using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Linq;
using System.Threading;
using System.Runtime.InteropServices;
using System.ComponentModel;
using Microsoft.Win32.SafeHandles;
using SpatialWallpaper.Sdk.Capabilities;

namespace SpatialWallpaper.Sdk.Hosting {
 // Opened on the background input owner. Read performs bounded metadata reads
 // only: no RPC, named mutex, wait, lock acquisition or retry loop.
 public sealed class NativeInputAuthorityReader:IDisposable {
  readonly MemoryMappedFile section;readonly MemoryMappedViewAccessor view;readonly SafeWaitHandle host;
  [DllImport("kernel32.dll",SetLastError=true)]static extern SafeWaitHandle OpenProcess(uint access,bool inherit,int pid);
  [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForSingleObject(SafeWaitHandle handle,uint milliseconds);
  public NativeInputAuthorityReader(string name,WorkerIdentity identity,PinnedProcessOwner hostOwner,PinnedProcessOwner renderer){
   if(name==null||!name.StartsWith("Local\\SpatialWallpaper.NativeAuthority.",StringComparison.Ordinal)||name.Length!=71||!Guid.TryParseExact(name.Substring(39),"N",out var ignored))throw new InvalidDataException("native-authority-name");
   try{
    if(IntPtr.Size!=8)throw new PlatformNotSupportedException("native-authority-requires-aligned-64-bit-access");
    host=OpenProcess(0x101000,false,hostOwner.Id);if(host.IsInvalid)throw new Win32Exception(Marshal.GetLastWin32Error());
    if(PinnedProcessOwner.ReadCreationFileTime(host.DangerousGetHandle())!=hostOwner.CreationFileTime||WaitForSingleObject(host,0)!=258)throw new InvalidDataException("native-authority-host-ended");
    section=MemoryMappedFile.OpenExisting(name,MemoryMappedFileRights.Read,HandleInheritability.None);view=section.CreateViewAccessor(0,96,MemoryMappedFileAccess.Read);
    var attempt=new byte[16];var instance=new byte[16];view.ReadArray(40,attempt,0,16);view.ReadArray(56,instance,0,16);
    if(view.ReadInt32(0)!=0x3155414e||view.ReadInt32(4)!=1||view.ReadInt32(8)!=hostOwner.Id||view.ReadInt32(12)!=0||view.ReadInt64(16)!=hostOwner.CreationFileTime||view.ReadInt32(24)!=renderer.Id||view.ReadUInt32(28)!=identity.Generation||view.ReadInt64(32)!=renderer.CreationFileTime||!attempt.SequenceEqual(identity.Attempt.ToByteArray())||!instance.SequenceEqual(identity.Instance.ToByteArray()))throw new InvalidDataException("native-authority-owner");
   }catch{Dispose();throw;}
  }
  public InputAuthority Read(){
   try{
    ulong before=view.ReadUInt64(80);Thread.MemoryBarrier();ulong time=view.ReadUInt64(88);Thread.MemoryBarrier();ulong after=view.ReadUInt64(80);
    return before!=0&&before==after&&WaitForSingleObject(host,0)==258?new InputAuthority(before,time):default(InputAuthority);
   }catch(ObjectDisposedException){return default(InputAuthority);}
  }
  public void Dispose(){view?.Dispose();section?.Dispose();host?.Dispose();}
 }
}
