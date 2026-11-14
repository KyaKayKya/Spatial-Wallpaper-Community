using System;using System.ComponentModel;using System.Diagnostics;using System.IO;using System.Runtime.InteropServices;
namespace SpatialWallpaper.Sdk.Hosting {
 // Trusted UI/bootstrap ownership only; not part of the author binding catalog.
 // Retaining the original process handle prevents PID reuse from changing owner.
 public sealed class PinnedProcessOwner:IDisposable {
  readonly object gate=new object();Process process;public int Id{get;}public long CreationFileTime{get;}
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr handle,out long creation,out long exit,out long kernel,out long user);
  public static long ReadCreationFileTime(IntPtr handle){if(Environment.OSVersion.Platform!=PlatformID.Win32NT)throw new PlatformNotSupportedException("process-owner-Windows-only");if(!GetProcessTimes(handle,out long created,out long exit,out long kernel,out long user))throw new Win32Exception(Marshal.GetLastWin32Error());if(created<=0)throw new InvalidDataException("process-owner-creation-time");return created;}
  PinnedProcessOwner(Process process,long expected){try{int id=process.Id;long created=ReadCreationFileTime(process.Handle);if(created!=expected||process.HasExited)throw new InvalidDataException("process-owner-identity-or-exit");Id=id;CreationFileTime=created;this.process=process;}catch{process.Dispose();throw;}}
  PinnedProcessOwner(Process original,int id,long created){Id=id;CreationFileTime=created;process=original;}
  // Takes ownership ONLY on success. Unlike Open, retaining an already exited
  // original is valid: its kernel handle is useful as final exit evidence.
  public static PinnedProcessOwner RetainOriginal(Process original,long expected){
   if(original==null||expected<=0)throw new ArgumentException("process-owner-original");int id=original.Id;long actual=ReadCreationFileTime(original.Handle);
   if(actual!=expected)throw new InvalidDataException("process-owner-original-identity");return new PinnedProcessOwner(original,id,actual);
  }
  public static PinnedProcessOwner Open(int id,long created){if(id<=0||created<=0)throw new ArgumentException("process-owner-identity");return new PinnedProcessOwner(Process.GetProcessById(id),created);}
  public static PinnedProcessOwner PinCurrent(){var process=Process.GetCurrentProcess();try{return new PinnedProcessOwner(process,ReadCreationFileTime(process.Handle));}catch{process.Dispose();throw;}}
  public bool HasExited{get{lock(gate){if(process==null)throw new ObjectDisposedException(nameof(PinnedProcessOwner));return process.HasExited;}}}
  public int ExitCode{get{lock(gate){if(process==null)throw new ObjectDisposedException(nameof(PinnedProcessOwner));return process.ExitCode;}}}
  public void Dispose(){lock(gate){process?.Dispose();process=null;}}
 }
}
