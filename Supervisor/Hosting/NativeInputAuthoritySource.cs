using System;
using System.IO;
using System.IO.MemoryMappedFiles;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using SpatialWallpaper.Sdk.Hosting;

namespace SpatialWallpaper.Supervision.Windows {
 // Sole writer, called under the original Broker lock. Current-SID section is
 // not inherited by the LPAC script; renderer receives a read-only view.
 internal sealed class NativeInputAuthoritySource:IDisposable {
  readonly MemoryMappedFile section;readonly MemoryMappedViewAccessor view;
  internal readonly string Name="Local\\SpatialWallpaper.NativeAuthority."+Guid.NewGuid().ToString("N");
  internal NativeInputAuthoritySource(WorkerIdentity identity,PinnedProcessOwner renderer){
   var security=new MemoryMappedFileSecurity();security.SetAccessRuleProtection(true,false);security.AddAccessRule(new AccessRule<MemoryMappedFileRights>(WindowsIdentity.GetCurrent().User,MemoryMappedFileRights.FullControl,AccessControlType.Allow));
   try{
    section=MemoryMappedFile.CreateNew(Name,96,MemoryMappedFileAccess.ReadWrite,MemoryMappedFileOptions.None,security,HandleInheritability.None);view=section.CreateViewAccessor(0,96,MemoryMappedFileAccess.ReadWrite);
    using(var host=PinnedProcessOwner.PinCurrent()){
     view.Write(0,0x3155414e);view.Write(4,1);view.Write(8,host.Id);view.Write(16,host.CreationFileTime);
    }
    view.Write(24,renderer.Id);view.Write(28,identity.Generation);view.Write(32,renderer.CreationFileTime);view.WriteArray(40,identity.Attempt.ToByteArray(),0,16);view.WriteArray(56,identity.Instance.ToByteArray(),0,16);
   }catch{Dispose();throw;}
  }
  internal void Publish(ulong epoch,long time){
   if(epoch==0){view.Write(80,0UL);Thread.MemoryBarrier();view.Write(88,0L);}
   else{view.Write(88,time);Thread.MemoryBarrier();view.Write(80,epoch);}
   Thread.MemoryBarrier();
  }
  public void Dispose(){view?.Dispose();section?.Dispose();}
 }
}
