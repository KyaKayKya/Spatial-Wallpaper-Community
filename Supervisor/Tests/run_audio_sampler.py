"""Run the production native sampler with owned synthetic process identities."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib,ctypes,struct,time
root=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(root/'tools/sdk/experiments'));sys.path.insert(0,str(root/'tools/sdk'))
from native_build import compile_probe
from timed_output import ready_line
out=root/'Validation/Content/sdk-runtime/E/audio-sampler'/uuid.uuid4().hex
out.mkdir(parents=True);print(out,flush=True)
sampler_source=root/'Supervisor/Interactive/ProcessAudioSampler.cpp';sampler=out/'AudioSampler.exe'
compile_probe(sampler_source,sampler)
tone_source=root/'tools/sdk/experiments/process_audio_capture.cpp';tone=out/'ToneFixture.exe'
compile_probe(tone_source,tone)
kernel=ctypes.WinDLL('kernel32',use_last_error=True)
kernel.GetProcessTimes.argtypes=[ctypes.c_void_p,*([ctypes.POINTER(ctypes.c_ulonglong)]*4)];kernel.GetProcessTimes.restype=ctypes.c_int
def creation(process):
 values=[ctypes.c_ulonglong() for _ in range(4)]
 assert kernel.GetProcessTimes(int(process._handle),*[ctypes.byref(v) for v in values])
 return values[0].value
def packets(data):
 assert len(data)%40==0 and len(data)<=40*32,'sampler-frame-bound'
 frames=[struct.unpack('<IIQQQI4B',data[i:i+40]) for i in range(0,len(data),40)]
 assert all(p[0]==0x46415753 and p[1]==2 and p[2]==i+1 for i,p in enumerate(frames)),'sampler-frame-header'
 assert all(max(p[6:])<=15 for p in frames),'sampler-feature-bound'
 assert all(not any(p[6:]) or p[4]>0 for p in frames),'features-missing-capture-time'
 return frames
children=[];report={'status':'not-complete'}
try:
 for mode,frequency in (('--tone-right','80'),('--tone','7000')):
  process=subprocess.Popen([str(tone),mode,frequency],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);children.append(process)
  assert json.loads(ready_line(process,5))['ready']
 target,other=children;born=creation(target)
 wrong=subprocess.run([str(sampler),str(target.pid),str(born+1)],capture_output=True,timeout=5)
 rejected=packets(wrong.stdout);assert len(rejected)==1 and rejected[0][5]==3 and rejected[0][6:]==(0,0,0,0),'wrong-source-identity-accepted'
 capture=subprocess.Popen([str(sampler),str(target.pid),str(born)],stdout=subprocess.PIPE,stderr=subprocess.PIPE);children.append(capture)
 time.sleep(1.2);stopped=time.perf_counter();target.terminate();target.wait(timeout=3)
 data,errors=capture.communicate(timeout=3);elapsed=(time.perf_counter()-stopped)*1000
 frames=packets(data);assert capture.returncode==0 and not errors,(capture.returncode,errors)
 active=[p for p in frames if p[5]==1]
 assert 3<=len(active)<=7 and any(p[6]>0 for p in active),'right-channel-target-missing'
 assert all(p[9]==0 for p in active),'unrelated-high-band-leaked'
 assert all(b[3]-a[3]>=200 for a,b in zip(active,active[1:])),'sampler-output-exceeds-five-Hz'
 assert frames[-1][5]==2 and frames[-1][6:]==(0,0,0,0),'source-exit-not-revoked'
 assert other.poll() is None and elapsed<1000,'sampler-exit-or-other-source-lifecycle'
 report.update(status='partial-production-native-audio-sampler-only',frames=frames,wrongIdentity=rejected,terminateToSamplerExitObservedMs=elapsed)
finally:
 for process in children:
  if process.poll() is None:process.terminate()
  process.wait(timeout=3)
 report['childrenExited']=all(p.poll() is not None for p in children)
 files=(sampler_source,root/'Supervisor/Interactive/AudioFeatures.h',root/'Supervisor/Interactive/AudioTiming.h',tone_source,sampler,tone,Path(__file__))
 report['sha256']={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report))
