"""Real windowed client audio settings, mute and restart. No mouse/keyboard input."""
from pathlib import Path
import argparse,ctypes,json,os,shutil,subprocess,sys,time,uuid
from ctypes import wintypes as w
ROOT=Path(__file__).resolve().parents[2]
sys.path[:0]=[str(ROOT/'tools/sdk'),str(ROOT/'tools/content'),str(ROOT/'tools/sdk/experiments')]
from run_e2e import Client
from toolchain_process import run_toolchain
from native_build import compile_probe
u=ctypes.WinDLL('user32',use_last_error=True);k=ctypes.WinDLL('kernel32',use_last_error=True)
u.GetWindowThreadProcessId.argtypes=[w.HWND,ctypes.POINTER(w.DWORD)];u.PostMessageW.argtypes=[w.HWND,w.UINT,w.WPARAM,w.LPARAM]
k.CreateFileW.argtypes=[w.LPCWSTR,w.DWORD,w.DWORD,ctypes.c_void_p,w.DWORD,w.DWORD,w.HANDLE];k.CreateFileW.restype=w.HANDLE;k.CloseHandle.argtypes=[w.HANDLE]

def child(a):
    out=a.child_output.resolve();data=out/'data';data.mkdir();attempts=ROOT/'Validation/Sdk'/('as'+out.name[-8:])/'a';attempts.mkdir(parents=True)
    descriptor=data/'runtime/connection.json';player=None;client=None;report={'status':'failed','scope':__doc__,'cases':[],'attempts':str(attempts)}
    def until(read,why,seconds=60):
        deadline=time.monotonic()+seconds
        while time.monotonic()<deadline:
            if player is not None and player.poll() is not None:raise RuntimeError('Player exited: '+str(player.returncode))
            value=read()
            if value:return value
            time.sleep(.05)
        raise TimeoutError(why)
    def launch(label):
        nonlocal player,client
        startup=subprocess.STARTUPINFO();startup.dwFlags=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=4
        player=subprocess.Popen([str(a.delivery.resolve()/'client/player/ContentPlayer.exe'),'--content-data-root',str(data),'--sdk-runtime',str(a.delivery.resolve()/'runtime'),'--sdk-attempts',str(attempts),'-screen-fullscreen','0','-screen-width','960','-screen-height','540','-logFile',str(out/(label+'.log'))],startupinfo=startup)
        def discovered():
            try:value=json.loads(descriptor.read_text(encoding='utf-8-sig'))
            except (FileNotFoundError,PermissionError):return None
            return value if value['pid']==player.pid else None
        client=Client(until(discovered,'runtime discovery'));until(lambda:client.call('GetContentState',{})['runtime_ready'],'runtime ready')
    def close():
        nonlocal player,client
        if client:client.close();client=None
        if player and player.poll() is None:
            callback=ctypes.WINFUNCTYPE(w.BOOL,w.HWND,w.LPARAM)
            @callback
            def visit(hwnd,_):
                pid=w.DWORD();u.GetWindowThreadProcessId(hwnd,ctypes.byref(pid))
                if pid.value==player.pid:u.PostMessageW(hwnd,0x10,0,0)
                return True
            u.EnumWindows(visit,0);code=player.wait(timeout=45);assert code==0,code
        player=None
    def state(label):
        value=client.call('GetContentState',{});(out/(label+'.json')).write_text(json.dumps(value,indent=2));return value
    def import_work():
        name=str(uuid.uuid4());staged=data/'staging'/(name+'.spatial');staged.parent.mkdir(exist_ok=True);shutil.copyfile(a.package,staged)
        result=client.call('ImportContent',{'staging_id':name});staged.unlink();return result['work_id']
    def mix(work,music,effects,mm=False,em=False):return client.call('SetSoundMix',{'work_id':work,'mix':{'music':music,'effects':effects,'music_muted':mm,'effects_muted':em}})
    def apply(work):
        current=client.call('GetContentState',{});result=client.call('ApplyContent',{'display_id':current['display_id'],'work_id':work});assert result['succeeded'],result
        until(lambda:client.call('GetContentState',{})['presentation_ready'],'presentation')
    def host_pid():
        current=client.call('GetContentState',{});attempt=current['presentation_metrics']['sandbox_receipt']['attempt']
        def found():
            for p in data.glob('sdk-runtime/launches/*/status.json'):
                try:s=json.loads(p.read_text(encoding='utf-8-sig'))
                except (FileNotFoundError,PermissionError):continue
                if (s.get('receipt') or {}).get('attempt')==attempt:return s['host_pid']
        return until(found,'owned native Host')
    def capture(label,pid,silent):
        time.sleep(.2);result=subprocess.run([str(out/'Capture.exe'),str(pid),'1500'],capture_output=True,text=True,timeout=10);(out/(label+'.capture.json')).write_text(result.stdout+result.stderr);result.check_returncode();value=json.loads(result.stdout);levels=[b['rms'] for b in value['bins']];maximum=max(levels);assert (maximum<.00001 if silent else maximum>.0001),(label,maximum);report['cases'].append({'name':label,'pid':pid,'max_rms':maximum,'silent':silent,'passed':True})
    try:
        launch('first');work=import_work();report['work']=work;mix(work,.4,.6,True,True);apply(work);pid=host_pid();capture('saved-mute-before-first-play',pid,True)
        mix(work,1,0);capture('music-audible',pid,False)
        settings=data/'sound-mixes.json';before=settings.read_bytes();locked=k.CreateFileW(str(settings),0x80000000,1,None,3,0x80,None);assert locked!=w.HANDLE(-1).value
        try:
            denied=False
            try:mix(work,.05,.2)
            except Exception:denied=True
            assert denied and settings.read_bytes()==before
            value=state('failed-save');assert value['sound_mixes'][work]['music']==1 and value['sound_mixes'][work]['effects']==0
        finally:k.CloseHandle(locked)
        report['cases'].append({'name':'failed-settings-save-preserves-old-disk-and-memory','passed':True})
        mix(work,.2,0);capture('lower-music-volume',pid,False)
        invalid=False
        try:mix(work,1.1,0)
        except Exception:invalid=True
        assert invalid and state('invalid-mix')['sound_mixes'][work]['music']==.2
        report['cases'].append({'name':'invalid-volume-refused-without-changing-settings','passed':True})
        mix(work,.2,.5,True,True);capture('user-mute',pid,True);state('before-restart');close();launch('restarted');value=state('after-restart');assert value['presentation_ready'];assert value['sound_mixes'][work]['music_muted'] and value['sound_mixes'][work]['effects_muted'];pid=host_pid();capture('restart-keeps-mute',pid,True)
        mix(work,.25,0);capture('unmute-after-restart',pid,False);removed=client.call('RemoveContent',{'work_id':work});assert removed['succeeded'];capture('uninstall-stops-sound',pid,True);assert work not in json.loads(settings.read_text())
        report['cases'].append({'name':'uninstall-clears-work-mix','passed':True})
        second=import_work();mix(second,.3,.4);apply(second);locked=k.CreateFileW(str(settings),0x80000000,1,None,3,0x80,None);assert locked!=w.HANDLE(-1).value
        try:
            removed=client.call('RemoveContent',{'work_id':second});assert removed['succeeded'] and removed.get('sound_settings_cleanup_error');assert second not in state('removed-despite-settings-lock')['state']['works']
        finally:k.CloseHandle(locked)
        report['cases'].append({'name':'settings-cleanup-failure-preserves-successful-uninstall-result','passed':True})
        close();report['status']='passed-real-windowed-client-sound-mix-restart-uninstall-not-RC'
    finally:
        try:close()
        finally:(out/'report.json').write_text(json.dumps(report,indent=2))

def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('--delivery',type=Path,required=True);p.add_argument('--package',type=Path,required=True);p.add_argument('--child-output',type=Path);a=p.parse_args()
    if a.child_output:child(a);return
    out=ROOT/'Validation/Content/sdk-runtime/G/sound'/('client-'+uuid.uuid4().hex);out.mkdir(parents=True);print(out,flush=True);compile_probe(Path(__file__).with_name('process_sound_capture.cpp'),out/'Capture.exe')
    receipt={}
    with (out/'toolchain.log').open('w') as log:run_toolchain([sys.executable,str(Path(__file__).resolve()),'--delivery',str(a.delivery.resolve()),'--package',str(a.package.resolve()),'--child-output',str(out)],ROOT,log,300,receipt,env=os.environ.copy())
    (out/'process.json').write_text(json.dumps(receipt,indent=2));print(out)
if __name__=='__main__':main()
