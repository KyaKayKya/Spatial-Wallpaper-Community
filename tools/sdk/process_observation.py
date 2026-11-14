"""Observe only named children of a test-owned process, retaining kernel identity."""
import ctypes,time
from ctypes import wintypes as w
k=ctypes.WinDLL('kernel32',use_last_error=True)
class Entry(ctypes.Structure):
 _fields_=[('size',w.DWORD),('usage',w.DWORD),('pid',w.DWORD),('heap',ctypes.c_size_t),('module',w.DWORD),('threads',w.DWORD),('parent',w.DWORD),('priority',w.LONG),('flags',w.DWORD),('name',w.WCHAR*260)]
k.CreateToolhelp32Snapshot.argtypes=[w.DWORD,w.DWORD];k.CreateToolhelp32Snapshot.restype=w.HANDLE
k.Process32FirstW.argtypes=[w.HANDLE,ctypes.POINTER(Entry)];k.Process32FirstW.restype=w.BOOL
k.Process32NextW.argtypes=[w.HANDLE,ctypes.POINTER(Entry)];k.Process32NextW.restype=w.BOOL
k.CloseHandle.argtypes=[w.HANDLE];k.CloseHandle.restype=w.BOOL
k.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD];k.OpenProcess.restype=w.HANDLE
k.WaitForSingleObject.argtypes=[w.HANDLE,w.DWORD];k.WaitForSingleObject.restype=w.DWORD
k.TerminateProcess.argtypes=[w.HANDLE,w.UINT];k.TerminateProcess.restype=w.BOOL
k.GetProcessTimes.argtypes=[w.HANDLE,*([ctypes.POINTER(ctypes.c_ulonglong)]*4)];k.GetProcessTimes.restype=w.BOOL
def children(parent,name):
 snapshot=k.CreateToolhelp32Snapshot(2,0)
 if snapshot==ctypes.c_void_p(-1).value:raise ctypes.WinError(ctypes.get_last_error())
 try:
  entry=Entry();entry.size=ctypes.sizeof(Entry);more=k.Process32FirstW(snapshot,ctypes.byref(entry));found=[]
  while more:
   if entry.parent==parent and entry.name.casefold()==name.casefold():found.append(entry.pid)
   more=k.Process32NextW(snapshot,ctypes.byref(entry))
  return found
 finally:k.CloseHandle(snapshot)
class ObservedProcess:
 def __init__(self,pid,allow_terminate=False,allow_suspend=False):
  self.pid=pid;self.allow_terminate=allow_terminate;self.allow_suspend=allow_suspend;self.handle=k.OpenProcess(0x100000|0x1000|(1 if allow_terminate else 0)|(0x800 if allow_suspend else 0),False,pid)
  if not self.handle:raise ctypes.WinError(ctypes.get_last_error())
 def terminate(self):
  if not self.allow_terminate:raise PermissionError('observation-only process handle')
  if not k.TerminateProcess(self.handle,99):raise ctypes.WinError(ctypes.get_last_error())
 def exited(self,timeout=0):return k.WaitForSingleObject(self.handle,int(timeout*1000))==0
 def suspend(self):
  if not self.allow_suspend:raise PermissionError('observation-only process handle')
  native=ctypes.WinDLL('ntdll');native.NtSuspendProcess.argtypes=[w.HANDLE];native.NtSuspendProcess.restype=ctypes.c_long
  status=native.NtSuspendProcess(self.handle)
  if status<0:raise OSError('NtSuspendProcess failed: '+hex(status&0xffffffff))
 def cpu_ms(self):
  values=[ctypes.c_ulonglong() for _ in range(4)]
  if not k.GetProcessTimes(self.handle,*[ctypes.byref(v) for v in values]):raise ctypes.WinError(ctypes.get_last_error())
  return (values[2].value+values[3].value)/10000
 def close(self):
  if self.handle:k.CloseHandle(self.handle);self.handle=None
def observe_child(parent,name,timeout=3,allow_terminate=False,allow_suspend=False):
 deadline=time.monotonic()+timeout
 while time.monotonic()<deadline:
  matches=children(parent,name)
  if len(matches)>1:raise AssertionError('multiple test-owned '+name)
  if matches:return ObservedProcess(matches[0],allow_terminate,allow_suspend)
  time.sleep(.01)
 raise TimeoutError('test-owned child not found: '+name)
