"""Convert real C# audio author and run it in ordinary Unity Play with a selected source."""
import os,subprocess,uuid,json,hashlib,sys,time,ctypes,argparse
from build_execution import ROOT,UNITY,create
from provenance import sources
from timed_output import ready_line
from process_observation import observe_child
sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from native_build import compile_probe
parser=argparse.ArgumentParser();parser.add_argument('--stable-zero',action='store_true');parser.add_argument('--negative-stability-verdict',action='store_true');args=parser.parse_args()
if args.negative_stability_verdict and not args.stable_zero:parser.error('negative stability verdict requires --stable-zero')
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/E/audio-author'/run;out.mkdir(parents=True)
project=ROOT/'Validation/Sdk'/('u'+run[:10]);create(project,[('StageEAudioAuthor.cs',False),('StageEAudioExport.cs',True),('StageEAudioPlay.cs',True)])
receipt=sources();env=os.environ.copy();env.update(SDK_E_OUTPUT=str(out),SDK_E_RUN=run,SDK_E_AUDIO_STABLE='1' if args.stable_zero else '0',SDK_E_AUDIO_BAD_VERDICT='1' if args.negative_stability_verdict else '0');env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));print(out,flush=True)
subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_supervisor.py'),'--play-broker','--output',str(out/'broker')],check=True)
compile_probe(ROOT/'tools/sdk/experiments/process_audio_capture.cpp',out/'Tone.exe')
subprocess.run([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageEAudioExport.Run','-logFile',str(out/'conversion.log')],env=env,timeout=900,check=True)
children=[];sampler=None;report={'status':'not-complete','sources':receipt,'stableZeroPackage':args.stable_zero,'negativeStabilityVerdictPackage':args.negative_stability_verdict,'playScope':'ordinary Play through first clear; stability window verified separately in Host'}
try:
 tone=subprocess.Popen([str(out/'Tone.exe'),'--tone-right-long','80'],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);children.append(tone);assert json.loads(ready_line(tone,5))['ready']
 kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.GetProcessTimes.argtypes=[ctypes.c_void_p,*([ctypes.POINTER(ctypes.c_ulonglong)]*4)];kernel.GetProcessTimes.restype=ctypes.c_int
 times=[ctypes.c_ulonglong() for _ in range(4)];assert kernel.GetProcessTimes(int(tone._handle),*[ctypes.byref(v) for v in times])
 (out/'audio.json').write_text(json.dumps({'process':tone.pid,'created':str(times[0].value)}));env.update(SDK_PLAY_BROKER=str(out/'broker/PlayBroker.exe'),SDK_PLAY_STORE=str(out/'play-storage'),SDK_PLAY_AUDIO_CONFIG=str(out/'audio.json'));env.pop('SDK_PLAY_LLM_CONFIG',None)
 editor=subprocess.Popen([UNITY,'-batchmode','-projectPath',str(project),'-executeMethod','StageEAudioPlay.Run','-logFile',str(out/'play.log')],env=env);children.append(editor)
 deadline=time.monotonic()+60
 while not (out/'heard').exists():
  assert editor.poll() is None,'Unity exited before audio was observed'
  if time.monotonic()>deadline:raise TimeoutError('ordinary Play audio deadline')
  time.sleep(.1)
 broker=observe_child(editor.pid,'PlayBroker.exe');sampler=observe_child(broker.pid,'AudioSampler.exe');broker.close()
 began=time.perf_counter();tone.terminate();tone.wait(timeout=3);assert sampler.exited(2),'Play sampler survived source termination';assert editor.wait(timeout=20)==0
 report['sourceTerminationToEditorExitObservedMs']=(time.perf_counter()-began)*1000
 play=json.loads((out/'play.json').read_text(encoding='utf-8-sig'));assert play['run_id']==run and play['phase']==4 and 0<play['peak']<=1
 assert receipt==sources(),'audio author source drift'
 report.update(status='partial-audio-CSharp-Play-only',play=play,author=json.loads((out/'author.json').read_text(encoding='utf-8-sig')),package_sha256=hashlib.sha256((out/'scene.spatial').read_bytes()).hexdigest())
finally:
 for child in children:
  if child.poll() is None:child.terminate()
  child.wait(timeout=5)
 if sampler:
  report['samplerExited']=sampler.exited(2);sampler.close()
 report['childrenExited']=all(p.poll() is not None for p in children)
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out,flush=True)
