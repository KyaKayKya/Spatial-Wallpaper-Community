"""GUI launch and native full-exit regression, isolated from real user data."""
from pathlib import Path
import ctypes as c
from ctypes import wintypes as w
import json,struct,subprocess,time,uuid,psutil,sys
from tray_roundtrip import windows

root=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(root/'tools/content'))
from run_e2e import Client
main=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip()).parent
binary=root/'Prototype/Builds/Library';exe=binary/'SpatialWallpaper.exe';launcher=binary/'Launch.exe'
out=root/'Validation/Desktop'/('lifecycle-'+uuid.uuid4().hex[:8]);out.mkdir(parents=True)
report={'passed':False,'events':[],'cases':[]};owned=set();started=time.monotonic()
u=c.WinDLL('user32',use_last_error=True)
callback_type=c.WINFUNCTYPE(None,w.HANDLE,w.DWORD,w.HWND,c.c_long,c.c_long,w.DWORD,w.DWORD)
u.SetWinEventHook.argtypes=[w.DWORD,w.DWORD,w.HMODULE,callback_type,w.DWORD,w.DWORD,w.DWORD];u.SetWinEventHook.restype=w.HANDLE
u.UnhookWinEvent.argtypes=[w.HANDLE]
u.GetWindowThreadProcessId.argtypes=[w.HWND,c.POINTER(w.DWORD)]
u.GetClassNameW.argtypes=[w.HWND,w.LPWSTR,c.c_int]
u.GetParent.argtypes=[w.HWND];u.GetParent.restype=w.HWND
u.IsWindowVisible.argtypes=[w.HWND]
u.IsWindow.argtypes=[w.HWND]
u.PostMessageW.argtypes=[w.HWND,w.UINT,w.WPARAM,w.LPARAM]
u.PeekMessageW.argtypes=[c.POINTER(w.MSG),w.HWND,w.UINT,w.UINT,w.UINT]

def event(hook,kind,hwnd,obj,child,thread,stamp):
    if not hwnd or obj!=0:return
    cls=c.create_unicode_buffer(128);u.GetClassNameW(hwnd,cls,128)
    if cls.value!='UnityWndClass':return
    pid=w.DWORD();u.GetWindowThreadProcessId(hwnd,c.byref(pid))
    try:
        owner=psutil.Process(pid.value)
        if Path(owner.exe()).resolve()!=exe.resolve():return
        role='runtime' if '--desktop-runtime' in owner.cmdline() else 'manager'
    except (psutil.NoSuchProcess,psutil.AccessDenied):return
    parent=u.GetParent(hwnd);parent_class=c.create_unicode_buffer(128)
    if parent:u.GetClassNameW(parent,parent_class,128)
    owned.add(pid.value)
    report['events'].append({'t':round(time.monotonic()-started,3),'pid':pid.value,'role':role,'visible':bool(u.IsWindowVisible(hwnd)),'parent_class':parent_class.value})

callback=callback_type(event);hook=u.SetWinEventHook(0x8002,0x8002,None,callback,0,0,0)
assert hook,'WinEvent show hook unavailable'

def pump():
    msg=w.MSG()
    while u.PeekMessageW(c.byref(msg),None,0,0,1):u.TranslateMessage(c.byref(msg));u.DispatchMessageW(c.byref(msg))
    time.sleep(.01)

def start(smoke):
    args=[str(launcher),'--desktop-data-root',str(out/'desktop'),'--data-root',str(out/'calibration'),
          '--tracking-python',str(main/'tracker/.venv/Scripts/python.exe'),'--tracking-root',str(root/'tracker'),
          '-screen-fullscreen','0','-logFile',str(out/('manager.log' if smoke else 'relaunch.log'))]
    if smoke:args+=['--desktop-smoke',str(out),'--desktop-full-exit-test','true']
    process=subprocess.Popen(args);owned.add(process.pid);return process

def native_quit(pid,busy=False):
    shell=windows(pid,'SpatialWallpaper.Desktop.');assert len(shell)==1
    # WM_COMMAND is the standard command posted by the actual native popup menu.
    if busy:assert u.PostMessageW(shell[0],0x0111,3,0)
    assert u.PostMessageW(shell[0],0x0111,4,0)

process=None
try:
    data=launcher.read_bytes();pe=struct.unpack_from('<I',data,0x3c)[0]
    assert struct.unpack_from('<H',data,pe+24+68)[0]==2,'Launcher must use GUI subsystem'
    report['cases'].append('native-GUI-entry-no-console-subsystem')
    process=start(True);deadline=time.monotonic()+180
    while not (out/'ready-for-full-exit.txt').exists():
        pump()
        assert process.poll() is None,'GUI launch exited before acceptance'
        assert time.monotonic()<deadline,'Manager acceptance timed out'
        if (out/'errors.txt').exists():raise AssertionError((out/'errors.txt').read_text()[:2000])
    connection=json.loads((out/'desktop/runtime/connection.json').read_text(encoding='utf-8-sig'))
    runtime_pid=connection['pid'];owned.add(runtime_pid)
    manager_pids=[p.pid for p in psutil.Process(process.pid).children() if p.name().lower()=='spatialwallpaper.exe'];assert len(manager_pids)==1
    manager_pid=manager_pids[0];owned.add(manager_pid)
    assert (out/'ready-for-full-exit.txt').read_text()=='active wallpaper and dirty manager draft'
    native_quit(runtime_pid,busy=True);deadline=time.monotonic()+25
    while any(psutil.pid_exists(pid) for pid in [runtime_pid,manager_pid,process.pid]):
        pump();assert time.monotonic()<deadline,'Full exit left a Runtime, manager, or GUI launcher alive'
    for _ in range(300):pump()
    assert not [p for p in psutil.process_iter(['exe','cmdline']) if p.info['exe'] and Path(p.info['exe']).resolve()==exe.resolve() and str(out/'desktop') in (p.info['cmdline'] or [])],'Full exit respawned a Unity process'
    report['cases'].append('native-full-exit-while-stopping-active-wallpaper-closes-dirty-manager-and-launcher-without-restart')
    # Explicit user relaunch must clear the old quit notice, unlike reconnection.
    second=start(False);process=second;deadline=time.monotonic()+30;new_runtime=None
    while time.monotonic()<deadline:
        pump();path=out/'desktop/runtime/connection.json'
        if path.exists():
            value=json.loads(path.read_text(encoding='utf-8-sig'))
            if value['pid']!=runtime_pid and psutil.pid_exists(value['pid']):
                new_runtime=value['pid'];break
    assert new_runtime,'Explicit relaunch did not create a new Runtime';owned.add(new_runtime)
    time.sleep(1)
    managers=[p for p in psutil.Process(second.pid).children() if p.name().lower()=='spatialwallpaper.exe'];assert len(managers)==1
    for hwnd in windows(managers[0].pid,'UnityWndClass'):u.PostMessageW(hwnd,0x0010,0,0)
    deadline=time.monotonic()+15
    while second.poll() is None:pump();assert time.monotonic()<deadline,'Normal manager close blocked'
    assert psutil.pid_exists(new_runtime),'Normal manager close stopped Runtime'
    runtime_args=psutil.Process(new_runtime).cmdline()
    assert runtime_args[runtime_args.index('-parentHWND')+1]=='delayed','Runtime still redirects rendering to an external HWND'
    client=Client(value)
    try:
        state=client.call('GetDesktopState',{});preset=json.loads(next((out/'calibration/presets').glob('*.json')).read_text(encoding='utf-8-sig'))
        assert u.IsWindow(state['window']),'Closing manager destroyed Runtime render HWND'
        applied=client.call('ApplyDesktop',{'artwork_id':'showcase','display_id':state['displays'][0]['id'],'preset_id':preset['id'],'preset_revision':preset['revision']})
        assert applied['succeeded'],applied
        assert u.IsWindowVisible(applied['window']) and applied['render_errors']==0,applied
        report['cases'].append('Runtime-render-HWND-survives-manager-close-without-external-host')
        display=state['displays'][0]
        assert (applied['render_width'],applied['render_height'])==(display['width'],display['height']),applied
    finally:client.close()
    shell=windows(new_runtime,'SpatialWallpaper.Desktop.');assert len(shell)==1
    # These are sequential real menu commands while the manager is closed.
    assert u.PostMessageW(shell[0],0x0111,1,0)
    assert u.PostMessageW(shell[0],0x0111,4,0)
    quit_at=time.monotonic()-started;deadline=time.monotonic()+25
    while psutil.pid_exists(new_runtime):pump();assert time.monotonic()<deadline,'Runtime failed to quit during in-flight tray open'
    for _ in range(500):pump()
    leftovers=[p.pid for p in psutil.process_iter(['exe','cmdline']) if p.info['exe'] and Path(p.info['exe']).resolve()==exe.resolve() and str(out/'desktop') in (p.info['cmdline'] or [])]
    assert not leftovers,('In-flight tray open resurrected application',leftovers)
    launcher_leftovers=[p.pid for p in psutil.process_iter(['exe','cmdline']) if p.info['exe'] and Path(p.info['exe']).resolve()==launcher.resolve() and str(out/'desktop') in (p.info['cmdline'] or [])]
    assert not launcher_leftovers,('Full exit left a GUI host alive',launcher_leftovers)
    assert not [e for e in report['events'] if e['t']>=quit_at and e['role']=='manager' and e['visible']],'In-flight tray manager flashed after full exit'
    report['cases']+=['explicit-relaunch-clears-stale-quit','normal-manager-close-keeps-runtime','in-flight-tray-open-cannot-cancel-later-full-exit-or-show-manager']
    assert not [e for e in report['events'] if e['role']=='runtime' and e['visible'] and e['parent_class']!='WorkerW'],'Runtime showed an independent Unity window; see events'
    report['cases'].append('WinEvent-observed-no-independent-visible-Runtime-window')
    report['passed']=True
finally:
    u.UnhookWinEvent(hook)
    for p in psutil.process_iter(['exe','cmdline']):
        if p.info['exe'] and Path(p.info['exe']).resolve() in [exe.resolve(),launcher.resolve()] and str(out/'desktop') in (p.info['cmdline'] or []):owned.add(p.pid)
    for pid in owned:
        try:
            p=psutil.Process(pid)
            if Path(p.exe()).resolve() in [exe.resolve(),launcher.resolve()]:p.terminate();p.wait(timeout=10)
        except psutil.NoSuchProcess:pass
    (out/'result.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2));print('Evidence',out)
