"""Real integrated manager acceptance plus survival after its normal exit."""
from pathlib import Path
import hashlib,json, subprocess, sys, time, uuid, psutil
root=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(root/'tools/content'))
from run_e2e import Client

def main():
    from verify_receipt import validate
    receipt=validate();fingerprint=hashlib.sha256(json.dumps(receipt,sort_keys=True).encode()).hexdigest()
    run=root/'Validation/Desktop'/('manager-'+uuid.uuid4().hex[:8]);run.mkdir(parents=True)
    main_root=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip()).parent
    args=[str(root/'Prototype/Builds/Library/SpatialWallpaper.exe'),'--desktop-smoke',str(run),'--desktop-data-root',str(run/'desktop'),'--data-root',str(run/'calibration'),'--tracking-python',str(main_root/'tracker/.venv/Scripts/python.exe'),'--tracking-root',str(root/'tracker'),'-screen-fullscreen','0','-logFile',str(run/'manager.log')]
    cold='--cold' in sys.argv
    if '--vertical-context-race' in sys.argv:args+=['--vertical-context-race','true']
    if cold:args+=['--desktop-cold-preview','true']
    manager=subprocess.Popen(args);client=None;duplicate=None;passed=False;samples=[]
    try:
        deadline=time.monotonic()+180
        while manager.poll() is None and time.monotonic()<deadline:
            try:
                owner=psutil.Process(manager.pid)
                row={'manager':owner.memory_info()._asdict(),'manager_cpu_seconds':owner.cpu_times()._asdict()}
            except psutil.NoSuchProcess:
                manager.wait(timeout=5);break
            descriptor=run/'desktop/runtime/connection.json'
            if descriptor.exists():
                info=json.loads(descriptor.read_text(encoding='utf-8-sig'))
                background=psutil.Process(info['pid'])
                row['runtime']=background.memory_info()._asdict()
                row['runtime_cpu_seconds']=background.cpu_times()._asdict()
            samples.append(row)
            if duplicate is None and (run/'desktop/manager.lock').exists():
                second=args.copy();position=second.index('--desktop-smoke');del second[position:position+2]
                log_position=second.index('-logFile');second[log_position+1]=str(run/'duplicate.log')
                duplicate=subprocess.Popen(second)
            if (run/'errors.txt').exists():raise RuntimeError((run/'errors.txt').read_text()[:2000])
            time.sleep(.2)
        assert manager.poll()==0,'Manager acceptance failed or timed out'
        assert (run/'result.txt').exists(),'Missing manager result'
        assert duplicate is not None and duplicate.wait(timeout=15)==0,'Duplicate manager did not activate existing instance and exit'
        data=json.loads((run/'desktop/runtime/connection.json').read_text(encoding='utf-8-sig'))
        client=Client(data);state=client.call('GetDesktopState',{})
        if cold:assert state['active_artwork'] is None,'Cold preview unexpectedly applied wallpaper'
        else:assert state['active_artwork']=='showcase' and state['attached'],'Closing manager stopped wallpaper'
        state=client.call('StopDesktop',{});assert state['succeeded'] and state['active_artwork'] is None
        client.call('QuitDesktop',{});passed=True
        print((run/'result.txt').read_text());print('PASS manager normal exit preserves Runtime; explicit stop and quit acknowledged')
    finally:
        (run/'result.json').write_text(json.dumps({'passed':passed,'cold_preview':cold,'error':repr(sys.exc_info()[1]) if sys.exc_info()[1] else None,'run':str(run),'duplicate_manager_checked':duplicate is not None,'process_memory_samples':samples},indent=2))
        if client:client.close()
        if manager.poll() is None:manager.terminate();manager.wait(timeout=15)
        if duplicate and duplicate.poll() is None:duplicate.terminate();duplicate.wait(timeout=15)
        path=run/'desktop/runtime/connection.json'
        if path.exists():
            # Address only the process from this isolated discovery file and this binary.
            data=json.loads(path.read_text(encoding='utf-8-sig'))
            import ctypes as c
            k=c.WinDLL('kernel32',use_last_error=True);k.OpenProcess.argtypes=[c.c_ulong,c.c_int,c.c_ulong];k.OpenProcess.restype=c.c_void_p
            k.QueryFullProcessImageNameW.argtypes=[c.c_void_p,c.c_ulong,c.c_wchar_p,c.POINTER(c.c_ulong)];k.TerminateProcess.argtypes=[c.c_void_p,c.c_uint];k.CloseHandle.argtypes=[c.c_void_p]
            handle=k.OpenProcess(0x1001,False,data['pid'])
            if handle:
                try:
                    name=c.create_unicode_buffer(32768);size=c.c_ulong(len(name))
                    if k.QueryFullProcessImageNameW(handle,0,name,c.byref(size)):
                        assert Path(name.value).resolve()==(root/'Prototype/Builds/Library/SpatialWallpaper.exe').resolve()
                        if not passed:k.TerminateProcess(handle,1)
                finally:k.CloseHandle(handle)
        (run/'result.json').write_text(json.dumps({'passed':passed,'cold_preview':cold,'build_fingerprint':fingerprint,'run':str(run),'duplicate_manager_checked':duplicate is not None,'process_memory_samples':samples},indent=2))
        print('Evidence',run)
    return run.name

if __name__=='__main__':
    results=[]
    for iteration in range(20 if '--cold' in sys.argv else 1):
        print('Acceptance iteration',iteration+1,flush=True);results.append(main())
    if '--cold' in sys.argv:
        path=root/'Validation/Desktop'/('cold-'+uuid.uuid4().hex[:8]+'.json');path.write_text(json.dumps({'runs':results},indent=2));print('Cold manifest',path)
