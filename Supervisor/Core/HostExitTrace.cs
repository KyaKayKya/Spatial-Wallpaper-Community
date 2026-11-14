using System;using System.IO;using System.IO.MemoryMappedFiles;using System.Diagnostics;using System.Threading;using System.Runtime.InteropServices;
namespace SpatialWallpaper.Supervision {
 // Fixed scalar diagnostics only. No monitor calls, pipe writes, exception text or user values.
 public enum HostExitPhase {ServiceDispose=1,RuntimeClose,RuntimeWait,RuntimeResult,GpuCloseRequest,StorageClose,StorageWait,LlmDispose,LlmWait,LlmImplicitDispose,AudioDispose,InputDispose,StationDispose,JournalClose,JournalWait,EndpointDelete,FailureReport,Main}
 public enum HostExitEvent {Begin=1,End,Fault,CloseRequested,Skipped,MainReturning,ForcedProcessExit}
 public sealed class HostExitTrace:IDisposable {
  public const uint Magic=0x53574558;public const int Schema=1,HeaderBytes=64,RecordBytes=32,Capacity=64,FileBytes=HeaderBytes+Capacity*RecordBytes;
  FileStream file;MemoryMappedFile mapping;MemoryMappedViewAccessor view;long publication;int count,dropped;readonly Action<Exception> cleanupFailure;readonly int owner=Thread.CurrentThread.ManagedThreadId;bool disabled;
  public bool DiagnosticFailed{get;private set;}
#if SDK_SUPERVISOR_TESTS
  public Action BeforeWrite;
#endif
  public HostExitTrace(string path,Action<Exception> cleanupFailure=null){this.cleanupFailure=cleanupFailure;try{if(File.Exists(path)&&(File.GetAttributes(path)&FileAttributes.ReparsePoint)!=0)throw new IOException("trace-link");file=new FileStream(path,FileMode.Create,FileAccess.ReadWrite,FileShare.ReadWrite|FileShare.Delete);file.SetLength(FileBytes);mapping=MemoryMappedFile.CreateFromFile(file,null,FileBytes,MemoryMappedFileAccess.ReadWrite,HandleInheritability.None,true);view=mapping.CreateViewAccessor(0,FileBytes,MemoryMappedFileAccess.ReadWrite);view.Write(0,Magic);view.Write(4,Schema);using(var process=Process.GetCurrentProcess()){long creation,exit,kernel,user;if(!GetProcessTimes(process.Handle,out creation,out exit,out kernel,out user))throw new IOException("trace-process-identity");view.Write(16,process.Id);view.Write(56,creation);}view.Write(20,Capacity);view.Write(32,Stopwatch.Frequency);view.Write(40,Stopwatch.GetTimestamp());view.Write(52,owner);}catch{DiagnosticFailed=true;disabled=true;Dispose();}}
  public void Mark(HostExitPhase phase,HostExitEvent kind,int code=0){try{if(disabled||view==null)return;if(Thread.CurrentThread.ManagedThreadId!=owner)throw new InvalidOperationException("trace-owner");long next=checked(publication+2);view.Write(8,next-1);Thread.MemoryBarrier();
#if SDK_SUPERVISOR_TESTS
   BeforeWrite?.Invoke();
#endif
   if(count<Capacity){long at=HeaderBytes+count*RecordBytes;view.Write(at,(long)count+1);view.Write(at+8,Stopwatch.GetTimestamp());view.Write(at+16,(int)phase);view.Write(at+20,(int)kind);view.Write(at+24,code);view.Write(at+28,0);count++;}else dropped=checked(dropped+1);view.Write(24,count);view.Write(28,dropped);Thread.MemoryBarrier();view.Write(8,next);publication=next;
  }catch{DiagnosticFailed=true;disabled=true;try{view?.Write(48,1);}catch{}}}
  public void Run(HostExitPhase phase,Action action,HostExitEvent success=HostExitEvent.End){Mark(phase,HostExitEvent.Begin);try{action();}catch(Exception error){if(phase!=HostExitPhase.FailureReport&&phase!=HostExitPhase.Main)cleanupFailure?.Invoke(error);Mark(phase,HostExitEvent.Fault,error.HResult);throw;}Mark(phase,success);}
  public IDisposable Track(IDisposable resource,HostExitPhase phase)=>new Tracked(this,resource,phase);
  sealed class Tracked:IDisposable {readonly HostExitTrace trace;readonly IDisposable resource;readonly HostExitPhase phase;internal Tracked(HostExitTrace t,IDisposable r,HostExitPhase p){trace=t;resource=r;phase=p;}public void Dispose(){if(resource==null)trace.Mark(phase,HostExitEvent.Skipped);else trace.Run(phase,resource.Dispose);}}
  public void Dispose(){disabled=true;try{view?.Dispose();}catch{DiagnosticFailed=true;}finally{view=null;}try{mapping?.Dispose();}catch{DiagnosticFailed=true;}finally{mapping=null;}try{file?.Dispose();}catch{DiagnosticFailed=true;}finally{file=null;}}
  [DllImport("kernel32.dll",SetLastError=true)]static extern bool GetProcessTimes(IntPtr process,out long creation,out long exit,out long kernel,out long user);
 }
}
