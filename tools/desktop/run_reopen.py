"""Tray open must focus an existing manager without starting another Player."""
from pathlib import Path
import argparse,ctypes as c,json,subprocess,time,uuid,psutil,hashlib
from verify_receipt import validate
from ctypes import wintypes as w
from tray_roundtrip import windows

root=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser();parser.add_argument('--launcher',type=Path,default=root/'Prototype/Builds/Library/Launch.exe');args=parser.parse_args()
launcher=args.launcher.resolve();exe=launcher.parent/'SpatialWallpaper.exe'
out=root/'Validation/Desktop'/('reopen-'+uuid.uuid4().hex[:8]);out.mkdir(parents=True)
data=out/'desktop';report={'passed':False,'new_players':[],'launcher':str(launcher),'player_sha256':hashlib.sha256(exe.read_bytes()).hexdigest()};owned=set()
if launcher==(root/'Prototype/Builds/Library/Launch.exe').resolve():
    report['build_fingerprint']=hashlib.sha256(json.dumps(validate(),sort_keys=True).encode()).hexdigest()
u=c.WinDLL('user32',use_last_error=True)
u.PostMessageW.argtypes=[w.HWND,w.UINT,w.WPARAM,w.LPARAM]
u.ShowWindow.argtypes=[w.HWND,c.c_int];u.IsIconic.argtypes=[w.HWND];u.GetForegroundWindow.restype=w.HWND

def players():
    result=[]
    for p in psutil.process_iter(['exe','cmdline']):
        if p.info['exe'] and Path(p.info['exe']).resolve()==exe and str(data) in (p.info['cmdline'] or []):result.append(p)
    return result

process=subprocess.Popen([str(launcher),'--desktop-data-root',str(data),'--data-root',str(out/'calibration'),'-logFile',str(out/'manager.log')])
try:
    deadline=time.monotonic()+35
    while True:
        rows=players();owned.update(p.pid for p in rows)
        managers=[p for p in rows if '--desktop-runtime' not in p.cmdline()]
        runtimes=[p for p in rows if '--desktop-runtime' in p.cmdline()]
        if len(managers)==len(runtimes)==1 and windows(runtimes[0].pid,'SpatialWallpaper.Desktop.'):break
        assert time.monotonic()<deadline,'Startup timeout';time.sleep(.05)
    manager=managers[0];runtime=runtimes[0];time.sleep(2)
    hwnd=windows(manager.pid,'UnityWndClass')[0];shell=windows(runtime.pid,'SpatialWallpaper.Desktop.')[0]
    u.ShowWindow(hwnd,6);assert u.IsIconic(hwnd)
    assert u.PostMessageW(shell,0x0111,1,0)
    deadline=time.monotonic()+5
    while time.monotonic()<deadline:
        rows=players();owned.update(p.pid for p in rows)
        extra=[p.pid for p in rows if p.pid not in [manager.pid,runtime.pid]]
        report['new_players']+=extra
        if extra:raise AssertionError('Tray open started another Unity Player instead of activating the owner')
        time.sleep(.03)
    assert not u.IsIconic(hwnd),'Existing manager remained minimized'
    assert u.GetForegroundWindow()==hwnd,'Existing manager was not brought to foreground'
    assert not (data/'manager.log').exists(),'Tray created a replacement manager log even though an owner was open'
    report['passed']=True;report['manager_pid']=manager.pid
finally:
    rows=players()
    for p in rows:
        if '--desktop-runtime' in p.cmdline():
            for shell in windows(p.pid,'SpatialWallpaper.Desktop.'):u.PostMessageW(shell,0x0111,4,0)
    deadline=time.monotonic()+15
    while players() and time.monotonic()<deadline:time.sleep(.1)
    for p in players():p.terminate();p.wait(10)
    if process.poll() is None:process.wait(timeout=15)
    (out/'result.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
    print(json.dumps(report,indent=2));print('Evidence',out)
