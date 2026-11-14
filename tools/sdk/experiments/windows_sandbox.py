"""Stage A experimental AppContainer + Job launcher; not a released sandbox.
Only grants ACLs to its generated experiment directory. Capabilities are explicit experiment inputs, never production defaults.
"""
import ctypes as c
from ctypes import wintypes as w
import subprocess, uuid, csv, os
from pathlib import Path

k=c.WinDLL('kernel32',use_last_error=True)
u=c.WinDLL('user32',use_last_error=True)
e=c.WinDLL('userenv',use_last_error=True)
a=c.WinDLL('advapi32',use_last_error=True)
P=c.c_void_p
SIZE=c.c_size_t
class SI(c.Structure):
 _fields_=[('cb',w.DWORD),('reserved',w.LPWSTR),('desktop',w.LPWSTR),('title',w.LPWSTR),('x',w.DWORD),('y',w.DWORD),('cx',w.DWORD),('cy',w.DWORD),('charsX',w.DWORD),('charsY',w.DWORD),('fill',w.DWORD),('flags',w.DWORD),('show',w.WORD),('reserved2len',w.WORD),('reserved2',P),('stdin',P),('stdout',P),('stderr',P)]
class SX(c.Structure):
 _fields_=[('si',SI),('attrs',P)]
class PI(c.Structure):
 _fields_=[('process',P),('thread',P),('pid',w.DWORD),('tid',w.DWORD)]
class CAP(c.Structure):
 _fields_=[('sid',P),('caps',P),('count',w.DWORD),('reserved',w.DWORD)]
class SA(c.Structure):
 _fields_=[('length',w.DWORD),('descriptor',P),('inherit',w.BOOL)]
class BASIC(c.Structure):
 _fields_=[('processTime',c.c_int64),('jobTime',c.c_int64),('flags',w.DWORD),('minWorking',SIZE),('maxWorking',SIZE),('active',w.DWORD),('affinity',SIZE),('priority',w.DWORD),('scheduling',w.DWORD)]
class IO(c.Structure):
 _fields_=[(n,c.c_uint64) for n in ('readOps','writeOps','otherOps','readBytes','writeBytes','otherBytes')]
class LIMIT(c.Structure):
 _fields_=[('basic',BASIC),('io',IO),('processMemory',SIZE),('jobMemory',SIZE),('peakProcess',SIZE),('peakJob',SIZE)]

def bind(d,name,args,result=w.BOOL):
 f=getattr(d,name);f.argtypes=args;f.restype=result;return f
bind(e,'CreateAppContainerProfile',[w.LPCWSTR,w.LPCWSTR,w.LPCWSTR,P,w.DWORD,c.POINTER(P)],c.c_long)
bind(e,'DeleteAppContainerProfile',[w.LPCWSTR],c.c_long)
bind(a,'ConvertSidToStringSidW',[P,c.POINTER(w.LPWSTR)])
bind(a,'ConvertStringSecurityDescriptorToSecurityDescriptorW',[w.LPCWSTR,w.DWORD,c.POINTER(P),P])
bind(k,'InitializeProcThreadAttributeList',[P,w.DWORD,w.DWORD,c.POINTER(SIZE)])
bind(k,'UpdateProcThreadAttribute',[P,w.DWORD,SIZE,P,SIZE,P,P])
bind(k,'DeleteProcThreadAttributeList',[P],None)
bind(k,'CreateProcessW',[w.LPCWSTR,w.LPWSTR,P,P,w.BOOL,w.DWORD,P,w.LPCWSTR,c.POINTER(SX),c.POINTER(PI)])
bind(k,'CreateJobObjectW',[P,w.LPCWSTR],P)
bind(k,'SetInformationJobObject',[P,c.c_int,P,w.DWORD])
bind(k,'AssignProcessToJobObject',[P,P])
bind(k,'ResumeThread',[P],w.DWORD)
bind(k,'WaitForSingleObject',[P,w.DWORD],w.DWORD)
bind(k,'GetExitCodeProcess',[P,c.POINTER(w.DWORD)])
bind(k,'TerminateJobObject',[P,w.UINT])
bind(k,'TerminateProcess',[P,w.UINT])
bind(k,'CloseHandle',[P])
bind(k,'LocalFree',[P],P)
bind(a,'FreeSid',[P],P)
bind(u,'CreateDesktopW',[w.LPCWSTR,P,P,w.DWORD,w.DWORD,c.POINTER(SA)],P)
bind(u,'CloseDesktop',[P])
bind(u,'CreateWindowStationW',[w.LPCWSTR,w.DWORD,w.DWORD,c.POINTER(SA)],P)
bind(u,'GetProcessWindowStation',[],P)
bind(u,'SetProcessWindowStation',[P])
bind(u,'CloseWindowStation',[P])
bind(u,'GetUserObjectInformationW',[P,c.c_int,P,w.DWORD,c.POINTER(w.DWORD)])
def check(ok):
 if not ok:raise c.WinError(c.get_last_error())
def launch(exe,args,root,isolated_desktop=True,timeout=15,lpac=False,prepare=None,on_started=None,capability_names=(),image_mitigations=0):
 name='SpatialWallpaper.Experiment.'+uuid.uuid4().hex
 sid=P();job=None;desktop=None;station=None;attrs=None;pi=PI();sd=P();capability_allocations=[]
 hr=e.CreateAppContainerProfile(name,name,'SDK stage A experiment',None,0,c.byref(sid))
 if hr<0:raise OSError('CreateAppContainerProfile HRESULT '+hex(hr&0xffffffff))
 try:
  text=w.LPWSTR();check(a.ConvertSidToStringSidW(sid,c.byref(text)));sidtext=text.value;k.LocalFree(text)
  # Root is generated for this attempt. No parent/user-profile ACL is changed.
  subprocess.run(['icacls',str(root),'/grant','*'+sidtext+':(OI)(CI)(RX)','/T','/Q'],check=True,capture_output=True)
  # LPAC image-section access has an additional restricted-package check.
  # Only trusted generated runtime DLLs receive this ACE, never author content.
  if lpac:
   for image in root.rglob('*.dll'):
    subprocess.run(['icacls',str(image),'/grant','*S-1-15-2-2:(RX)','*S-1-15-2-1:(RX)','/Q'],check=True,capture_output=True)
  output=root/'output';output.mkdir(exist_ok=True)
  caller=next(csv.reader(subprocess.check_output(['whoami','/user','/fo','csv','/nh'],text=True).splitlines()))[1]
  subprocess.run(['icacls',str(output),'/grant','*'+caller+':(OI)(CI)(F)','/Q'],check=True,capture_output=True)
  subprocess.run(['icacls',str(output),'/grant','*'+sidtext+':(OI)(CI)(M)','/T','/Q'],check=True,capture_output=True)
  subprocess.run(['icacls',str(output),'/setintegritylevel','(OI)(CI)L','/T','/Q'],check=True,capture_output=True)
  inherited=prepare(sidtext,output) if prepare else []
  sx=SX();sx.si.cb=c.sizeof(sx)
  if isolated_desktop:
   desktop_name='SpatialWallpaper_'+uuid.uuid4().hex
   check(a.ConvertStringSecurityDescriptorToSecurityDescriptorW('D:(A;;GA;;;SY)(A;;GA;;;'+caller+')(A;;GA;;;'+sidtext+')S:(ML;;NW;;;LW)',1,c.byref(sd),None))
   sa=SA(c.sizeof(SA),sd,False)
   station=u.CreateWindowStationW(None,0,0x10000000,None);check(station)
   buffer=c.create_unicode_buffer(512);needed=w.DWORD()
   check(u.GetUserObjectInformationW(station,2,buffer,c.sizeof(buffer),c.byref(needed)))
   station_name=buffer.value
   previous=u.GetProcessWindowStation();check(u.SetProcessWindowStation(station))
   try:
    desktop=u.CreateDesktopW(desktop_name,None,None,0,0x10000000,c.byref(sa));check(desktop)
   finally:check(u.SetProcessWindowStation(previous))
   sx.si.desktop=station_name+'\\'+desktop_name
  size=SIZE();k.InitializeProcThreadAttributeList(None,5,0,c.byref(size))
  attrs=c.create_string_buffer(size.value);check(k.InitializeProcThreadAttributeList(attrs,5,0,c.byref(size)));sx.attrs=c.cast(attrs,P)
  class SID_ATTRIBUTE(c.Structure):_fields_=[("sid",P),("attributes",w.DWORD)]
  capability_sids=[]
  derive=bind(c.WinDLL("kernelbase",use_last_error=True),"DeriveCapabilitySidsFromName",[w.LPCWSTR,c.POINTER(P),c.POINTER(w.DWORD),c.POINTER(P),c.POINTER(w.DWORD)])
  for capability_name in capability_names:
   groups=P();group_count=w.DWORD();values=P();count=w.DWORD()
   check(derive(capability_name,c.byref(groups),c.byref(group_count),c.byref(values),c.byref(count)))
   group_array=c.cast(groups,c.POINTER(P));value_array=c.cast(values,c.POINTER(P))
   for i in range(group_count.value):capability_allocations.append(group_array[i])
   for i in range(count.value):capability_sids.append(value_array[i]);capability_allocations.append(value_array[i])
   capability_allocations.extend([groups,values])
  capability_array=(SID_ATTRIBUTE*len(capability_sids))(*[SID_ATTRIBUTE(value,4) for value in capability_sids])
  caps=CAP(sid,c.cast(capability_array,P) if capability_sids else None,len(capability_sids),0);check(k.UpdateProcThreadAttribute(attrs,0,0x20009,c.byref(caps),c.sizeof(caps),None,None))
  optout=w.DWORD(1)
  if lpac:check(k.UpdateProcThreadAttribute(attrs,0,0x2000f,c.byref(optout),4,None,None))
  if inherited:
   handle_array=(P*len(inherited))(*inherited)
   check(k.UpdateProcThreadAttribute(attrs,0,0x20002,handle_array,c.sizeof(handle_array),None,None))
  mitigation=c.c_uint64(image_mitigations)
  if image_mitigations:check(k.UpdateProcThreadAttribute(attrs,0,0x20007,c.byref(mitigation),c.sizeof(mitigation),None,None))
  job=k.CreateJobObjectW(None,None);check(job)
  limit=LIMIT();limit.basic.flags=0x2000|8|0x100;limit.basic.active=1;limit.processMemory=512*1024*1024
  check(k.SetInformationJobObject(job,9,c.byref(limit),c.sizeof(limit)))
  ui=w.DWORD(0xff);check(k.SetInformationJobObject(job,4,c.byref(ui),c.sizeof(ui)))
  # Atomic job assignment at process creation closes the suspended-orphan race.
  jobs=(P*1)(job)
  check(k.UpdateProcThreadAttribute(attrs,0,0x2000d,jobs,c.sizeof(jobs),None,None))
  command=c.create_unicode_buffer(subprocess.list2cmdline([str(exe),*map(str,args)]))
  environment=c.create_unicode_buffer('SystemRoot='+os.environ['SystemRoot']+'\0LOCALAPPDATA='+os.environ['LOCALAPPDATA']+'\0WINDIR='+os.environ['SystemRoot']+'\0TEMP='+str(output)+'\0TMP='+str(output)+'\0\0')
  check(k.CreateProcessW(str(exe),command,None,None,bool(inherited),0x80000|4|0x08000000|0x400,environment,str(root),c.byref(sx),c.byref(pi)))
  try:
   if on_started:on_started(pi.pid)
   if k.ResumeThread(pi.thread)==0xffffffff:raise c.WinError(c.get_last_error())
   result=k.WaitForSingleObject(pi.process,int(timeout*1000))
   if result!=0: k.TerminateJobObject(job,124);raise TimeoutError('restricted process deadline')
   code=w.DWORD();check(k.GetExitCodeProcess(pi.process,c.byref(code)));return code.value,sidtext
  finally:
   if pi.process:k.TerminateProcess(pi.process,125)
 finally:
  if pi.thread:k.CloseHandle(pi.thread)
  if pi.process:k.CloseHandle(pi.process)
  if job:k.CloseHandle(job)
  if attrs:k.DeleteProcThreadAttributeList(attrs)
  if desktop:u.CloseDesktop(desktop)
  if station:u.CloseWindowStation(station)
  if sd:k.LocalFree(sd)
  for allocated in capability_allocations:k.LocalFree(allocated)
  a.FreeSid(sid)
  hr=e.DeleteAppContainerProfile(name)
  if hr<0:raise OSError('DeleteAppContainerProfile HRESULT '+hex(hr&0xffffffff))
