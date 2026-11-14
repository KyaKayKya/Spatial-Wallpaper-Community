"""Real interactive Unity Game View input with the ordinary author API."""
import os,subprocess,uuid,json,sys,argparse,time,ctypes
from pathlib import Path
from build_execution import ROOT,UNITY,create
from provenance import sources
from process_observation import observe_child
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--reuse-project');parser.add_argument('--negative-no-adapter',action='store_true');parser.add_argument('--reload',action='store_true');parser.add_argument('--negative-skip-reload',action='store_true');options=parser.parse_args()
assert not (options.reload and options.negative_no_adapter),'reload and missing-adapter are separate acceptance cases'
assert not options.negative_skip_reload or options.reload,'skip-reload negative requires --reload'
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/E/native-input-play'/run;out.mkdir(parents=True);project=ROOT/'Validation/Sdk'/('n'+run[:10])
if options.reuse_project:
 project=(ROOT/options.reuse_project).resolve();assert project.parent==(ROOT/'Validation/Sdk').resolve() and (project/'Assets/Editor/StageENativeInputPlay.cs').is_file(),'only reuse owned native input fixture'
create(project,[('StageENativeInputAuthor.cs',False),('StageENativeInputPlay.cs',True)])
receipt=sources();report={'status':'not-complete','run_id':run,'sources':receipt};print(out,flush=True)
env=os.environ.copy();env.update(SDK_E_OUTPUT=str(out),SDK_E_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));editor=None;broker=None;second_broker=None
user=ctypes.WinDLL('user32');user.GetForegroundWindow.restype=ctypes.c_void_p;user.SetForegroundWindow.argtypes=[ctypes.c_void_p];previous=user.GetForegroundWindow()
class Point(ctypes.Structure):_fields_=[('x',ctypes.c_long),('y',ctypes.c_long)]
class MouseInput(ctypes.Structure):_fields_=[('x',ctypes.c_long),('y',ctypes.c_long),('data',ctypes.c_uint32),('flags',ctypes.c_uint32),('time',ctypes.c_uint32),('extra',ctypes.c_size_t)]
class KeyboardInput(ctypes.Structure):_fields_=[('key',ctypes.c_uint16),('scan',ctypes.c_uint16),('flags',ctypes.c_uint32),('time',ctypes.c_uint32),('extra',ctypes.c_size_t)]
class InputPayload(ctypes.Union):_fields_=[('mouse',MouseInput),('keyboard',KeyboardInput)]
class NativeInput(ctypes.Structure):_fields_=[('type',ctypes.c_uint32),('payload',InputPayload)]
user.SendInput.argtypes=[ctypes.c_uint32,ctypes.POINTER(NativeInput),ctypes.c_int];user.SendInput.restype=ctypes.c_uint32
cursor=Point();user.GetCursorPos(ctypes.byref(cursor))
try:
 subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_supervisor.py'),'--play-broker','--output',str(out/'broker')],check=True)
 subprocess.run([UNITY,'-batchmode','-quit','-projectPath',str(project),'-logFile',str(out/'import.log')],env=env,timeout=900,check=True)
 env.update(SDK_E_NATIVE_INPUT='1',SDK_E_INPUT_RELOAD='1' if options.reload else '0',SDK_E_SKIP_INPUT_RELOAD='1' if options.negative_skip_reload else '0',SDK_E_NO_INPUT_ADAPTER='1' if options.negative_no_adapter else '0',SDK_PLAY_BROKER=str(out/'broker/PlayBroker.exe'),SDK_PLAY_STORE=str(out/'play-storage'));env.pop('SDK_PLAY_AUDIO_CONFIG',None);env.pop('SDK_PLAY_LLM_CONFIG',None)
 editor=subprocess.Popen([UNITY,'-projectPath',str(project),'-executeMethod','StageENativeInputPlay.Run','-logFile',str(out/'play.log')],env=env);report['editor_pid']=editor.pid;report['project']=str(project);deadline=time.monotonic()+100
 while not (out/'ready').exists():
  assert editor.poll() is None and time.monotonic()<deadline,'Game View fixture did not become ready';time.sleep(.02)
 broker=observe_child(editor.pid,'PlayBroker.exe')
 if options.reload:
  while not (out/'ready-second').exists():
   assert editor.poll() is None and time.monotonic()<deadline,'native input did not restart after stop';time.sleep(.02)
  assert broker.exited(2),'first Play Broker survived stop/reload'
  second_broker=observe_child(editor.pid,'PlayBroker.exe')
 code=editor.wait(timeout=90)
 if options.negative_no_adapter:
  assert code!=0 and (out/'clicked').exists() and 'ordinary-Play-real-click-did-not-acquire-input' in (out/'failure.txt').read_text() and not (out/'play.json').exists()
  report.update(status='expected-missing-Play-native-adapter-rejected')
 else:
  assert code==0;result=json.loads((out/'play.json').read_text(encoding='utf-8-sig'));assert result['run_id']==run and result['native_click'] and result['native_space'] and result['downs']==1 and result['ups']==1 and result['same_frame_stable'] and result['toolbar_excluded'] and result['other_editor_window_excluded']
  if options.reload:
   assert all((out/name).exists() for name in ['active-before-stop','entered-edit','restarted-input'])
   events=[json.loads(line) for line in (out/'reload-events.jsonl').read_text(encoding='utf-8-sig').splitlines()]
   assert events and all(event['process']==editor.pid and event['asset_import_worker'] is False and event['run_id']==run and Path(event['project']).resolve()==(project/'Assets').resolve() for event in events),'reload observations must belong to the launched main Editor and this run'
   assert (out/'reload-events.jsonl').read_bytes()==(out/f'reload-events-process-{editor.pid}.jsonl').read_bytes(),'main Editor observation copies differ'
   assert [event['sequence'] for event in events]==list(range(1,len(events)+1)),'reload observation sequence incomplete'
   assert {event['event'] for event in events if event['restarted']}=={'before','after'},'second Play automatic reload must remain observable'
   explicit=[event for event in events if event['requested'] and not event['playing'] and not event['restarted']]
   if options.negative_skip_reload:
    assert result.get('reload_count')==0 and (out/'reload-skipped').exists() and not explicit and not any((out/name).exists() for name in ['before-reload','after-reload']),'negative must observe automatic reload without claiming explicit reload'
   else:
    assert result.get('reload_count')==1 and [event['event'] for event in explicit]==['before','after'],'native input explicit edit-mode reload evidence missing'
    assert explicit==[json.loads((out/name).read_text(encoding='utf-8-sig')) for name in ['before-reload','after-reload']],'explicit markers do not match observed events'
   report['reloadEvents']=events
  assert 'Exception:' not in (out/'play.log').read_text(encoding='utf-8-sig',errors='replace')
  report.update(status='expected-missing-explicit-input-reload-rejected' if options.negative_skip_reload else 'partial-native-ordinary-Play-input-reload' if options.reload else 'partial-native-ordinary-Play-input',result=result)
 assert sources()==receipt
finally:
 if editor is not None and editor.poll() is None:editor.terminate();editor.wait(10)
 if (out/'key-state').exists() and (out/'key-state').read_text(encoding='utf-8-sig')=='down':
  release=NativeInput(type=1,payload=InputPayload(keyboard=KeyboardInput(key=32,flags=2)))
  report['emergencyKeyRelease']=user.SendInput(1,ctypes.byref(release),ctypes.sizeof(release))==1
 if broker is not None:report['brokerExited']=broker.exited(2);broker.close()
 if second_broker is not None:report['secondBrokerExited']=second_broker.exited(2);second_broker.close()
 report['editorExited']=editor is not None and editor.poll() is not None
 user.SetCursorPos(cursor.x,cursor.y)
 if previous:user.SetForegroundWindow(previous)
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out,flush=True)
assert report['brokerExited'] and report['editorExited']
if options.reload:assert report.get('secondBrokerExited')
