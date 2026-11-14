"""Outer ownership for deliberate missing-inner-Job negative tests."""
import ctypes as c,time
from ctypes import wintypes as w
class Basic(c.Structure):_fields_=[('process_time',c.c_longlong),('job_time',c.c_longlong),('flags',w.DWORD),('minimum',c.c_size_t),('maximum',c.c_size_t),('active',w.DWORD),('affinity',c.c_size_t),('priority',w.DWORD),('scheduling',w.DWORD)]
class Io(c.Structure):_fields_=[('values',c.c_ulonglong*6)]
class Limits(c.Structure):_fields_=[('basic',Basic),('io',Io),('process_memory',c.c_size_t),('job_memory',c.c_size_t),('peak_process',c.c_size_t),('peak_job',c.c_size_t)]
class Accounting(c.Structure):_fields_=[('times',c.c_longlong*4),('faults',w.DWORD),('total',w.DWORD),('active',w.DWORD),('terminated',w.DWORD)]
class ProbeJob:
 def __init__(self):
  self.k=c.WinDLL('kernel32',use_last_error=True);k=self.k
  k.CreateJobObjectW.argtypes=[c.c_void_p,w.LPCWSTR];k.CreateJobObjectW.restype=w.HANDLE
  k.SetInformationJobObject.argtypes=[w.HANDLE,c.c_int,c.c_void_p,w.DWORD];k.SetInformationJobObject.restype=w.BOOL
  k.AssignProcessToJobObject.argtypes=[w.HANDLE,w.HANDLE];k.AssignProcessToJobObject.restype=w.BOOL
  k.TerminateJobObject.argtypes=[w.HANDLE,w.UINT];k.TerminateJobObject.restype=w.BOOL
  k.QueryInformationJobObject.argtypes=[w.HANDLE,c.c_int,c.c_void_p,w.DWORD,c.c_void_p];k.QueryInformationJobObject.restype=w.BOOL
  k.CloseHandle.argtypes=[w.HANDLE]
  self.handle=k.CreateJobObjectW(None,None)
  if not self.handle:raise c.WinError(c.get_last_error())
  limits=Limits();limits.basic.flags=0x2000
  if not k.SetInformationJobObject(self.handle,9,c.byref(limits),c.sizeof(limits)):
   error=c.get_last_error();k.CloseHandle(self.handle);self.handle=None;raise c.WinError(error)
 def assign(self,process):
  if not self.k.AssignProcessToJobObject(self.handle,int(process._handle)):raise c.WinError(c.get_last_error())
 def close(self):
  if not self.handle:return
  try:
   if not self.k.TerminateJobObject(self.handle,1):raise c.WinError(c.get_last_error())
   deadline=time.monotonic()+5
   while True:
    accounting=Accounting()
    if not self.k.QueryInformationJobObject(self.handle,1,c.byref(accounting),c.sizeof(accounting),None):raise c.WinError(c.get_last_error())
    if accounting.active==0:return
    if time.monotonic()>=deadline:raise RuntimeError('outer-probe-job-still-active')
    time.sleep(.01)
  finally:self.k.CloseHandle(self.handle);self.handle=None
