"""Production sampler: late child inclusion, child exit, parent exit with live child."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib,ctypes,struct,time,threading
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'));sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from provenance import sources
from native_build import compile_probe
from timed_output import ready_line
from windows_sandbox import k,LIMIT,check
from process_observation import observe_child
out=ROOT/'Validation/Content/sdk-runtime/E/audio-tree'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();print(out,flush=True)
compile_probe(ROOT/'Supervisor/Interactive/ProcessAudioSampler.cpp',out/'AudioSampler.exe')
compile_probe(ROOT/'tools/sdk/experiments/process_audio_capture.cpp',out/'ToneFixture.exe')
k.GetProcessTimes.argtypes=[ctypes.c_void_p,*([ctypes.POINTER(ctypes.c_ulonglong)]*4)];k.GetProcessTimes.restype=ctypes.c_int
k.QueryPerformanceCounter.argtypes=[ctypes.POINTER(ctypes.c_longlong)];k.QueryPerformanceCounter.restype=ctypes.c_int
k.QueryPerformanceFrequency.argtypes=[ctypes.POINTER(ctypes.c_longlong)];k.QueryPerformanceFrequency.restype=ctypes.c_int
frequency=ctypes.c_longlong();check(k.QueryPerformanceFrequency(ctypes.byref(frequency)))
children=[];observed=[];job=None;reader=None;frames=[];reader_errors=[];report={'status':'not-complete','sources':receipt};sync=threading.Lock()
def send(parent,command):parent.stdin.write(command+'\n');parent.stdin.flush()
def snapshot():
 with sync:return list(frames)
def wait_for(predicate,timeout,code):
 deadline=time.monotonic()+timeout
 while time.monotonic()<deadline:
  assert not reader_errors,reader_errors
  if predicate(snapshot()):return
  time.sleep(.01)
 raise AssertionError(code)
def read_frames(process):
 try:
  while True:
   data=process.stdout.read(40)
   if not data:return
   assert len(data)==40,'partial audio frame'
   p=struct.unpack('<IIQQQI4B',data)
   ticks=ctypes.c_longlong();check(k.QueryPerformanceCounter(ctypes.byref(ticks)));received_qpc=ticks.value*10000000//frequency.value
   with sync:
    assert len(frames)<128 and p[0]==0x46415753 and p[1]==2 and p[2]==len(frames)+1,'audio tree frame bound/identity'
    assert max(p[6:])<=15 and (not any(p[6:]) or p[4]>0),'audio tree feature shape'
    assert p[5] in (1,2),'audio tree unexpected status'
    if p[4]:
     age=received_qpc-p[4];assert 0<=age<=5000000,'audio tree stale/future capture time'
     report['maxCaptureToReadObservedMs']=max(report.get('maxCaptureToReadObservedMs',0),age/10000)
    if p[5]!=1:assert p[4]==0 and p[6:]==(0,0,0,0),'audio tree nonzero terminal state'
    frames.append((time.perf_counter(),p))
 except Exception as error:reader_errors.append(str(error))
try:
 parent=subprocess.Popen([sys.executable,str(ROOT/'tools/sdk/experiments/audio_source_parent.py'),str(out/'ToneFixture.exe')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);children.append(parent)
 assert json.loads(ready_line(parent,5))['parent_ready']
 job=k.CreateJobObjectW(None,None);check(job);limits=LIMIT();limits.basic.flags=0x2000|8;limits.basic.active=3
 check(k.SetInformationJobObject(job,9,ctypes.byref(limits),ctypes.sizeof(limits)));check(k.AssignProcessToJobObject(job,int(parent._handle)))
 other=subprocess.Popen([str(out/'ToneFixture.exe'),'--tone-right-long','7000'],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);children.append(other);assert json.loads(ready_line(other,5))['ready']
 times=[ctypes.c_ulonglong() for _ in range(4)];check(k.GetProcessTimes(int(parent._handle),*[ctypes.byref(v) for v in times]))
 sampler=subprocess.Popen([str(out/'AudioSampler.exe'),str(parent.pid),str(times[0].value)],stdout=subprocess.PIPE,stderr=subprocess.PIPE);children.append(sampler)
 reader=threading.Thread(target=read_frames,args=(sampler,),daemon=True);reader.start()
 wait_for(lambda rows:len(rows)>=3,3,'silent parent sampler not ready')
 assert all(p[5]==1 and p[6:]==(0,0,0,0) for _,p in snapshot()),'silent parent captured unrelated source'
 baseline=len(snapshot());send(parent,'start');first=json.loads(ready_line(parent,5));assert first['child_ready'];first_handle=observe_child(parent.pid,'ToneFixture.exe');observed.append(first_handle);assert first_handle.pid==first['pid']
 wait_for(lambda rows:any(p[6]>0 for _,p in rows[baseline:]),3,'late child audio missing')
 report['lateChildIncluded']=True;send(parent,'stop');assert json.loads(ready_line(parent,5))['child_stopped'];assert first_handle.exited(1)
 stopped=time.perf_counter()
 wait_for(lambda rows:len(rows)>=3 and all(at>=stopped and p[5]==1 and p[6:]==(0,0,0,0) for at,p in rows[-3:]),4,'child exit did not become silent')
 assert parent.poll() is None and sampler.poll() is None;report['childExitSilenceObservedMs']=(time.perf_counter()-stopped)*1000
 baseline=len(snapshot());send(parent,'start');second=json.loads(ready_line(parent,5));assert second['child_ready'];second_handle=observe_child(parent.pid,'ToneFixture.exe');observed.append(second_handle);assert second_handle.pid==second['pid']
 wait_for(lambda rows:any(p[6]>0 for _,p in rows[baseline:]),3,'replacement child audio missing')
 ended=time.perf_counter();send(parent,'exit');parent.wait(timeout=2);sampler.wait(timeout=2);reader.join(timeout=2)
 assert not reader.is_alive() and not reader_errors;rows=snapshot();assert sampler.returncode==0 and not sampler.stderr.read()
 assert rows[-1][1][5]==2 and rows[-1][1][6:]==(0,0,0,0),'parent exit did not revoke source tree'
 assert not second_handle.exited() and other.poll() is None,'child or unrelated source ended before revoke proof'
 elapsed=(time.perf_counter()-ended)*1000;assert elapsed<1000,'parent exit sampler latency'
 assert all(p[9]==0 for _,p in rows),'unrelated high-band source leaked'
 active=[p for _,p in rows if p[5]==1];assert all(b[3]-a[3]>=200 for a,b in zip(active,active[1:])),'tree sampler rate exceeded'
 report.update(status='partial-production-audio-tree',parentExitCommandToSamplerAndReaderObservedMs=elapsed,childSurvivedParentAtRevoke=True,frames=[p for _,p in rows])
finally:
 if job:k.CloseHandle(job)
 for process in reversed(children):
  if process.poll() is None:process.terminate()
  process.wait(timeout=3)
 if reader:reader.join(timeout=3)
 report['childrenExited']=all(process.poll() is not None for process in children)
 report['descendantsExited']=all(process.exited(3) for process in observed)
 for process in observed:process.close()
 report['readerStopped']=reader is None or not reader.is_alive()
 report['sourceStable']=sources()==receipt
 report['binaries']={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [out/'AudioSampler.exe',out/'ToneFixture.exe']}
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out,flush=True)
assert report['childrenExited'] and report['descendantsExited'] and report['readerStopped'] and report['sourceStable']
