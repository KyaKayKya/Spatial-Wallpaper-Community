using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Runtime.InteropServices;

// Test-only failure injection. Own a duplicate kernel handle, never reopen a PID.
// The production Job must terminate the paused helper before a passing assertion.
sealed class OwnedHelperPause:IDisposable {
 IntPtr handle;
 [DllImport("kernel32.dll")]static extern IntPtr GetCurrentProcess();
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool DuplicateHandle(IntPtr from,IntPtr source,IntPtr to,out IntPtr target,uint access,bool inherit,uint options);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool CloseHandle(IntPtr handle);
 [DllImport("kernel32.dll",SetLastError=true)]static extern uint WaitForSingleObject(IntPtr handle,uint milliseconds);
 [DllImport("kernel32.dll",SetLastError=true)]static extern bool TerminateProcess(IntPtr handle,uint exitCode);
 [DllImport("ntdll.dll")]static extern int NtSuspendProcess(IntPtr handle);
 [DllImport("ntdll.dll")]static extern int NtResumeProcess(IntPtr handle);
 public bool Exited {get{uint value=WaitForSingleObject(handle,0);if(value!=0&&value!=258)throw new Win32Exception(Marshal.GetLastWin32Error());return value==0;}}
 public OwnedHelperPause(Process owned){
  if(!DuplicateHandle(GetCurrentProcess(),owned.Handle,GetCurrentProcess(),out handle,0,false,2))throw new Win32Exception(Marshal.GetLastWin32Error());
  try{if(Exited)throw new InvalidOperationException("helper-exited-before-pause");int status=NtSuspendProcess(handle);if(status<0)throw new InvalidOperationException("helper-suspend-status-"+status);}catch{Dispose();throw;}
 }
 public void Dispose(){if(handle==IntPtr.Zero)return;try{if(!Exited){NtResumeProcess(handle);if(!TerminateProcess(handle,223)&&!Exited)throw new Win32Exception(Marshal.GetLastWin32Error());if(WaitForSingleObject(handle,3000)!=0)throw new InvalidOperationException("paused-helper-fallback-exit-timeout");}}finally{CloseHandle(handle);handle=IntPtr.Zero;}}
}
