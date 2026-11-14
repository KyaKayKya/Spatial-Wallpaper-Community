"""Stop Play, explicitly reload Unity scripts in edit mode, then restart and hear audio."""
import os,subprocess,uuid,json,sys,time,ctypes,argparse
from build_execution import ROOT,UNITY,create,digest
from provenance import sources
from timed_output import ready_line
from process_observation import observe_child,k
sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from native_build import compile_probe
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--reuse-project');parser.add_argument('--negative-skip-reload',action='store_true');options=parser.parse_args()
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/E/audio-play-reload'/run;out.mkdir(parents=True)
project=ROOT/'Validation/Sdk'/('r'+run[:10])
if options.reuse_project:
 project=(ROOT/options.reuse_project).resolve()
 assert project.parent==(ROOT/'Validation/Sdk').resolve() and (project/'Assets/Editor/StageEAudioReloadPlay.cs').is_file(),'only reuse an owned reload fixture project'
create(project,[('StageEAudioFailureAuthor.cs',False),('StageEAudioReloadPlay.cs',True)])
receipt=sources();report={'status':'not-complete','run_id':run,'sources':receipt,'negativeSkipReload':options.negative_skip_reload};print(out,flush=True)
env=os.environ.copy();env.update(SDK_E_OUTPUT=str(out),SDK_E_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
env['SDK_E_SKIP_RELOAD']='1' if options.negative_skip_reload else '0'
children=[];observed={}
def marker(name,seconds):
 deadline=time.monotonic()+seconds
 while not (out/name).exists():
  assert editor.poll() is None,'Editor exited before '+name
  if time.monotonic()>deadline:
   report.update(markerDeadline=name,sourceAliveAtDeadline=tone.poll() is None)
   for label,child in observed.items():report[label+'ExitedAtDeadline']=child.exited()
   raise TimeoutError('Play reload marker deadline: '+name)
  time.sleep(.01)
try:
 subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_supervisor.py'),'--play-broker','--output',str(out/'broker')],check=True)
 compile_probe(ROOT/'tools/sdk/experiments/process_audio_capture.cpp',out/'Tone.exe')
 subprocess.run([UNITY,'-batchmode','-quit','-projectPath',str(project),'-logFile',str(out/'import.log')],env=env,timeout=900,check=True)
 tone=subprocess.Popen([str(out/'Tone.exe'),'--tone-right-long','80'],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True);children.append(tone);assert json.loads(ready_line(tone,5))['ready']
 times=[ctypes.c_ulonglong() for _ in range(4)];assert k.GetProcessTimes(int(tone._handle),*[ctypes.byref(v) for v in times])
 (out/'audio.json').write_text(json.dumps({'process':tone.pid,'created':str(times[0].value)}));env.update(SDK_PLAY_BROKER=str(out/'broker/PlayBroker.exe'),SDK_PLAY_STORE=str(out/'play-storage'),SDK_PLAY_AUDIO_CONFIG=str(out/'audio.json'));env.pop('SDK_PLAY_LLM_CONFIG',None)
 editor=subprocess.Popen([UNITY,'-batchmode','-projectPath',str(project),'-executeMethod','StageEAudioReloadPlay.Run','-logFile',str(out/'play.log')],env=env);children.append(editor)
 marker('heard',60);observed['oldBroker']=observe_child(editor.pid,'PlayBroker.exe');observed['oldSampler']=observe_child(observed['oldBroker'].pid,'AudioSampler.exe')
 began=time.perf_counter();(out/'reload-request').write_text('request actual Unity script reload')
 marker('entered-edit',20)
 assert observed['oldBroker'].exited(2) and observed['oldSampler'].exited(2),'old Play children survived reload'
 report['stopRequestToOldChildrenExitObservedMs']=(time.perf_counter()-began)*1000
 marker('reloaded',20)
 marker('reload-heard',10)
 observed['newBroker']=observe_child(editor.pid,'PlayBroker.exe');observed['newSampler']=observe_child(observed['newBroker'].pid,'AudioSampler.exe')
 assert not observed['newBroker'].exited() and not observed['newSampler'].exited(),'new session already exited'
 assert tone.poll() is None,'selected source ended during reload'
 (out/'finish').write_text('new session verified');assert editor.wait(timeout=20)==0
 play=json.loads((out/'play.json').read_text(encoding='utf-8-sig'));assert play['run_id']==run and play['framesAfterReload']>=10 and play['peakAfterReload']>0
 assert not options.negative_skip_reload,'accepted missing explicit reload'
 assert 'SDK ordinary Play capability session is not running' not in (out/'play.log').read_text(encoding='utf-8-sig',errors='replace'),'author session missing during restart'
 assert receipt==sources(),'reload source drift'
 report.update(status='partial-ordinary-Play-audio-stop-reload-restart',play=play,sourceAliveAfterEditorExit=tone.poll() is None,broker_sha256=digest(out/'broker/PlayBroker.exe'))
finally:
 for child in reversed(children):
  if child.poll() is None:child.terminate()
  child.wait(timeout=5)
 for name,child in observed.items():report[name+'Exited']=child.exited(2);child.close()
 for child in children:
  if child.stdout:child.stdout.close()
  if child.stderr:child.stderr.close()
 report['childrenExited']=all(p.poll() is not None for p in children)
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out,flush=True)
assert all(report[key] is True for key in ['oldBrokerExited','oldSamplerExited','newBrokerExited','newSamplerExited','childrenExited','sourceAliveAfterEditorExit'])
