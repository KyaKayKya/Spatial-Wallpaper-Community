"""Formal Launch desktop role/control integration; no UI, pixels, RC or perf claim."""
from pathlib import Path
import argparse,ctypes,hashlib,json,shutil,subprocess,sys,time,uuid
from ctypes import wintypes
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/content'))
from run_e2e import Client
sys.path.insert(0,str(ROOT/'Validation/Dependencies/python'))
import psutil

def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def until(check,label,seconds=40):
 end=time.monotonic()+seconds
 while time.monotonic()<end:
  value=check()
  if value:return value
  time.sleep(.05)
 raise TimeoutError(label)
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--launcher',type=Path,required=True);p.add_argument('--profile',type=Path,required=True);p.add_argument('--sdk-runtime',type=Path);p.add_argument('--packaged-dependencies',action='store_true');p.add_argument('--legacy-switches',action='store_true');p.add_argument('--sound-rpc-failure',action='store_true');a=p.parse_args()
 if a.sound_rpc_failure and not a.legacy_switches:p.error('--sound-rpc-failure requires --legacy-switches')
 if a.packaged_dependencies:
  if a.sdk_runtime:p.error('--packaged-dependencies uses the Launch directory runtime')
  a.sdk_runtime=a.launcher.resolve().parent/'sdk-runtime'
 elif not a.sdk_runtime:p.error('--sdk-runtime is required unless --packaged-dependencies is used')
 launcher=a.launcher.resolve();out=ROOT/'Validation/Content/sdk-runtime/native-render/task3/native-runtime'/uuid.uuid4().hex[:12];out.mkdir(parents=True);print(out,flush=True)
 desktop=out/'desktop';calibration=out/'calibration';(calibration/'presets').mkdir(parents=True);profile=read(a.profile);shutil.copyfile(a.profile,calibration/'presets'/(profile['id']+'.json'))
 common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent
 sources=[Path(__file__).resolve(),*[ROOT/'Prototype/Assets/Desktop'/n for n in ['DesktopNativeAdapter.cs','DesktopRuntime.cs','DesktopContentAdapter.cs','Core/DesktopAppliedStore.cs','Core/NativeDesktopPose.cs','NativeRenderInstance.cs']]]
 hashes={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
 report={'status':'failed','scope':__doc__,'checks':[],'source_sha256':hashes,'sdk_runtime_dependency':str(a.sdk_runtime.resolve()),'sdk_runtime_manifest_sha256':hashlib.sha256((a.sdk_runtime/'runtime.json').read_bytes()).hexdigest(),'launcher_sha256':hashlib.sha256(launcher.read_bytes()).hexdigest(),'assembly_sha256':hashlib.sha256((launcher.parent/'SpatialWallpaper_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()}
 tracker_root=launcher.parent/'Tracker' if a.packaged_dependencies else ROOT/'tracker'
 report['packaged_dependencies']=a.packaged_dependencies
 report['tracker_source_sha256']={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (tracker_root/'src').rglob('*.py')}
 process=None;client=None;children=[]
 def state():return client.call('GetDesktopState',{})
 def ready_native(artwork,display):
  s=state();targets=[x for x in s['native_targets'] if x['display_id']==display]
  return s if len(targets)==1 and targets[0]['attached'] and targets[0]['state'].get('artwork')==artwork and targets[0]['state'].get('frame',0)>10 else None
 def launch(stage):
  nonlocal process,client
  command=[str(launcher),'--desktop-runtime','true','--desktop-data-root',str(desktop),'--data-root',str(calibration),'--tracking-scenario','fixed','--native-presentation-diagnostics','true','-screen-fullscreen','0','-screen-width','800','-screen-height','450','-logFile',str(out/(stage+'.log'))]
  if not a.packaged_dependencies:command+=['--tracking-python',str(common/'tracker/.venv/Scripts/python.exe'),'--tracking-root',str(tracker_root),'--sdk-runtime',str(a.sdk_runtime.resolve())]
  process=subprocess.Popen(command)
  descriptor=desktop/'runtime/connection.json'
  def discovery():
   if process.poll() is not None:raise RuntimeError('Launch exited '+str(process.returncode))
   try:
    d=read(descriptor)
    return d if psutil.pid_exists(d['pid']) else None
   except (OSError,ValueError):return None
  data=until(discovery,'formal Launch runtime discovery');client=Client(data);until(lambda:state().get('ready'),'formal Runtime ready',70);return state()
 def pin(s,display):
  native=next(x['state'] for x in s['native_targets'] if x['display_id']==display);child=psutil.Process(native['pid']);child.create_time();children.append(child);return native,child
 def quit_runtime():
  nonlocal client
  result=client.call('QuitDesktop',{});assert result['succeeded'];client.close();client=None;assert process.wait(timeout=40)==0
 try:
  s=launch('first');display=profile['manual']['display_id'];assert any(d['id']==display for d in s['displays']),'fixture display offline'
  assert not s['applied_state']['targets'] and not s['camera_started'];report['checks'].append('formal Launch empty state and no Tracker')
  assert not s.get('error'),s.get('error')
  client.call('DesktopCalibrationSelection',{'selected':profile['id'],'applied':profile,'pause_when_invisible':False})
  request={'artwork_id':'showcase','display_id':display,'preset_id':profile['id'],'preset_revision':profile['revision']}
  result=client.call('ApplyDesktop',request);assert result['succeeded'],result;s=until(lambda:ready_native('showcase',display),'actual native Showcase ready');first,first_pin=pin(s,display)
  assert first['pid']!=s['pid'] and first['render_errors']==0 and len(s['applied_state']['targets'])==1;report['showcase_native']=first;report['runtime_pid']=s['pid'];report['checks'].append('formal Runtime owns actual full native Showcase renderer')
  client.call('ConfigureDesktopCamera',{'camera_id':'simulation','camera_name':'Native integration fixed simulation','mode_id':None,'simulate':True})
  def tracked():
   parent=client.call('GetDesktopTracking',{'consume':False,'preview':False});report['last_tracking_observation']=parent;native=parent['native_targets'][0]['state'];raw=parent.get('tracking')
   return parent if parent['camera_started'] and raw and native.get('tracking_fresh') and native.get('tracking_stream')==raw['sample']['stream_id'] else None
  tracked_state=until(tracked,'original Tracker fresh derived metadata reaches actual native renderer');trackers=[p for p in psutil.Process(tracked_state['pid']).children(recursive=True) if p.name().lower() in ['python.exe','pythonw.exe']]
  report['tracker_processes']=[{'pid':p.pid,'parent':p.ppid(),'image':p.exe(),'cwd':p.cwd(),'args':p.cmdline()} for p in trackers]
  roots=[p for p in trackers if p.ppid()==tracked_state['pid']];assert len(roots)==1
  # Windows venv python.exe is a redirector and retains the base interpreter as
  # its child. Count the original Tracker launch tree, not just image names.
  assert all(p.pid==roots[0].pid or roots[0].pid in [q.pid for q in p.parents()] for p in trackers)
  assert all(Path(p.cwd()).resolve()==tracker_root.resolve() for p in trackers);report['tracker_pid']=roots[0].pid;report['tracked_native']=tracked_state['native_targets'][0]['state'];report['checks'].append('one original Tracker launch tree sends fresh derived metadata; no Tracker in renderer')
  assert client.call('PauseDesktop',{'display_id':display,'paused':True})['applied_state']['targets'][0]['paused']
  def paused():
   v=state()['native_targets'][0]['state'];return v if v.get('paused') and v.get('time_scale')==0 and v.get('audio_paused') else None
  held=until(paused,'actual native animation/audio pause');time.sleep(1.4);assert state()['native_targets'][0]['state']['frame']>held['frame']+5
  client.call('PauseDesktop',{'display_id':display,'paused':False});until(lambda:not state()['native_targets'][0]['state']['paused'],'target resumes');report['checks'].append('per-target persisted pause and native frames continue')
  before=state();bad=dict(request,preset_revision=profile['revision']+1);failed=client.call('ApplyDesktop',bad);assert not failed['succeeded'];after=state();assert after['native_targets'][0]['state']['pid']==first['pid'] and after['applied_state']==before['applied_state'];report['checks'].append('invalid exact preset rejected without replacing original owner')
  # Hold only our own test data file without FILE_SHARE_DELETE. The candidate
  # must fail its real atomic replace and restore the previous exact scene.
  kernel=ctypes.WinDLL('kernel32',use_last_error=True);kernel.CreateFileW.argtypes=[wintypes.LPCWSTR,wintypes.DWORD,wintypes.DWORD,wintypes.LPVOID,wintypes.DWORD,wintypes.DWORD,wintypes.HANDLE];kernel.CreateFileW.restype=wintypes.HANDLE;kernel.CloseHandle.argtypes=[wintypes.HANDLE]
  before=state();old_bytes=(desktop/'applied.json').read_bytes();locked=kernel.CreateFileW(str(desktop/'applied.json'),0x80000000,1,None,3,0,None);assert locked not in [None,ctypes.c_void_p(-1).value]
  try:failed=client.call('ApplyDesktop',dict(request,artwork_id='pool'));assert not failed['succeeded'],failed
  finally:assert kernel.CloseHandle(locked)
  s=until(lambda:ready_native('showcase',display),'failed atomic save restores exact Showcase');recovered,recovered_pin=pin(s,display);first_pin.wait(timeout=10);assert s['applied_state']==before['applied_state'] and (desktop/'applied.json').read_bytes()==old_bytes
  owned=[p for p in psutil.Process(s['pid']).children() if '--native-render-session' in p.cmdline()];assert len(owned)==1 and owned[0].pid==recovered['pid'];report['checks'].append('actual atomic-save denial restores exact old scene, file and sole renderer owner')
  request['artwork_id']='pool';assert client.call('ApplyDesktop',request)['succeeded'];s=until(lambda:ready_native('pool',display),'native Pool switch');second,second_pin=pin(s,display);assert second['pid']!=first['pid'];first_pin.wait(timeout=10);assert second['render_errors']==0;report['pool_native']=second;report['checks'].append('same-target switch releases original renderer and keeps exact applied table')
  if a.legacy_switches:
   content=client.call('GetContentState',{});bundled=content['bundled_catalog'];assert len(bundled)==5,bundled
   report['legacy_switches']=[]
   for original,item in bundled.items():
    work=item['work_id'];result=client.call('ApplyDesktop',dict(request,artwork_id=work));assert result['succeeded'],result
    s=state();assert s['content_ready'] and s['active_artwork']==work and not s['native_targets'];second_pin.wait(timeout=10)
    applied=s['applied_state']['targets'][0];assert applied['artwork_id']==work and applied['content_revision']==s['content_revision']
    report['legacy_switches'].append({'original_id':original,'work_id':work,'revision_id':s['content_revision']})
   report['checks'].append('five existing SDK works switch through formal Runtime without retaining native owner')
   mix={'music':.23,'effects':.71,'music_muted':True,'effects_muted':False}
   client.call('SetSoundMix',{'work_id':work,'mix':mix});before=state();old_bytes=(desktop/'applied.json').read_bytes()
   assert before['applied_state']['targets'][0]['sound']==mix
   locked=kernel.CreateFileW(str(desktop/'applied.json'),0x80000000,1,None,3,0,None);assert locked not in [None,ctypes.c_void_p(-1).value]
   try:
    try:client.call('SetSoundMix',{'work_id':work,'mix':dict(mix,music=.9)});raise RuntimeError('sound save denial accepted')
    except AssertionError as failure:report['sound_save_denial']=str(failure)
   finally:assert kernel.CloseHandle(locked)
   sound=client.call('GetContentState',{});assert sound['sound_mixes'][work]==mix and state()['applied_state']==before['applied_state'] and (desktop/'applied.json').read_bytes()==old_bytes
   report['checks'].append('actual applied-save denial compensates original sound RPC and keeps old exact mix')
   # An update changes the installed latest revision, never the playing one.
   staging=desktop/'content/staging';stage_id=str(uuid.uuid4());shutil.copy2(launcher.parent/'SpatialWallpaper_Data/StreamingAssets/sdk-content/Train.spatial',staging/(stage_id+'.spatial'))
   old_revision=state()['content_revision'];updated=client.call('UpdateContent',{'staging_id':stage_id,'work_id':work,'expected_revision':content['state']['works'][work]['latest_revision']});assert updated['revision_id']!=old_revision
   assert state()['content_revision']==old_revision and state()['applied_state']['targets'][0]['content_revision']==old_revision
   report['checks'].append('installed SDK update preserves exact playing revision until explicit apply')
   if a.sound_rpc_failure:
    # End only this fixture's retained, authenticated Preview kernel. The
    # original Runtime must compensate persistent sound even when both RPCs
    # fail; its normal crash-cleanup contract must still drain the Host.
    viewers=[q for q in psutil.Process(state()['pid']).children() if Path(q.exe()).resolve()==(launcher.parent/'sdk-runtime/SpatialWallpaper.Preview.exe').resolve()];assert len(viewers)==1
    viewer=viewers[0];status=next(read(q) for q in (desktop/'content/sdk-runtime/launches').glob('*/status.json') if read(q)['viewer_pid']==viewer.pid)
    kernel.OpenProcess.argtypes=[wintypes.DWORD,wintypes.BOOL,wintypes.DWORD];kernel.OpenProcess.restype=wintypes.HANDLE
    kernel.GetProcessTimes.argtypes=[wintypes.HANDLE,*[ctypes.POINTER(wintypes.FILETIME)]*4];kernel.TerminateProcess.argtypes=[wintypes.HANDLE,wintypes.UINT];kernel.WaitForSingleObject.argtypes=[wintypes.HANDLE,wintypes.DWORD]
    handle=kernel.OpenProcess(0x101001,False,viewer.pid);assert handle
    try:
     created,exited,cpu_kernel,cpu_user=[wintypes.FILETIME() for _ in range(4)];assert kernel.GetProcessTimes(handle,ctypes.byref(created),ctypes.byref(exited),ctypes.byref(cpu_kernel),ctypes.byref(cpu_user))
     assert (created.dwHighDateTime<<32|created.dwLowDateTime)==status['viewer_creation_filetime'] and viewer.ppid()==state()['pid']
     report['sound_rpc_failed_viewer']={'pid':viewer.pid,'creation_filetime':status['viewer_creation_filetime']};assert kernel.TerminateProcess(handle,93);assert kernel.WaitForSingleObject(handle,5000)==0
    finally:assert kernel.CloseHandle(handle)
    try:client.call('SetSoundMix',{'work_id':work,'mix':dict(mix,music=.9)});raise RuntimeError('dead sound RPC accepted')
    except AssertionError as failure:report['sound_rpc_failure']=str(failure)
    assert read(desktop/'content/sound-mixes.json')[work]==mix and state()['applied_state']['targets'][0]['sound']==mix
    assert '声音设置与已应用项未恢复一致' in state()['error'];report['checks'].append('actual ended Preview rejects sound RPC and compensation RPC, restores persistent mix and exposes explicit failure')
   assert client.call('ApplyDesktop',request)['succeeded'];s=until(lambda:ready_native('pool',display),'legacy to native Pool');second,second_pin=pin(s,display)
   assert s['active_artwork']=='pool' and not s['content_ready'];report['checks'].append('SDK to native source switch returns sole target ownership')
  expected=read(desktop/'applied.json');assert expected['format']==2 and expected['targets'][0]['artwork_id']=='pool';(out/'expected-applied.json').write_text(json.dumps(expected,indent=2),encoding='utf-8')
  report['online_displays']=s['displays'];report['physical_multi_target']='not-run-one-online-display' if len(s['displays'])<2 else 'not-run-by-this-single-target-fixture'
  quit_runtime();second_pin.wait(timeout=10)
  for tracker in trackers:tracker.wait(timeout=10)
  report['checks'].append('normal formal quit releases original renderer and sole Tracker tree without clearing applied item')
  s=launch('restart');s=until(lambda:ready_native('pool',display),'exact restart restores native Pool');restored,restored_pin=pin(s,display);assert s['applied_state']==expected and not s['camera_started'];report['restored_native']=restored;report['checks'].append('formal Launch restart restores exact format2 item and profile without Tracker')
  assert client.call('StopDesktop',{'display_id':display})['succeeded'];restored_pin.wait(timeout=10);s=state();assert not s['native_targets'] and not s['applied_state']['targets'] and s['active_artwork'] is None;report['checks'].append('per-target stop drains owned renderer and removes only applied target')
  quit_runtime();report['status']='passed-formal-Launch-native-control-development-only'
 except Exception as failure:report['error']=repr(failure);raise
 finally:
  if client:
   try:client.call('QuitDesktop',{})
   except Exception as failure:report['cleanup_error']=repr(failure)
   client.close()
  if process and process.poll() is None:
   try:process.wait(timeout=40)
   except subprocess.TimeoutExpired:report['cleanup_unproven']=True
  live=[]
  for child in children:
   try:child.wait(timeout=5)
   except psutil.TimeoutExpired:live.append(child.pid)
  report['owned_renderer_pins_still_live']=live
  if live:report['status']='failed-cleanup-unproven'
  report['sources_unchanged']=hashes=={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in sources}
  if not report['sources_unchanged']:report['status']='failed-sources-changed-during-check'
  if report.get('cleanup_error') or report.get('cleanup_unproven'):report['status']='failed-cleanup-unproven'
  (out/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(report['status'],out/'report.json',flush=True)
  if sys.exc_info()[0] is None and report['status']!='passed-formal-Launch-native-control-development-only':raise RuntimeError('native Runtime final verification failed: '+report['status'])
if __name__=='__main__':main()
