"""Run managed production audio supervision and native sampler in a private station."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib,ctypes
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'tools/sdk'));sys.path.insert(0,str(root/'tools/sdk/experiments'))
from check_contracts import DATA
from windows_framework import references
from native_build import compile_probe
from timed_output import ready_line
out=root/'Validation/Content/sdk-runtime/E/audio-broker'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
files=[root/p for p in ['Supervisor/Tests/AudioBrokerChecks.cs','Supervisor/Broker/AudioBroker.cs','Supervisor/Windows/TrustedAudioProcess.cs','Supervisor/Windows/Native.cs','Supervisor/Windows/RestrictedProcess.cs']]
command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)]
subprocess.run(command,check=True)
compile_probe(root/'Supervisor/Interactive/ProcessAudioSampler.cpp',out/'AudioSampler.exe')
compile_probe(root/'tools/sdk/experiments/process_audio_capture.cpp',out/'Tone.exe')
children=[];report={'status':'not-complete'}
try:
 tone=subprocess.Popen([str(out/'Tone.exe'),'--tone-right','80'],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);children.append(tone)
 assert json.loads(ready_line(tone,5))['ready']
 kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.GetProcessTimes.argtypes=[ctypes.c_void_p,*([ctypes.POINTER(ctypes.c_ulonglong)]*4)];kernel.GetProcessTimes.restype=ctypes.c_int
 times=[ctypes.c_ulonglong() for _ in range(4)];assert kernel.GetProcessTimes(int(tone._handle),*[ctypes.byref(v) for v in times])
 check=subprocess.Popen([str(out/'Checks.exe'),str(out/'AudioSampler.exe'),str(tone.pid),str(times[0].value)],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);children.append(check)
 assert ready_line(check,6).strip()=='ready'
 tone.terminate();tone.wait(timeout=3);stdout,stderr=check.communicate(timeout=6)
 report.update(stdout=stdout,stderr=stderr,exitCode=check.returncode);assert check.returncode==0,report
 report['status']='partial-managed-native-audio-broker-only'
finally:
 for child in children:
  if child.poll() is None:child.terminate()
  child.wait(timeout=3)
 report['childrenExited']=all(p.poll() is not None for p in children)
 paths=[*files,root/'Supervisor/Interactive/ProcessAudioSampler.cpp',root/'Supervisor/Interactive/AudioFeatures.h',root/'Supervisor/Interactive/AudioTiming.h',out/'Checks.exe',out/'AudioSampler.exe']
 report['sha256']={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report))
