"""Kill the owned fixture after actual selected key/button down; outer owner must pair its release."""
from pathlib import Path
import ctypes as c,subprocess,sys,json,time,uuid,hashlib
from ctypes import wintypes as w
from owned_input_cleanup import cleanup
from input_probe_job import ProbeJob
from owned_input_timing import ForcedClientTiming
root=Path(__file__).resolve().parents[2];compiled=Path(sys.argv[1]).resolve();client=compiled/'Client.exe'
assert client.is_file() and compiled.is_relative_to(root/'Validation/Content/sdk-runtime/E/native-desktop-input-host')
publication='--pid-publication' in sys.argv
negative='--creation-negative' in sys.argv
creation='--creation' in sys.argv or negative
unmapped='--unmapped' in sys.argv
assert not unmapped or not (publication or creation)
selected=None
if '--key' in sys.argv:
 index=sys.argv.index('--key');assert index+1<len(sys.argv),'missing key name';selected=sys.argv[index+1]
keys={'Space':(32,1),'A':(65,4),'Left':(37,8),'Right':(39,16),'Up':(38,32),'Down':(40,64),'Primary':(1,2),'Secondary':(2,128)}
assert selected is None or selected in keys,'unsupported key'
assert selected is None or not(unmapped or publication or creation),'key mode cannot mix fault modes'
key,bit=keys[selected] if selected else (65,4) if unmapped else (32,1)
mode='--unmapped' if unmapped else '--creation-negative' if negative else '--creation' if creation else '--pid-publication' if publication else None
if selected:mode='--unmapped' if selected=='A' else '--key='+selected
out=root/'Validation/Content/sdk-runtime/E/owned-input-timeout'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
user=c.WinDLL('user32',use_last_error=True);user.GetForegroundWindow.restype=w.HWND;user.SetForegroundWindow.argtypes=[w.HWND];user.GetAsyncKeyState.argtypes=[c.c_int];user.GetAsyncKeyState.restype=c.c_short
kernel=c.WinDLL('kernel32',use_last_error=True);kernel.OpenProcess.argtypes=[w.DWORD,w.BOOL,w.DWORD];kernel.OpenProcess.restype=w.HANDLE;kernel.WaitForSingleObject.argtypes=[w.HANDLE,w.DWORD];kernel.WaitForSingleObject.restype=w.DWORD;kernel.CloseHandle.argtypes=[w.HANDLE];kernel.TerminateProcess.argtypes=[w.HANDLE,w.UINT];kernel.TerminateProcess.restype=w.BOOL
class Point(c.Structure):_fields_=[('x',w.LONG),('y',w.LONG)]
user.GetCursorPos.argtypes=[c.POINTER(Point)];user.GetCursorPos.restype=w.BOOL
user.SetCursorPos.argtypes=[c.c_int,c.c_int];user.SetCursorPos.restype=w.BOOL
cursor=Point();assert user.GetCursorPos(c.byref(cursor))
previous=user.GetForegroundWindow();assert not user.GetAsyncKeyState(key)&0x8000,'preheld key';child=None;suspended=None
report={'status':'failed','client_sha256':hashlib.sha256(client.read_bytes()).hexdigest(),'key':key,'key_name':selected or ('A' if unmapped else 'Space'),'ledger_bit':bit,'pid_publication_negative':publication,'creation_probe':creation,'omit_atomic_job':negative,'compiled_report':json.loads((compiled/'report.json').read_text())}
timing=ForcedClientTiming();report['forced_lifecycle']=timing.data
outer=None
try:
 outer=ProbeJob()
 with (out/'client.log').open('w') as log:
  child=subprocess.Popen([str(client),'--injector-timeout-probe',str(out)]+([mode] if mode else []),stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW)
  report['client_pid']=child.pid
  outer.assign(child);report['outer_job_assigned']=True;(out/'outer-job-ready').write_text('owned')
  deadline=time.monotonic()+10
  while not (out/('creation-paused' if creation else 'pid-publication-paused' if publication else 'timeout-probe-ready')).exists():
   assert child.poll() is None and time.monotonic()<deadline,'probe did not reach actual held state';time.sleep(.02)
  assert user.GetAsyncKeyState(key)&0x8000,'native key was not held'
  if publication:
   partial=list(out.glob('inject-*.request.pid.tmp'));assert len(partial)==1 and len(partial[0].read_text().split())==1,'partial record was not captured'
   report['partial_record']=partial[0].read_text()
  if publication or creation:
   pid=int((out/('creation-paused' if creation else 'pid-publication-paused')).read_text().split()[0]);suspended=kernel.OpenProcess(0x100001,False,pid);assert suspended,'suspended injector handle unavailable'
  identities=[list(map(int,p.read_text().split())) for p in out.glob('inject-*.request.pid')]
  assert identities and all(len(value)==2 and value[0]>0 and value[1]>0 for value in identities),'missing owned injector identities'
  report['published_injector_identities']=identities
  timing.begin_force();child.kill();child.wait(5);timing.client_exited();report['client_kernel_exit_observed']=True
  if suspended:
   observed=kernel.WaitForSingleObject(suspended,300 if negative else 5000)
   if negative:
    assert observed==258,'negative failed to reproduce unowned suspended child';report['unowned_child_survived']=True
    assert kernel.TerminateProcess(suspended,1) and kernel.WaitForSingleObject(suspended,5000)==0,'negative child cleanup failed'
   else:assert observed==0,'unpublished suspended injector survived owner kill'
   report['unpublished_injector_kernel_exited']=True
  assert (out/'input-obligations.bin').read_bytes()==bytes([bit]),'Client finally unexpectedly discharged obligation'
  # Negative observation before emergency cleanup: killing Client alone is insufficient.
  still_held=bool(user.GetAsyncKeyState(key)&0x8000);report['without_outer_cleanup_held']=still_held
  assert still_held,'negative did not reproduce missing outer cleanup'
  report['held_before_kill']=True;report['ledger_after_kill']=bit
  cleanup(out);report['first_external_cleanup']=json.loads((out/'external-input-cleanup.json').read_text());deadline=time.monotonic()+2
  while user.GetAsyncKeyState(key)&0x8000 and time.monotonic()<deadline:time.sleep(.01)
  assert not user.GetAsyncKeyState(key)&0x8000 and (out/'input-obligations.bin').read_bytes()==b'\0','outer cleanup failed'
  report['native_released']=True;report['ledger_after_cleanup']=0
  report['status']='expected-nonatomic-job-rejected-and-cleaned' if negative else 'partial-actual-client-kill-release-positive-and-negative';print('PASS actual Client kill leaves held key; outer owner pairs release')
finally:
 primary=sys.exc_info()[1];errors=[]
 try:
  if child is not None:
   if child.poll() is None:timing.begin_force();child.kill()
   child.wait(5)
   if timing.data['forced_started_at_ms'] is not None:
    timing.client_exited();report['client_kernel_exit_observed']=True
 except Exception as error:errors.append('client:'+repr(error))
 try:
  if outer is not None:outer.close();report['outer_job_active_zero']=True
 except Exception as error:errors.append('outer-job:'+repr(error))
 try:
  if suspended:
   if kernel.WaitForSingleObject(suspended,0)!=0:
    assert kernel.TerminateProcess(suspended,1) and kernel.WaitForSingleObject(suspended,5000)==0,'owned negative child cleanup failed'
   kernel.CloseHandle(suspended);suspended=None
 except Exception as error:errors.append('suspended:'+repr(error))
 try:
  # A failed Client wait cannot authorize releases while it can still inject.
  assert child is None or child.poll() is not None,'Client still alive before release'
  cleanup(out)
 except Exception as error:errors.append('release:'+repr(error))
 try:
  if selected in ('Primary','Secondary'):user.SetCursorPos(cursor.x,cursor.y)
  if previous:user.SetForegroundWindow(previous)
 except Exception as error:errors.append('restore:'+repr(error))
 try:
  if not errors and report.get('client_kernel_exit_observed') is True and report.get('outer_job_active_zero') is True:
   timing.cleanup_finished()
 except Exception as error:errors.append('timing:'+repr(error))
 report['cleanup_errors']=errors
 if primary is not None:report['primary_failure']=repr(primary)
 if errors or primary is not None:report['status']='failed'
 (out/'report.json').write_text(json.dumps(report,indent=2))
 if errors and primary is None:raise RuntimeError(';'.join(errors))
