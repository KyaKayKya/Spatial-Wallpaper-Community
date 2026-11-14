"""Original Launch and Library native preview; focused development verification, not RC."""
import argparse
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
from pathlib import Path
import subprocess
import time
import uuid

from check_main_manager import ROOT, read, screenshot, until, windows, psutil, ImageGrab, u
from check_native_preview_window import process_creation
from native_input_cleanup import release_owed


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--launcher', type=Path, required=True)
    parser.add_argument('--sdk-runtime', type=Path, required=True)
    args = parser.parse_args()
    launcher = args.launcher.resolve()
    output = ROOT / 'Validation/Content/sdk-runtime/native-render/task3/library-native-preview' / uuid.uuid4().hex[:12]
    output.mkdir(parents=True)
    first = output / 'first-launch'
    first.mkdir()
    print(output, flush=True)
    desktop = output / 'desktop'
    common = Path(subprocess.check_output(['git', 'rev-parse', '--path-format=absolute', '--git-common-dir'], cwd=ROOT, text=True).strip()).parent
    command = [str(launcher), '--desktop-data-root', str(desktop), '--data-root', str(output / 'calibration'), '--tracking-python', str(common / 'tracker/.venv/Scripts/python.exe'), '--tracking-root', str(ROOT / 'tracker'), '--tracking-scenario', 'fixed', '--main-manager-smoke', str(first), '--main-manager-native-preview', 'true', '--sdk-runtime', str(args.sdk_runtime.resolve()), '-screen-fullscreen', '0', '-screen-width', '1100', '-screen-height', '800', '-logFile', str(first / 'manager.log')]
    frozen = [Path(__file__), ROOT / 'tools/desktop/native_input_cleanup.py']
    for folder in [launcher.parent, args.sdk_runtime]:
        frozen += [path for path in folder.rglob('*') if path.is_file()]
    inventory = {str(path.resolve()): hashlib.sha256(path.read_bytes()).hexdigest() for path in frozen}
    report = {'status': 'failed', 'scope': __doc__, 'command': command, 'inputs': inventory, 'captures': []}
    kernel = c.WinDLL('kernel32', use_last_error=True)
    kernel.OpenProcess.argtypes = [w.DWORD, w.BOOL, w.DWORD]
    kernel.OpenProcess.restype = w.HANDLE
    kernel.WaitForSingleObject.argtypes = [w.HANDLE, w.DWORD]
    kernel.TerminateProcess.argtypes = [w.HANDLE, w.UINT]
    kernel.CloseHandle.argtypes = [w.HANDLE]
    pin, manager_pid, creation = None, None, None
    cursor = w.POINT()
    assert u.GetCursorPos(c.byref(cursor))

    def owned_players():
        result = []
        for proc in psutil.process_iter(['name']):
            try:
                if proc.info['name'].lower() == 'spatialwallpaper.exe' and Path(proc.exe()).resolve() == launcher.parent / 'SpatialWallpaper.exe' and any(str(desktop) in arg for arg in proc.cmdline()):
                    result.append(proc)
            except (psutil.NoSuchProcess, psutil.AccessDenied):
                pass
        return result

    try:
        process = subprocess.Popen(command)
        seen = set()
        deadline = time.monotonic() + 240
        while time.monotonic() < deadline:
            if (first / 'capture-request.json').exists():
                request = read(first / 'capture-request.json')
                if request['name'] not in seen:
                    assert not request['desktop']
                    if pin is None:
                        manager_pid = request['pid']
                        pin = kernel.OpenProcess(0x101001, False, manager_pid)
                        assert pin
                        creation = process_creation(pin)
                    assert request['pid'] == manager_pid
                    screenshot(manager_pid, first / (request['name'] + '.png'))
                    report['captures'].append(request['name'])
                    (first / (request['name'] + '.captured')).write_text('actual composited original manager')
                    seen.add(request['name'])
                    print('Captured', request['name'], flush=True)
            if process.poll() is not None:
                break
            time.sleep(.03)
        else:
            raise TimeoutError('original Library native preview')
        report['manager_result'] = read(first / 'result.json')
        assert process.returncode == 0 and report['manager_result']['status'] == 'passed-main-manager-development-combination', report['manager_result']
        assert len(report['captures']) == 4
        report['inputs_unchanged'] = all(hashlib.sha256(Path(path).read_bytes()).hexdigest() == digest for path, digest in inventory.items())
        assert report['inputs_unchanged']
        report['status'] = 'passed-original-Library-native-preview-development'
    except Exception as error:
        report['error'] = repr(error)
        raise
    finally:
        for proc in owned_players():
            for shell in windows(proc.pid, 'SpatialWallpaper.Desktop.'):
                u.PostMessageW(shell, 0x111, 4, 0)
            if '--native-render-session' not in proc.cmdline() and '--desktop-runtime' not in proc.cmdline():
                for window in windows(proc.pid, 'UnityWndClass'):
                    u.PostMessageW(window, 0x10, 0, 0)
        deadline = time.monotonic() + 40
        while owned_players() and time.monotonic() < deadline:
            time.sleep(.05)
        if pin:
            if kernel.WaitForSingleObject(pin, 0) != 0:
                report['status'] = 'failed-owned-injector-timeout'
                assert kernel.TerminateProcess(pin, 23) and kernel.WaitForSingleObject(pin, 5000) == 0
            if (first / 'input-owed.json').exists():
                report['input_cleanup'] = release_owed(read(first / 'input-owed.json'), manager_pid, creation)
                if not report['input_cleanup']['stable_clear']:
                    report['status'] = 'failed-owned-input-cleanup'
            kernel.CloseHandle(pin)
        report['leftover_owned_players'] = [proc.pid for proc in owned_players()]
        if report['leftover_owned_players']:
            report['status'] = 'failed-owned-player-cleanup'
        u.SetCursorPos(cursor.x, cursor.y)
        (output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    return 0 if report['status'] == 'passed-original-Library-native-preview-development' else 1


if __name__ == '__main__':
    raise SystemExit(main())
