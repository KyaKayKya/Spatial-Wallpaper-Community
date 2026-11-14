"""Reproduce apply with restored settings and held pointer events, no camera start."""
from pathlib import Path
import argparse,json,subprocess,time,uuid,sys,shutil,os,hashlib
from verify_receipt import validate
root=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(root/'tools/content'));from run_e2e import Client
parser=argparse.ArgumentParser();parser.add_argument('--settings-source',type=Path);parser.add_argument('--fullscreen',action='store_true');parser.add_argument('--use-defaults',action='store_true');args=parser.parse_args()
out=root/'Validation/Desktop'/('pointer-'+uuid.uuid4().hex[:8]);out.mkdir(parents=True)
if not args.use_defaults:
    assert args.settings_source,'Provide an isolated settings source';shutil.copytree(args.settings_source,out/'calibration')
main=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip()).parent
data=Path(os.environ['LOCALAPPDATA'])/'SpatialWallpaper/Desktop' if args.use_defaults else out/'desktop'
command=[str(root/'Prototype/Builds/Library/Launch.exe'),'--desktop-smoke',str(out),'--desktop-pointer-probe','true','--tracking-python',str(main/'tracker/.venv/Scripts/python.exe'),'--tracking-root',str(root/'tracker'),'-screen-fullscreen','0','-logFile',str(out/'manager.log')]
if not args.use_defaults:command+=['--desktop-data-root',str(data),'--data-root',str(out/'calibration')]
if args.fullscreen:command+=['--desktop-pointer-fullscreen','true']
receipt=validate()
p=subprocess.Popen(command);client=None;report={'passed':False,'fullscreen':args.fullscreen,'build_fingerprint':hashlib.sha256(json.dumps(receipt,sort_keys=True).encode()).hexdigest()}
try:
    deadline=time.monotonic()+70
    while p.poll() is None:
        assert time.monotonic()<deadline,'Pointer probe timeout'
        assert not (out/'errors.txt').exists(),(out/'errors.txt').read_text() if (out/'errors.txt').exists() else ''
        time.sleep(.1)
    assert p.returncode==0 and (out/'result.txt').exists(),'Pointer probe failed'
    client=Client(json.loads((data/'runtime/connection.json').read_text(encoding='utf-8-sig')))
    state=client.call('GetDesktopState',{});assert state['active_artwork']=='limpool' and state['attached'] and not state['camera_started'],state
    import ctypes as c
    u=c.WinDLL('user32');u.IsWindowVisible.argtypes=[c.c_void_p]
    report['window_visible']=bool(u.IsWindowVisible(state['window']));report['parent_visible']=bool(u.IsWindowVisible(state['parent']))
    assert report['window_visible'] and report['parent_visible'],('Desktop acknowledged an invisible native presentation',report,state)
    report['passed']=True;report['result']=(out/'result.txt').read_text();print(report['result'])
finally:
    if client:client.call('QuitDesktop',{});client.close()
    if p.poll() is None:p.terminate();p.wait(15)
    if client is None:
        import psutil
        for child in psutil.process_iter(['exe','cmdline']):
            if child.info['exe'] and Path(child.info['exe']).parent.resolve()==(root/'Prototype/Builds/Library').resolve() and str(out/'desktop') in (child.info['cmdline'] or []):child.terminate()
    (out/'result.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print('Evidence',out)
