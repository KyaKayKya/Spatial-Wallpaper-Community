"""Launch the original GUI product and verify its bounded SDK integration flow."""
from pathlib import Path
import argparse,ctypes as c,hashlib,json,shutil,subprocess,sys,time,uuid
from contextlib import contextmanager
from ctypes import wintypes as w
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Validation/Dependencies/python'))
import psutil
from PIL import ImageGrab,Image,ImageStat
sys.path.insert(0,str(ROOT/'tools/content'))
from run_e2e import Client
from tray_roundtrip import windows
from check_builtin_visibility import signature,compare_signatures,observation
u=c.WinDLL('user32',use_last_error=True)
u.PostMessageW.argtypes=[w.HWND,w.UINT,w.WPARAM,w.LPARAM]
u.SetForegroundWindow.argtypes=[w.HWND];u.GetForegroundWindow.restype=w.HWND
u.GetWindowThreadProcessId.argtypes=[w.HWND,c.POINTER(w.DWORD)];u.GetWindowThreadProcessId.restype=w.DWORD
u.AttachThreadInput.argtypes=[w.DWORD,w.DWORD,w.BOOL]
u.FindWindowW.argtypes=[w.LPCWSTR,w.LPCWSTR];u.FindWindowW.restype=w.HWND
u.SendMessageW.argtypes=[w.HWND,w.UINT,w.WPARAM,w.LPARAM]
u.WindowFromPoint.argtypes=[w.POINT];u.WindowFromPoint.restype=w.HWND
u.GetAncestor.argtypes=[w.HWND,w.UINT];u.GetAncestor.restype=w.HWND
u.GetClassNameW.argtypes=[w.HWND,w.LPWSTR,c.c_int]
u.ShowWindow.argtypes=[w.HWND,c.c_int]
u.CreateWindowExW.argtypes=[w.DWORD,w.LPCWSTR,w.LPCWSTR,w.DWORD,c.c_int,c.c_int,c.c_int,c.c_int,w.HWND,w.HMENU,w.HINSTANCE,c.c_void_p];u.CreateWindowExW.restype=w.HWND
u.DestroyWindow.argtypes=[w.HWND]
u.UpdateWindow.argtypes=[w.HWND]
u.GetClientRect.argtypes=[w.HWND,c.POINTER(w.RECT)];u.ClientToScreen.argtypes=[w.HWND,c.POINTER(w.POINT)]

def read(path):return json.loads(path.read_text(encoding='utf-8-sig'))
@contextmanager
def exposed_desktop(display):
 # Some owned dialogs have no minimize action. Temporarily hide only windows
 # actually hit over the scene, and restore every one without activating it.
 hidden=[];u.SendMessageW(u.FindWindowW('Shell_TrayWnd',None),0x111,419,0)
 def refresh():
  for _ in range(4):
   for x in [.35,.5,.65,.8]:
    for y in [.15,.35,.55,.75,.9]:
     window=u.GetAncestor(u.WindowFromPoint(w.POINT(round(display['x']+display['width']*x),round(display['y']+display['height']*y))),2)
     name=c.create_unicode_buffer(256);u.GetClassNameW(window,name,256)
     if window and name.value not in ['Progman','WorkerW','Shell_TrayWnd','Shell_SecondaryTrayWnd']:
      if window not in hidden:hidden.append(window)
      u.ShowWindow(window,0)
   time.sleep(.1)
 try:
  refresh()
  yield refresh
 finally:
  for window in reversed(hidden):u.ShowWindow(window,4)
  u.SendMessageW(u.FindWindowW('Shell_TrayWnd',None),0x111,416,0)
@contextmanager
def blocked_scene_region(display):
 x=round(display['x']+display['width']*.32);y=round(display['y']+display['height']*.10)
 window=u.CreateWindowExW(0x08000088,'STATIC','Spatial Wallpaper visibility negative',0x90000004,x,y,round(display['width']*.48),round(display['height']*.8),None,None,None,None)
 assert window,'could not create owned visibility obstruction'
 u.UpdateWindow(window)
 try:time.sleep(.2);yield
 finally:assert u.DestroyWindow(window)
def until(condition,seconds,label):
 end=time.monotonic()+seconds
 while time.monotonic()<end:
  value=condition()
  if value:return value
  time.sleep(.05)
 raise TimeoutError(label)
def screenshot(pid,target):
 hwnd=windows(pid,'UnityWndClass')[0]
 def focus():
  if u.GetForegroundWindow()==hwnd:return True
  observed=w.DWORD();thread=u.GetWindowThreadProcessId(hwnd,c.byref(observed));assert observed.value==pid
  foreground=u.GetWindowThreadProcessId(u.GetForegroundWindow(),None);current=c.windll.kernel32.GetCurrentThreadId();attached=[]
  try:
   for other in {thread,foreground}-{current,0}:
    if u.AttachThreadInput(current,other,True):attached.append(other)
   u.ShowWindow(hwnd,9);u.SetForegroundWindow(hwnd)
  finally:
   for other in attached:u.AttachThreadInput(current,other,False)
  return u.GetForegroundWindow()==hwnd
 until(focus,10,'owned manager foreground before capture')
 rect=w.RECT();origin=w.POINT();assert u.GetClientRect(hwnd,c.byref(rect)) and u.ClientToScreen(hwnd,c.byref(origin))
 time.sleep(.15);ImageGrab.grab(bbox=(origin.x,origin.y,origin.x+rect.right,origin.y+rect.bottom),all_screens=True).save(target)
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--launcher',type=Path,default=ROOT/'Prototype/Builds/Library/Launch.exe');p.add_argument('--sdk-runtime',type=Path,help='explicit compatible runtime, forwarded through formal Launch and tray reopens');args=p.parse_args()
 launcher=args.launcher.resolve();exe=launcher.parent/'SpatialWallpaper.exe'
 out=ROOT/'Validation/Content/sdk-runtime/main-manager/runs'/uuid.uuid4().hex[:12];out.mkdir(parents=True);print(out,flush=True)
 common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent
 desktop=out/'desktop';calibration=out/'calibration';report={'status':'failed','scope':'bounded original Launch.exe manager/SDK combination; synthetic camera; no RC/long-input/performance claim','launcher_sha256':hashlib.sha256(launcher.read_bytes()).hexdigest(),'assembly_sha256':hashlib.sha256((launcher.parent/'SpatialWallpaper_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),'cases':[]}
 baseline=c.create_unicode_buffer(32768);assert u.SystemParametersInfoW(0x73,len(baseline),baseline,0)
 references=read(ROOT/'tools/desktop/builtin-visibility-reference.json')['profiles']
 process=None;client=None;runtime_pid=None
 def capture_builtin(asked,target):
   # Runtime control is single-owner. Ask the original manager to observe over
   # its existing connection rather than opening a competing manager socket.
   (target.parent/(target.stem+'.state-initial.request')).write_text('observe current native frame')
   initial=until(lambda:read(target.parent/(target.stem+'.state-initial.json')) if (target.parent/(target.stem+'.state-initial.json')).exists() else None,5,'original manager native state')
   display=next(item for item in initial['displays'] if item['id']==initial['display_id'])
   with exposed_desktop(display) as refresh_desktop:
    time.sleep(8 if asked['artwork_id']=='mita' else 1.2)
    (target.parent/(target.stem+'.state-capture.request')).write_text('observe capture-time native frame')
    state=until(lambda:read(target.parent/(target.stem+'.state-capture.json')) if (target.parent/(target.stem+'.state-capture.json')).exists() else None,3,'capture-time original manager state');assert state['pid']==asked['pid']
    assert state.get('native_presentation'),'native presentation diagnostic flag did not reach original runtime'
    # Other applications can reappear during the original scene's eight-second
    # warmup. Expose again immediately before actual compositor capture; retain
    # the same visibility comparator and restore every encountered window.
    refresh_desktop()
    actual=ImageGrab.grab(bbox=(display['x'],display['y'],display['x']+display['width'],display['y']+display['height']),all_screens=True)
    actual.save(target);target.with_suffix('.json').write_text(json.dumps(state,indent=2),encoding='utf-8')
    if target.stem=='direct-pool':
     with blocked_scene_region(display):
      blocked=ImageGrab.grab(bbox=(display['x'],display['y'],display['x']+display['width'],display['y']+display['height']),all_screens=True)
      blocked.save(target.with_name('controlled-black-pool.png'))
     try:compare_signatures(references['pool']['signature'],signature(blocked))
     except AssertionError:report['cases'].append('actual-owned-black-overlay-rejected-despite-successful-apply-and-native-frame')
     else:raise AssertionError('visibility checker accepted actual black overlay')
   error=compare_signatures(references[asked['artwork_id']]['signature'],signature(actual))
   observation(state,asked['artwork_id'])
   report['cases'].append({'case':'builtin-frame-visible-after-sdk-switch','image':target.name,'spatial_error':error,'native_presentation':state.get('native_presentation')})
 def players():
  result=[]
  for proc in psutil.process_iter(['pid','name']):
   if proc.info['name'].lower()!=exe.name.lower():continue
   try:
    if Path(proc.exe()).resolve()==exe and str(desktop) in proc.cmdline():result.append(proc)
   except (psutil.NoSuchProcess,psutil.AccessDenied):continue
  return result
 def launch(stage,restore=False):
  nonlocal process
  stage.mkdir(exist_ok=True)
  tracking=launcher.parent/'Tracker' if (launcher.parent/'Tracker/src').is_dir() else ROOT/'tracker'
  command=[str(launcher),'--desktop-data-root',str(desktop),'--data-root',str(calibration),'--tracking-python',str(common/'tracker/.venv/Scripts/python.exe'),'--tracking-root',str(tracking),'--tracking-scenario','fixed','--native-presentation-diagnostics','true','--main-manager-smoke',str(stage),'--main-manager-probe',str(out/'tray-manager-probe.json'),'-screen-fullscreen','0','-screen-width','800','-screen-height','640','-logFile',str(stage/'manager.log')]
  if args.sdk_runtime:command+=['--sdk-runtime',str(args.sdk_runtime.resolve())]
  if restore:command+=['--main-manager-restore','true']
  process=subprocess.Popen(command);end=time.monotonic()+600;last_capture=None;native_captures=0
  while time.monotonic()<end:
   request=stage/'capture-request.json'
   if request.exists():
    try:asked=read(request)
    except (json.JSONDecodeError,PermissionError):asked=None
    if asked and asked['name']!=last_capture:
     if asked.get('desktop'):
      capture_builtin(asked,stage/(asked['name']+'.png'));native_captures+=1
     else:screenshot(asked['pid'],stage/(asked['name']+'.png'))
     if asked['name']=='sdk-fullscreen-input-hit':
      before=Image.open(stage/'sdk-original-fullscreen.png').convert('RGB');after=Image.open(stage/'sdk-fullscreen-input-hit.png').convert('RGB');assert before.size==after.size
      x=round(after.width*.403);y=round(64+(after.height-64)*.449);box=(x-15,y-15,x+15,y+15);old=ImageStat.Stat(before.crop(box)).mean;new=ImageStat.Stat(after.crop(box)).mean
      assert old[0]-new[0]>30 and new[1]-new[0]>25,('actual scaled MiniShot author click was not visibly presented',old,new)
      report['cases'].append({'case':'actual-scaled-fullscreen-author-hit-presented','before_rgb':old,'after_rgb':new})
     (stage/(asked['name']+'.captured')).write_text('captured composited owned manager client region');last_capture=asked['name'];print('Captured',last_capture,flush=True)
   if process.poll() is not None:break
   time.sleep(.05)
  else:raise TimeoutError('original main manager combination')
  result=read(stage/'result.json');report['cases'].append(result);assert result['status']=='passed-main-manager-development-combination',result
  if not restore:assert native_captures==12,'launcher did not exercise twelve actual builtin desktop frames'
  assert process.returncode==0,process.returncode
  return result
 try:
  first=out/'first-launch';launch(first)
  descriptor=read(desktop/'runtime/connection.json');runtime_pid=descriptor['pid'];assert psutil.pid_exists(runtime_pid)
  client=Client(descriptor);state=client.call('GetDesktopState',{});expected=read(first/'expected-restart.json')
  assert state['active_artwork']==expected['work'] and state['content_ready'] and state['content_revision']==expected['revision'],state
  report['cases'].append('normal-manager-close-keeps-original-runtime-and-sound-session')
  assert len(players())==1 and players()[0].pid==runtime_pid,'duplicate desktop runtime or manager survived close'
  # The real manager must acquire the sole manager control connection. Holding
  # this test client's manager role makes a disconnected Library look reopened.
  client.close();client=None
  shell=windows(runtime_pid,'SpatialWallpaper.Desktop.');assert len(shell)==1
  assert u.PostMessageW(shell[0],0x0111,1,0)
  def manager():
   candidates=[proc for proc in players() if '--desktop-runtime' not in proc.cmdline()]
   return candidates[0] if len(candidates)==1 and windows(candidates[0].pid,'UnityWndClass') else None
  reopened=until(manager,35,'tray reopens original manager')
  def manager_ready():
   try:return int((desktop/'manager.lock').read_text())==reopened.pid
   except (OSError,ValueError):return False
  until(manager_ready,35,'reopened manager finished Unity splash and acquired original lease')
  probe=until(lambda:read(out/'tray-manager-probe.json') if (out/'tray-manager-probe.json').exists() else None,50,'reopened GUI own connection/SDK receipt')
  assert probe['status']=='passed-connected-original-manager' and probe['manager_pid']==reopened.pid and probe['runtime_pid']==runtime_pid,probe
  assert probe['active_work']==expected['work'] and probe['active_revision']==expected['revision'] and probe['mix']==expected['mix'] and probe['sdk_import_enabled'],probe
  assert len(players())==2
  screenshot(reopened.pid,out/'tray-reopened-original-library.png');assert u.PostMessageW(shell[0],0x0111,1,0)
  until(lambda:manager() is not None and manager().pid==reopened.pid and len(players())==2,10,'tray focuses existing original manager')
  for hwnd in windows(reopened.pid,'UnityWndClass'):u.PostMessageW(hwnd,0x10,0,0)
  until(lambda:not psutil.pid_exists(reopened.pid),20,'close tray-reopened manager')
  client=Client(descriptor)
  assert client.call('GetDesktopState',{})['content_ready'];report['cases'].append('tray-reopens-same-full-manager-and-focuses-existing-owner')
  client.close();client=None;assert u.PostMessageW(shell[0],0x0111,4,0)
  until(lambda:not players(),50,'full tray exit drains original runtime')
  wallpaper=c.create_unicode_buffer(32768);assert u.SystemParametersInfoW(0x73,len(wallpaper),wallpaper,0) and wallpaper.value==baseline.value
  report['cases'].append('full-exit-drains-runtime-and-preserves-original-system-wallpaper')
  restored=out/'restart';restored.mkdir();shutil.copy2(first/'expected-restart.json',restored/'expected-restart.json');launch(restored,True)
  descriptor=read(desktop/'runtime/connection.json');runtime_pid=descriptor['pid'];client=Client(descriptor);state=client.call('GetDesktopState',{});assert state['active_artwork'] is None and not state['content_ready'],'uninstall did not restore system wallpaper'
  client.close();client=None;assert u.PostMessageW(windows(runtime_pid,'SpatialWallpaper.Desktop.')[0],0x0111,4,0);until(lambda:not players(),50,'final normal cleanup')
  report['status']='passed-main-manager-development-combination'
 except Exception as error:
  report['error']=repr(error);raise
 finally:
  if client:client.close()
  for proc in players():
   if '--desktop-runtime' in proc.cmdline():
    for shell in windows(proc.pid,'SpatialWallpaper.Desktop.'):u.PostMessageW(shell,0x0111,4,0)
  deadline=time.monotonic()+50
  while players() and time.monotonic()<deadline:time.sleep(.1)
  report['leftover_owned_players']=[proc.pid for proc in players()]
  if report['leftover_owned_players']:report['status']='failed-cleanup-unproven'
  (out/'report.json').write_text(json.dumps(report,indent=2,ensure_ascii=False),encoding='utf-8');print('Report:',out/'report.json',flush=True)
if __name__=='__main__':main()
