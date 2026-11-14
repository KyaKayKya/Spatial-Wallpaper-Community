"""Owned windowed Client: five named packages, frame receipts and short process audio.
No desktop mode, mouse/keyboard input, uninstall, or existing data root.
"""
from pathlib import Path
import argparse, ctypes, hashlib, json, os, shutil, subprocess, sys, time, uuid
from ctypes import wintypes as w
ROOT = next(p for p in Path(__file__).resolve().parents if (p/'tools/sdk').is_dir())
sys.path[:0] = [str(ROOT/'tools/sdk'), str(ROOT/'tools/content'), str(ROOT/'tools/sdk/experiments')]
from run_e2e import Client
from toolchain_process import run_toolchain
from native_build import compile_probe
u = ctypes.WinDLL('user32', use_last_error=True)
u.GetWindowThreadProcessId.argtypes = [w.HWND, ctypes.POINTER(w.DWORD)]
u.PostMessageW.argtypes = [w.HWND, w.UINT, w.WPARAM, w.LPARAM]
k = ctypes.WinDLL('kernel32', use_last_error=True)
k.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]; k.OpenProcess.restype = w.HANDLE
k.CloseHandle.argtypes = [w.HANDLE]
k.GetProcessTimes.argtypes = [w.HANDLE] + [ctypes.POINTER(w.FILETIME)]*4
k.WaitForSingleObject.argtypes = [w.HANDLE, w.DWORD]

def pin_host(pid, creation):
    handle = k.OpenProcess(0x100000 | 0x1000, False, pid)
    if not handle: raise ctypes.WinError(ctypes.get_last_error())
    try:
        times = [w.FILETIME() for _ in range(4)]
        if not k.GetProcessTimes(handle, *(ctypes.byref(t) for t in times)): raise ctypes.WinError(ctypes.get_last_error())
        assert (times[0].dwHighDateTime << 32) | times[0].dwLowDateTime == creation, 'Host creation mismatch'
        assert k.WaitForSingleObject(handle, 0) == 258, 'Host already exited'
        return handle
    except BaseException:
        k.CloseHandle(handle); raise

def write(path, value): path.write_text(json.dumps(value, indent=2, ensure_ascii=False), encoding='utf-8')
def sha(path): return hashlib.sha256(path.read_bytes()).hexdigest()
def packages(path):
    value = json.loads(path.read_text(encoding='utf-8-sig'))
    if isinstance(value, dict): value = value.get('packages', value.get('scenes'))
    assert isinstance(value, list) and len(value) == 5, 'exactly five packages required'
    result = []
    for item in value:
        name = item.get('name', item.get('scene', item.get('SceneSDK')))
        package = Path(item.get('package', item.get('path', '')))
        if not package.is_absolute(): package = path.parent/package
        package = package.resolve()
        assert isinstance(name, str) and name.strip() and package.is_file(), item
        result.append({'name': name, 'package': str(package), 'sha256': sha(package)})
    assert len({x['name'] for x in result}) == 5, 'distinct names required'
    return result

def child(a):
    out = a.child_output.resolve()
    data = a.trial_data.resolve()
    assert not data.exists(), 'TrialData must be fresh'
    data.mkdir(parents=True)
    attempts = data/'sdk-attempts'; attempts.mkdir()
    player = None; client = None
    report = {'status': 'failed', 'scope': __doc__, 'trial_data': str(data), 'packages': packages(a.packages.resolve()), 'cases': []}
    def until(read, label, seconds=60):
        deadline = time.monotonic()+seconds
        while time.monotonic() < deadline:
            if player.poll() is not None: raise RuntimeError('Player exited: '+str(player.returncode))
            value = read()
            if value: return value
            time.sleep(.05)
        raise TimeoutError(label)
    def state(label=None):
        value = client.call('GetContentState', {})
        if label: write(out/(label+'.json'), value)
        return value
    def close():
        nonlocal client
        if client: client.close(); client = None
        if player is None: return
        if player.poll() is None:
            callback = ctypes.WINFUNCTYPE(w.BOOL, w.HWND, w.LPARAM)
            @callback
            def visit(hwnd, _):
                pid = w.DWORD(); u.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
                if pid.value == player.pid: u.PostMessageW(hwnd, 0x10, 0, 0)
                return True
            u.EnumWindows(visit, 0)
        code = player.wait(timeout=45)
        report['player_exit_code'] = code
        assert code == 0, code
    def set_mix(work, music, effects):
        value = {'music': music, 'effects': effects, 'music_muted': False, 'effects_muted': False}
        client.call('SetSoundMix', {'work_id': work, 'mix': value})
        return value
    def apply(item, label):
        current = state()
        response = client.call('ApplyContent', {'display_id': current['display_id'], 'work_id': item['work_id']})
        assert response['succeeded'], response
        def presented():
            value = state(); receipt = value.get('presentation_metrics', {}).get('sandbox_receipt') or {}
            if value['presentation_ready'] and receipt.get('work_id') == item['work_id']:
                assert receipt['revision_id'] == item['revision_id'] and receipt['build'] == item['build_id'], receipt
                assert not value.get('sound_error'), value
                return value
        value = until(presented, label+' presentation')
        write(out/(label+'.state.json'), value)
        receipt = value['presentation_metrics']['sandbox_receipt']
        def owned_host():
            for p in data.glob('sdk-runtime/launches/*/status.json'):
                try: s = json.loads(p.read_text(encoding='utf-8-sig'))
                except (FileNotFoundError, PermissionError): continue
                if (s.get('receipt') or {}).get('attempt') == receipt['attempt']:
                    assert s['host_pid'] > 0 and s['host_creation_filetime'] > 0, s
                    return s
        status = until(owned_host, label+' owned Host')
        write(out/(label+'.native-status.json'), status)
        handle = pin_host(status['host_pid'], status['host_creation_filetime'])
        try:
            time.sleep(.2)
            captured = subprocess.run([str(out/'Capture.exe'), str(status['host_pid']), '1000'], capture_output=True, text=True, timeout=10)
            assert k.WaitForSingleObject(handle, 0) == 258, 'Host exited during capture'
        finally: k.CloseHandle(handle)
        (out/(label+'.capture.txt')).write_text(captured.stdout+captured.stderr, encoding='utf-8')
        captured.check_returncode(); audio = json.loads(captured.stdout)
        assert audio['pid'] == status['host_pid'] and audio['hresult'] == '0x00000000', audio
        maximum = max(b['rms'] for b in audio['bins'])
        assert maximum > .0001, (label, maximum)
        report['cases'].append({'name': label, 'receipt': receipt, 'host_pid': status['host_pid'], 'max_rms': maximum, 'passed': True})
        return value
    try:
        startup = subprocess.STARTUPINFO(); startup.dwFlags = subprocess.STARTF_USESHOWWINDOW; startup.wShowWindow = 4
        player = subprocess.Popen([str(a.delivery.resolve()/'client/player/ContentPlayer.exe'), '--content-data-root', str(data), '--sdk-runtime', str(a.delivery.resolve()/'runtime'), '--sdk-attempts', str(attempts), '-screen-fullscreen', '0', '-screen-width', '960', '-screen-height', '540', '-logFile', str(out/'player.log')], startupinfo=startup)
        report['player_pid'] = player.pid
        def discover():
            try: value = json.loads((data/'runtime/connection.json').read_text(encoding='utf-8-sig'))
            except (FileNotFoundError, PermissionError): return None
            return value if value['pid'] == player.pid else None
        client = Client(until(discover, 'descriptor'))
        until(lambda: state()['runtime_ready'], 'runtime ready')
        for item in report['packages']:
            staged_id = str(uuid.uuid4()); staged = data/'staging'/(staged_id+'.spatial'); staged.parent.mkdir(exist_ok=True)
            shutil.copyfile(item['package'], staged)
            try: item.update(client.call('ImportContent', {'staging_id': staged_id, 'name': item['name']}))
            finally: staged.unlink(missing_ok=True)
        rows = state('imported-five')
        assert len(rows['state']['works']) == 5 and len({i['work_id'] for i in report['packages']}) == 5
        for item in report['packages']: assert rows['state']['works'][item['work_id']]['name'] == item['name']
        first, second = report['packages'][:2]
        first_mix = set_mix(first['work_id'], .7, .2)
        second_mix = set_mix(second['work_id'], .35, .8)
        for index, item in enumerate(report['packages']): apply(item, 'scene-'+str(index+1))
        for item, expected, label in [(first, first_mix, 'switch-back-first'), (second, second_mix, 'switch-back-second')]:
            value = apply(item, label)
            assert value['sound_mixes'][first['work_id']] == first_mix and value['sound_mixes'][second['work_id']] == second_mix
            assert value['sound_mixes'][item['work_id']] == expected
        state('final-five-preserved')
        close()
        report['status'] = 'passed-five-scenes-windowed-api-audio-not-UI-gesture-or-RC'
    finally:
        try: close()
        finally: write(out/'report.json', report)

def main():
    p = argparse.ArgumentParser(description=__doc__)
    p.add_argument('--delivery', type=Path, required=True); p.add_argument('--packages', type=Path, required=True)
    p.add_argument('--trial-data', type=Path); p.add_argument('--child-output', type=Path)
    a = p.parse_args()
    if a.child_output: child(a); return
    packages(a.packages.resolve())
    run = uuid.uuid4().hex
    out = Path(__file__).resolve().parent/run; out.mkdir()
    data = a.trial_data or ROOT/'Validation/Sdk'/('five'+run[:8])/'TrialData'
    assert not data.exists(), data
    compile_probe(ROOT/'tools/wallpapers/process_sound_capture.cpp', out/'Capture.exe')
    write(out/'inputs.json', {'delivery': str(a.delivery.resolve()), 'assembly_report_sha256': sha(a.delivery/'assembly-report.json'), 'packages_json': str(a.packages.resolve()), 'packages_json_sha256': sha(a.packages), 'script_sha256': sha(Path(__file__)), 'trial_data': str(data.resolve())})
    receipt = {}
    try:
        with (out/'toolchain.log').open('w', encoding='utf-8') as log:
            run_toolchain([sys.executable, str(Path(__file__).resolve()), '--delivery', str(a.delivery.resolve()), '--packages', str(a.packages.resolve()), '--trial-data', str(data.resolve()), '--child-output', str(out)], ROOT, log, 300, receipt, env=os.environ.copy())
    finally: write(out/'process.json', receipt)
    print(out, flush=True)
if __name__ == '__main__': main()
