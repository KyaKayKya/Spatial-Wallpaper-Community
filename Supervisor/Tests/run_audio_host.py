"""Run selected-process audio through configured Host, production LPAC Worker and real C# author."""
from pathlib import Path
import argparse,sys,subprocess,uuid,shutil,json,hashlib,time,ctypes,struct
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'));sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from check_contracts import DATA
from provenance import sources
from windows_framework import references
from native_build import compile_probe
from timed_output import ready_line
from process_observation import observe_child
from audio_process_observation import observe_audio_sampler
from windows_sandbox import k as job_kernel,LIMIT,check
from audio_evidence import validate_inputs
from failure_report import save_report
parser=argparse.ArgumentParser();parser.add_argument('--worker',type=Path,required=True);parser.add_argument('--author',type=Path,required=True);parser.add_argument('--tree-source',action='store_true');parser.add_argument('--pause-sampler',action='store_true');parser.add_argument('--negative-no-pause',action='store_true');parser.add_argument('--require-stable-zero',action='store_true');parser.add_argument('--negative-stability-verdict',action='store_true');args=parser.parse_args();worker=args.worker.resolve();author=args.author.resolve()
if args.negative_no_pause and not args.pause_sampler:parser.error('--negative-no-pause requires --pause-sampler')
if args.pause_sampler and args.tree_source:parser.error('pause mode currently verifies a direct selected source only')
if args.negative_stability_verdict and (not args.require_stable_zero or args.pause_sampler or args.tree_source):parser.error('negative stability verdict requires standalone stable direct-source mode')
out=ROOT/'Validation/Content/sdk-runtime/E/audio-host'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();print(out,flush=True)
wr,ar,input_binding=validate_inputs(worker,author,receipt,args.require_stable_zero,args.negative_stability_verdict)
for name in ['supervisor','validator']:subprocess.run([sys.executable,str(ROOT/f'tools/sdk/build_{name}.py'),'--output',str(out/name)],check=True)
compile_probe(ROOT/'tools/sdk/experiments/process_audio_capture.cpp',out/'Tone.exe')
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');files=[ROOT/'Supervisor/Tests/AudioHostChecks.cs']
for folder in ['Supervisor/Core','Supervisor/Broker','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities']:files+=list((ROOT/folder).glob('*.cs'))
files += [ROOT/'Supervisor/Windows'/name for name in ['Native.cs','RestrictedProcess.cs','TrustedAudioProcess.cs']]
files += [ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'/name for name in ['WorkerFrameCodec.cs','WorkerExitCodes.cs','RuntimeLimits.Generated.cs']]
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Client.exe'),*map(str,files)],check=True)
children=[];sampler=None;source_child=None;source_job=None;report={'status':'not-complete','sources':receipt,'treeSource':args.tree_source,'boundary':'sampler-pause' if args.pause_sampler else 'source-exit','negativeNoPause':args.negative_no_pause,'input_binding':input_binding,'negativeStabilityVerdict':args.negative_stability_verdict}
report.update(author_package_sha256=ar['package_sha256'],author_build_id=ar['author']['build'],author_metadata_sha256=hashlib.sha256((author/'author.json').read_bytes()).hexdigest())
try:
 if args.tree_source:
  tone=subprocess.Popen([sys.executable,str(ROOT/'tools/sdk/experiments/audio_source_parent.py'),str(out/'Tone.exe')],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);children.append(tone);assert json.loads(ready_line(tone,5))['parent_ready']
  source_job=job_kernel.CreateJobObjectW(None,None);check(source_job);limits=LIMIT();limits.basic.flags=0x2000|8;limits.basic.active=3
  check(job_kernel.SetInformationJobObject(source_job,9,ctypes.byref(limits),ctypes.sizeof(limits)));check(job_kernel.AssignProcessToJobObject(source_job,int(tone._handle)))
  tone.stdin.write('start\n');tone.stdin.flush();ready=json.loads(ready_line(tone,5));assert ready['child_ready'];source_child=observe_child(tone.pid,'Tone.exe');assert source_child.pid==ready['pid']
 else:
  tone=subprocess.Popen([str(out/'Tone.exe'),'--tone-right-long','80'],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);children.append(tone);assert json.loads(ready_line(tone,5))['ready']
 kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.GetProcessTimes.argtypes=[ctypes.c_void_p,*([ctypes.POINTER(ctypes.c_ulonglong)]*4)];kernel.GetProcessTimes.restype=ctypes.c_int
 times=[ctypes.c_ulonglong() for _ in range(4)];assert kernel.GetProcessTimes(int(tone._handle),*[ctypes.byref(v) for v in times])
 state=out/'state';config=out/'config.json';config.write_text(json.dumps({'image':str(worker/'player'),'inventory':str(worker/'report.json'),'validator':str(out/'validator'),'state':str(state),'attempts':str(ROOT/'Validation/Sdk'/('j'+out.name[:10])),'width':640,'height':360,'audio':{'process':tone.pid,'created':str(times[0].value)}}))
 with (out/'host.log').open('w') as log,(out/'client.log').open('w') as client_log:
  host=subprocess.Popen([str(out/'supervisor/Supervisor.exe'),str(config)],stdout=log,stderr=subprocess.STDOUT);children.append(host);deadline=time.monotonic()+15
  while not (state/'endpoint.json').exists():
   assert host.poll() is None and time.monotonic()<deadline,'configured Host did not publish endpoint';time.sleep(.02)
  sampler_identity=json.loads((state/'endpoint.json').read_text(encoding='utf-8-sig')).get('audio_sampler')
  sampler=observe_audio_sampler(host,sampler_identity,allow_terminate=args.pause_sampler,allow_suspend=args.pause_sampler);report['samplerIdentity']=sampler_identity;sample_start=time.perf_counter();cpu_start=sampler.cpu_ms()
  client=subprocess.Popen([str(out/'Client.exe'),str(out),str(author/'scene.spatial'),ar['author']['build'],str(host.pid),*(['--pause-sampler'] if args.pause_sampler else []),*(['--require-stable-zero'] if args.require_stable_zero else [])],stdout=client_log,stderr=subprocess.STDOUT);children.append(client);deadline=time.monotonic()+25
  while not (out/'ready-to-end-source').exists():
   assert client.poll() is None and host.poll() is None and time.monotonic()<deadline,'Host author did not become ready';time.sleep(.02)
  report['samplerObservation']={'pid':sampler.pid,'cpuMs':sampler.cpu_ms()-cpu_start,'wallMs':(time.perf_counter()-sample_start)*1000}
  began=time.perf_counter()
  if args.pause_sampler:
   assert tone.poll() is None and host.poll() is None and not sampler.exited(),'boundary process already exited'
   snapshots=list((state/'storage').glob('live-*/state.bin'));assert len(snapshots)==1,'ambiguous author storage'
   with snapshots[0].open('rb') as snapshot:revision=struct.unpack('<q',snapshot.read(16)[8:])[0]
   report['samplerObservation']['authorRevisionImmediatelyBeforeBoundary']=revision
   assert revision==1,'author already cleared before pause boundary'
   report['samplerObservation']['scope']='kernel exit while selected source and Host alive; persisted nonzero then zero, not exact watchdog branch attribution, continuous-zero, or exact author-observation causality'
   if not args.negative_no_pause:sampler.suspend()
   report['samplerObservation']['pauseInjected']=not args.negative_no_pause
   exited=sampler.exited(3);report['samplerObservation']['exitedBeforeFallback']=exited
   report['samplerObservation']['sourceAliveAtExitCheck']=tone.poll() is None
   report['samplerObservation']['hostAliveAtExitCheck']=host.poll() is None
   report['samplerObservation']['boundaryToExitObservedMs']=(time.perf_counter()-began)*1000
   assert exited,'sampler-watchdog-kernel-exit-missing'
   deadline=time.monotonic()+(16 if args.require_stable_zero else 6)
   while not (out/'author-cleared-before-stop').exists():
    assert client.poll() is None and host.poll() is None and tone.poll() is None and time.monotonic()<deadline,'author-clear-before-stop missing';time.sleep(.02)
   assert host.poll() is None and tone.poll() is None,'source or Host ended before watchdog proof'
   report['sourceAndHostAliveAfterSamplerExitAndAuthorClear']=True
   (out/'allow-host-stop').write_text('kernel-exit-and-live-source-observed')
  else:
   tone.terminate();tone.wait(timeout=3);assert sampler.exited(2),'sampler survived source termination';report['samplerObservation']['sourceTerminationToSamplerExitMs']=(time.perf_counter()-began)*1000
  if source_child:assert not source_child.exited(),'source child did not survive selected parent termination'
  assert client.wait(timeout=15)==0,'audio Host client failed';assert host.wait(timeout=5)==0;assert sampler.exited(),'sampler remains after Host exit'
  if source_child:assert not source_child.exited(),'source child ended before author-clear proof';report['sourceChildAliveAfterAuthorCleared']=True
 assert sources()==receipt,'audio Host source drift'
 report.update(status='partial-configured-audio-Host',worker_report_sha256=hashlib.sha256((worker/'report.json').read_bytes()).hexdigest(),author_report_sha256=hashlib.sha256((author/'report.json').read_bytes()).hexdigest(),host_build=json.loads((out/'supervisor/build.json').read_text()),result=json.loads((out/'result.json').read_text()))
except BaseException as error:
 report['failure']={'type':type(error).__name__,'message':str(error)}
 raise
finally:
 primary_failure=sys.exc_info()[1]
 cleanup_errors=[]
 def cleanup(label,operation):
  try:operation()
  except Exception as error:cleanup_errors.append({'operation':label,'error':repr(error)})
 if sampler and args.pause_sampler:
  def sampler_fallback():
   report['samplerFallbackUsed']=not sampler.exited()
   if report['samplerFallbackUsed']:sampler.terminate()
  cleanup('sampler-fallback',sampler_fallback)
 if source_job:cleanup('source-job-close',lambda:check(job_kernel.CloseHandle(source_job)))
 for child in reversed(children):
  if child.poll() is None:cleanup('child-terminate-'+str(child.pid),child.terminate)
  cleanup('child-wait-'+str(child.pid),lambda child=child:child.wait(timeout=5))
 if sampler:
  cleanup('sampler-exit-observation',lambda:report.update(samplerExited=sampler.exited(2)))
  cleanup('sampler-handle-close',sampler.close)
 if source_child:
  cleanup('source-child-exit-observation',lambda:report.update(sourceChildExited=source_child.exited(3)))
  cleanup('source-child-handle-close',source_child.close)
 report['childrenExited']=all(p.poll() is not None for p in children)
 report['cleanupErrors']=cleanup_errors
 save_report(out/'report.json',report,primary_failure)
 try:print(out,flush=True)
 except Exception:
  if primary_failure is None:raise
assert not report['cleanupErrors'] and report['childrenExited'] and report.get('sourceChildExited',True) and report.get('samplerExited',False)
