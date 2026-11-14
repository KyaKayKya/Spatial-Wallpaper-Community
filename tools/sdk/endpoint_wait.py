"""Bounded discovery of a newly published private backend endpoint; never grants access or starts content."""
import json,time,errno,os,sys

def open_endpoint(path):
 # A poller must allow the Host to atomically replace the stale descriptor.
 if sys.platform!='win32':return open(path,'rb')
 import ctypes,msvcrt
 from ctypes import wintypes
 kernel=ctypes.WinDLL('kernel32',use_last_error=True)
 create=kernel.CreateFileW;create.argtypes=[wintypes.LPCWSTR,wintypes.DWORD,wintypes.DWORD,ctypes.c_void_p,wintypes.DWORD,wintypes.DWORD,wintypes.HANDLE];create.restype=wintypes.HANDLE
 handle=create(os.fspath(path),0x80000000,7,None,3,0x80,None)
 if handle==ctypes.c_void_p(-1).value:raise ctypes.WinError(ctypes.get_last_error())
 try:fd=msvcrt.open_osfhandle(handle,os.O_RDONLY|os.O_BINARY)
 except BaseException:
  kernel.CloseHandle.argtypes=[wintypes.HANDLE];kernel.CloseHandle(handle);raise
 return os.fdopen(fd,'rb')

def read_endpoint(path):
 with open_endpoint(path) as stream:return json.load(stream)
def wait_endpoint(read,alive,previous,timeout,clock=time.monotonic,sleep=time.sleep,events=None):
 deadline=clock()+timeout
 while clock()<deadline:
  if not alive():raise RuntimeError('backend exited before publishing endpoint')
  try:
   value=read()
   if value['auth']!=previous:return value
  except (FileNotFoundError,json.JSONDecodeError):pass
  except PermissionError as error:
   # Windows replacement can hold an exclusive/delete-pending file briefly. A permanent denial still times out.
   code=getattr(error,'winerror',None)
   if code not in (5,32,33) and not(code is None and error.errno==errno.EACCES):raise
   if events is not None and len(events)<128:events.append(code if code is not None else error.errno)
  sleep(.02)
 raise TimeoutError('backend endpoint was not readable and fresh before deadline')
