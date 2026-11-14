"""Observe the actual sampler kernel process during broker disposal and parent termination."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib,ctypes,time
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'tools/sdk'));sys.path.insert(0,str(root/'tools/sdk/experiments'))
from check_contracts import DATA
from windows_framework import references
from native_build import compile_probe
from timed_output import ready_line
from process_observation import observe_child
from provenance import sources
out=root/'Validation/Content/sdk-runtime/E/audio-lifecycle'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();print(out,flush=True)
files=[root/p for p in ['Supervisor/Tests/AudioBrokerChecks.cs','Supervisor/Broker/AudioBroker.cs','Supervisor/Windows/TrustedAudioProcess.cs','Supervisor/Windows/Native.cs','Supervisor/Windows/RestrictedProcess.cs']]
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
compile_probe(root/'Supervisor/Interactive/ProcessAudioSampler.cpp',out/'AudioSampler.exe');compile_probe(root/'tools/sdk/experiments/process_audio_capture.cpp',out/'Tone.exe')
children=[];observed=[];report={'status':'not-complete','cases':[],'sources':receipt}
try:
 tone=subprocess.Popen([str(out/'Tone.exe'),'--tone-right-long','80'],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);children.append(tone);assert json.loads(ready_line(tone,5))['ready']
 kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.GetProcessTimes.argtypes=[ctypes.c_void_p,*([ctypes.POINTER(ctypes.c_ulonglong)]*4)];kernel.GetProcessTimes.restype=ctypes.c_int
 times=[ctypes.c_ulonglong() for _ in range(4)];assert kernel.GetProcessTimes(int(tone._handle),*[ctypes.byref(v) for v in times])
 for mode in ['dispose','crash']:
  parent=subprocess.Popen([str(out/'Checks.exe'),str(out/'AudioSampler.exe'),str(tone.pid),str(times[0].value),mode],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);children.append(parent);assert ready_line(parent,6).strip()=='ready'
  sampler=observe_child(parent.pid,'AudioSampler.exe');observed.append(sampler);assert not sampler.exited() and tone.poll() is None
  began=time.perf_counter()
  if mode=='dispose':parent.stdin.write('stop\n');parent.stdin.flush()
  else:parent.terminate()
  stdout,stderr=parent.communicate(timeout=5);assert mode=='crash' or parent.returncode==0,(stdout,stderr)
  assert sampler.exited(2),'sampler survived owner '+mode
  assert tone.poll() is None,'source exit confounded ownership test'
  report['cases'].append({'mode':mode,'samplerPid':sampler.pid,'samplerExited':True,'sourceStillAlive':True,'ownerStopToSamplerExitObservedMs':(time.perf_counter()-began)*1000})
 report['status']='partial-real-audio-owner-lifecycle'
finally:
 for child in reversed(children):
  if child.poll() is None:child.terminate()
  child.wait(timeout=5)
 report['samplersExited']=all(p.exited(2) for p in observed)
 for p in observed:p.close()
 report['childrenExited']=all(p.poll() is not None for p in children)
 report['sourceStable']=sources()==receipt
 paths=[*files,root/'Supervisor/Interactive/ProcessAudioSampler.cpp',root/'Supervisor/Interactive/AudioFeatures.h',root/'Supervisor/Interactive/AudioTiming.h',out/'Checks.exe',out/'AudioSampler.exe']
 report['sha256']={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report))
assert report['sourceStable'] and report['childrenExited'] and report['samplersExited']
