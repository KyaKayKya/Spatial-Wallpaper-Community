"""Real ordinary Unity Play audio facade after abrupt test-owned Broker termination."""
import os,subprocess,uuid,json,sys,time,ctypes,argparse
from build_execution import ROOT,UNITY,create,digest
from provenance import sources
from timed_output import ready_line
from process_observation import observe_child,k
sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from native_build import compile_probe

parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--negative-keep-broker',action='store_true',help='Expected failure: leave Broker and tone alive so clear oracle must reject');options=parser.parse_args()
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/E/audio-play-failure'/run;out.mkdir(parents=True)
project=ROOT/'Validation/Sdk'/('f'+run[:10]);create(project,[('StageEAudioFailureAuthor.cs',False),('StageEAudioFailurePlay.cs',True)])
receipt=sources();report={'status':'not-complete','run_id':run,'sources':receipt,'negativeKeepBroker':options.negative_keep_broker};print(out,flush=True)
env=os.environ.copy();env.update(SDK_E_OUTPUT=str(out),SDK_E_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
children=[];broker=None;sampler=None
try:
 subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_supervisor.py'),'--play-broker','--output',str(out/'broker')],check=True)
 compile_probe(ROOT/'tools/sdk/experiments/process_audio_capture.cpp',out/'Tone.exe')
 subprocess.run([UNITY,'-batchmode','-quit','-projectPath',str(project),'-logFile',str(out/'import.log')],env=env,timeout=900,check=True)
 tone=subprocess.Popen([str(out/'Tone.exe'),'--tone-right-long','80'],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);children.append(tone);assert json.loads(ready_line(tone,5))['ready']
 times=[ctypes.c_ulonglong() for _ in range(4)];assert k.GetProcessTimes(int(tone._handle),*[ctypes.byref(v) for v in times])
 (out/'audio.json').write_text(json.dumps({'process':tone.pid,'created':str(times[0].value)}));env.update(SDK_PLAY_BROKER=str(out/'broker/PlayBroker.exe'),SDK_PLAY_STORE=str(out/'play-storage'),SDK_PLAY_AUDIO_CONFIG=str(out/'audio.json'));env.pop('SDK_PLAY_LLM_CONFIG',None)
 editor=subprocess.Popen([UNITY,'-batchmode','-projectPath',str(project),'-executeMethod','StageEAudioFailurePlay.Run','-logFile',str(out/'play.log')],env=env);children.append(editor)
 deadline=time.monotonic()+180
 while not (out/'heard').exists():
  assert editor.poll() is None,'Unity exited before audio was observed'
  if time.monotonic()>deadline:raise TimeoutError('ordinary Play audio deadline')
  time.sleep(.1)
 broker=observe_child(editor.pid,'PlayBroker.exe',allow_terminate=True);sampler=observe_child(broker.pid,'AudioSampler.exe')
 began=time.perf_counter()
 if not options.negative_keep_broker:
  broker.terminate();assert broker.exited(2),'Broker termination failed'
 (out/'broker-killed').write_text('measurement gate; termination independently observed by driver')
 if not options.negative_keep_broker:
  assert sampler.exited(2),'Play sampler survived Broker crash'
  report['brokerTerminationToSamplerExitObservedMs']=(time.perf_counter()-began)*1000
 deadline=time.monotonic()+5
 while not (out/'cleared').exists():
  assert editor.poll() is None,'Unity exited before four-band clear observation'
  if time.monotonic()>deadline:
   report.update(clearDeadlineExceeded=True,sourceAliveAtDeadline=tone.poll() is None,brokerAliveAtDeadline=not broker.exited(),samplerAliveAtDeadline=not sampler.exited())
   raise TimeoutError('Broker crash audio clear deadline')
  time.sleep(.01)
 report['brokerTerminationToTwoSecondsZeroAuthorFramesObservedMs']=(time.perf_counter()-began)*1000
 assert editor.wait(timeout=20)==0,'Editor failed after Broker crash'
 assert tone.poll() is None,'source exited before author cleared'
 play=json.loads((out/'play.json').read_text(encoding='utf-8-sig'));assert play['run_id']==run and play['zeroFrames']>=10 and play['zeroSeconds']>=2 and 0<play['peak']<=1
 assert not options.negative_keep_broker,'clear oracle accepted a live Broker/source'
 assert receipt==sources(),'audio Play failure source drift'
 report.update(status='partial-audio-Play-Broker-crash-only',play=play,sourceAliveAfterEditorExit=True,broker_sha256=digest(out/'broker/PlayBroker.exe'),sampler_sha256=digest(out/'broker/AudioSampler.exe'))
finally:
 for child in reversed(children):
  if child.poll() is None:child.terminate()
  child.wait(timeout=5)
 for name,observed in [('broker',broker),('sampler',sampler)]:
  if observed:report[name+'Exited']=observed.exited(2);observed.close()
 for child in children:
  if child.stdout:child.stdout.close()
  if child.stderr:child.stderr.close()
 report['childrenExited']=all(p.poll() is not None for p in children)
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out,flush=True)
assert all(report.get(name) is True for name in ['brokerExited','samplerExited','childrenExited']),'test-owned processes survived cleanup'
