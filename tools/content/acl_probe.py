"""ACL-only fixtures for this test's own connection descriptor, no token logging."""
import ctypes
from ctypes import wintypes
adv=ctypes.WinDLL('advapi32',use_last_error=True);kernel=ctypes.WinDLL('kernel32',use_last_error=True)
adv.ConvertStringSecurityDescriptorToSecurityDescriptorW.argtypes=[wintypes.LPCWSTR,wintypes.DWORD,ctypes.POINTER(ctypes.c_void_p),ctypes.POINTER(wintypes.ULONG)]
adv.SetFileSecurityW.argtypes=[wintypes.LPCWSTR,wintypes.DWORD,ctypes.c_void_p]
kernel.LocalFree.argtypes=[ctypes.c_void_p]
def make_old_descriptor_readable(path):
 path.parent.mkdir(parents=True,exist_ok=True);path.write_text('{}',encoding='utf-8');descriptor=ctypes.c_void_p();size=wintypes.ULONG()
 assert adv.ConvertStringSecurityDescriptorToSecurityDescriptorW('D:P(A;;FA;;;WD)',1,ctypes.byref(descriptor),ctypes.byref(size))
 try:assert adv.SetFileSecurityW(str(path),0x80000004,descriptor)
 finally:kernel.LocalFree(descriptor)
def acl_is_private(path):
 adv.GetNamedSecurityInfoW.argtypes=[wintypes.LPWSTR,ctypes.c_int,wintypes.DWORD,ctypes.c_void_p,ctypes.c_void_p,ctypes.POINTER(ctypes.c_void_p),ctypes.c_void_p,ctypes.POINTER(ctypes.c_void_p)]
 adv.GetAce.argtypes=[ctypes.c_void_p,wintypes.DWORD,ctypes.POINTER(ctypes.c_void_p)]
 adv.ConvertSidToStringSidW.argtypes=[ctypes.c_void_p,ctypes.POINTER(ctypes.c_void_p)]
 adv.GetTokenInformation.argtypes=[ctypes.c_void_p,ctypes.c_int,ctypes.c_void_p,wintypes.DWORD,ctypes.POINTER(wintypes.DWORD)]
 adv.OpenProcessToken.argtypes=[ctypes.c_void_p,wintypes.DWORD,ctypes.POINTER(ctypes.c_void_p)]
 kernel.GetCurrentProcess.restype=ctypes.c_void_p;kernel.CloseHandle.argtypes=[ctypes.c_void_p]
 def sid_text(pointer):
  text=ctypes.c_void_p();assert adv.ConvertSidToStringSidW(pointer,ctypes.byref(text))
  try:return ctypes.wstring_at(text)
  finally:kernel.LocalFree(text)
 token=ctypes.c_void_p();assert adv.OpenProcessToken(kernel.GetCurrentProcess(),8,ctypes.byref(token));needed=wintypes.DWORD();adv.GetTokenInformation(token,1,None,0,ctypes.byref(needed));buffer=ctypes.create_string_buffer(needed.value)
 try:assert adv.GetTokenInformation(token,1,buffer,needed,ctypes.byref(needed));own=sid_text(ctypes.c_void_p.from_buffer(buffer).value)
 finally:kernel.CloseHandle(token)
 acl=ctypes.c_void_p();descriptor=ctypes.c_void_p();assert adv.GetNamedSecurityInfoW(str(path),1,4,None,None,ctypes.byref(acl),None,ctypes.byref(descriptor))==0
 try:
  count=ctypes.c_ushort.from_address(acl.value+4).value;allowed=[]
  for i in range(count):
   ace=ctypes.c_void_p();assert adv.GetAce(acl,i,ctypes.byref(ace));kind=ctypes.c_ubyte.from_address(ace.value).value
   if kind!=0:return False
   allowed.append(sid_text(ace.value+8))
  return count==2 and set(allowed)=={own,'S-1-5-18'}
 finally:kernel.LocalFree(descriptor)
