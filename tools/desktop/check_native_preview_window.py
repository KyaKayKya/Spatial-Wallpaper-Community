"""Real Unity native preview/crop input checks; bounded development, not RC."""
import argparse
import ctypes
from ctypes import wintypes
import hashlib
import json
import pathlib
import shutil
import subprocess
import sys
import time
import uuid

ROOT = pathlib.Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/sdk'))
from case_process import Job
from check_contracts import DATA
from windows_framework import FRAMEWORK, references
sys.path.insert(0, str(ROOT / 'Validation/Dependencies/python'))
from PIL import ImageGrab
from native_input_cleanup import release_owed


def process_creation(handle):
    kernel = ctypes.windll.kernel32
    kernel.GetProcessTimes.argtypes = [wintypes.HANDLE, *([ctypes.POINTER(wintypes.FILETIME)] * 4)]
    times = [wintypes.FILETIME() for _ in range(4)]
    if not kernel.GetProcessTimes(handle, *[ctypes.byref(value) for value in times]):
        raise ctypes.WinError()
    return times[0].dwLowDateTime | times[0].dwHighDateTime << 32


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['supervisor', 'player', 'worker', 'program']:
        parser.add_argument('--' + name, type=pathlib.Path, required=True)
    parser.add_argument('--case', choices=['crop', 'pause', 'menu', 'region', 'disconnect', 'other-window', 'other-app', 'all-input', 'icon', 'taskbar', 'backpressure', 'backpressure-revoke', 'switch', 'script-crash', 'script-deadline', 'script-budget', 'pipe-block', 'pipe-saturation', 'event-saturation'], default='crop')
    parser.add_argument('--hit-helper', type=pathlib.Path)
    parser.add_argument('--qa-partial-click', action='store_true')
    parser.add_argument('--qa-hang-after-down', action='store_true')
    parser.add_argument('--trusted-ui', action='store_true')
    args = parser.parse_args()
    if args.case in ['pipe-block', 'pipe-saturation', 'event-saturation'] and not args.trusted_ui:
        parser.error('real pipe cases require --trusted-ui for the single-collector product path')
    if args.case in ['icon', 'taskbar'] and not args.hit_helper:
        parser.error('--hit-helper is required for real Shell exclusions')
    output = ROOT / 'Validation/Content/sdk-runtime/native-render/task3/native-preview-window' / uuid.uuid4().hex[:12]
    output.mkdir(parents=True)
    run = ROOT / 'Validation/NB' / ('native-preview-' + output.name)
    print(output, flush=True)
    for name in ['Supervisor.exe', 'Supervisor.exe.config', 'Newtonsoft.Json.dll']:
        shutil.copyfile(args.supervisor / name, output / name)
    shutil.copyfile(args.supervisor / 'Supervisor.exe.config', output / 'Checks.exe.config')
    sources = [ROOT / 'tools/desktop/NativePreviewWindowChecks.cs']
    sources += [ROOT / 'Prototype/Assets/Desktop/Windows' / name for name in ['NativeRenderProcess.cs', 'WindowsDesktopWindow.cs']]
    sources += [ROOT / 'Prototype/Assets/Desktop/Core/DesktopDisplay.cs']
    sources += [ROOT / name for name in ['Supervisor/Interactive/DesktopHitClient.cs', 'Supervisor/Windows/Native.cs']]
    frozen = sources + [pathlib.Path(__file__), ROOT / 'tools/desktop/native_input_cleanup.py', ROOT / 'tools/sdk/case_process.py', args.program]
    if args.hit_helper:
        frozen.append(args.hit_helper)
    for folder in [args.supervisor, args.worker, args.player.parent]:
        frozen += [path for path in folder.rglob('*') if path.is_file()]
    inventory = {str(path.resolve()): hashlib.sha256(path.read_bytes()).hexdigest() for path in frozen}
    report = {'status': 'failed', 'scope': __doc__, 'sources_sha256': inventory}
    command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'),
               '-nologo', '-langversion:latest', '-platform:x64', *references(),
               '-r:' + str(FRAMEWORK / 'System.Drawing.dll'), '-r:' + str(FRAMEWORK / 'System.Windows.Forms.dll'),
               '-r:' + str(output / 'Supervisor.exe'), '-r:' + str(output / 'Newtonsoft.Json.dll'), '-out:' + str(output / 'Checks.exe'), *map(str, sources)]
    result = subprocess.run(command, capture_output=True, text=True)
    (output / 'compile.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    print(result.stdout + result.stderr)
    report['compile_exit_code'] = result.returncode
    if result.returncode:
        (output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
        return result.returncode
    compiled = {str(path.resolve()): hashlib.sha256(path.read_bytes()).hexdigest()
                for path in output.iterdir() if path.suffix in ['.exe', '.dll', '.config']}
    report['compiled_sha256'] = compiled
    job, process, creation = Job(), None, None
    shell, desktop_exposed = None, False
    user = ctypes.windll.user32
    user.FindWindowW.argtypes = [wintypes.LPCWSTR, wintypes.LPCWSTR]
    user.FindWindowW.restype = wintypes.HWND
    user.SendMessageTimeoutW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM, wintypes.UINT, wintypes.UINT, ctypes.POINTER(ctypes.c_size_t)]
    cursor = wintypes.POINT()
    ctypes.windll.user32.GetCursorPos(ctypes.byref(cursor))
    with (output / 'result.log').open('w', encoding='utf-8') as log:
        try:
            if args.case == 'icon':
                shell = user.FindWindowW('Shell_TrayWnd', None)
                result = ctypes.c_size_t()
                assert shell and user.SendMessageTimeoutW(shell, 0x111, 419, 0, 2, 2000, ctypes.byref(result)), 'original Shell desktop exposure failed'
                desktop_exposed = True
                report['shell_exposure'] = 'original Shell MinimizeAll before owned preview; UndoMinimizeAll in finally'
                time.sleep(.2)
            process = subprocess.Popen([str(output / 'Checks.exe'), str(args.player.resolve()), str(run),
                                        str((args.supervisor / 'Supervisor.exe').resolve()), str(args.worker.resolve()), str(args.program.resolve()), args.case,
                                        *(['--qa-partial-click'] if args.qa_partial_click else []),
                                        *(['--qa-hang-after-down'] if args.qa_hang_after_down else []),
                                        *(['--trusted-ui'] if args.trusted_ui else []),
                                        *(['--hit-helper', str(args.hit_helper.resolve())] if args.hit_helper else [])],
                                       stdin=subprocess.PIPE, stdout=log, stderr=log, text=True)
            job.assign(process)
            creation = process_creation(process._handle)
            process.stdin.write('RUN\n')
            process.stdin.close()
            deadline = time.monotonic() + (12 if args.qa_hang_after_down else 100)
            while process.poll() is None:
                if time.monotonic() >= deadline:
                    raise subprocess.TimeoutExpired(process.args, 12 if args.qa_hang_after_down else 100)
                for name in ['positive', 'negative']:
                    request, marker = run / (name + '.json'), run / (name + '.captured')
                    if request.is_file() and not marker.exists():
                        try:
                            capture = json.loads(request.read_text(encoding='utf-8-sig'))
                        except json.JSONDecodeError:
                            continue
                        ImageGrab.grab(bbox=(capture['x'], capture['y'], capture['x'] + capture['width'], capture['y'] + capture['height']), all_screens=True).save(run / (name + '.png'))
                        marker.write_text('actual owned native preview image', encoding='utf-8')
                time.sleep(.02)
            report['exit_code'] = process.returncode
            report['status'] = 'passed-native-preview-window-development' if process.returncode == 0 else 'failed'
        except Exception as error:
            report['error'] = repr(error)
        finally:
            active = job.active()
            if process is not None and process.poll() is not None:
                deadline = time.monotonic() + .5
                while active and time.monotonic() < deadline:
                    time.sleep(.01)
                    active = job.active()
            report['job_active_before_fallback'] = active
            if active:
                job.terminate()
                report['status'] = 'failed-owned-job-fallback'
            deadline = time.monotonic() + 5
            while job.active() and time.monotonic() < deadline:
                time.sleep(.01)
            report['job_active_after_cleanup'] = job.active()
            job.wait_members()
            job.close()
            try:
                if process is not None:
                    report['injector_exit_code_after_cleanup'] = process.wait(timeout=5)
                ledger = run / 'injection-owed.json'
                if ledger.is_file():
                    record = json.loads(ledger.read_text(encoding='utf-8-sig'))
                    assert process.poll() is not None and not report['job_active_after_cleanup'], 'injector exit not proven'
                    report['input_cleanup'] = release_owed(record, process.pid, creation)
                    assert report['input_cleanup']['stable_clear'], 'owned injected input did not remain clear for 50ms'
                    assert not report['input_cleanup']['remaining_held'], 'owned injected input remains held'
            except Exception as error:
                report['status'], report['input_cleanup_error'] = 'failed', repr(error)
            report['primary_held_after_cleanup'] = bool(ctypes.windll.user32.GetAsyncKeyState(1) & 0x8000)
            if active:
                ctypes.windll.user32.SetCursorPos(cursor.x, cursor.y)
            report['inputs_unchanged'] = all(hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest() == digest for path, digest in {**inventory, **compiled}.items())
            if not report['inputs_unchanged'] or report['job_active_after_cleanup'] or report['primary_held_after_cleanup']:
                report['status'] = 'failed'
            if desktop_exposed:
                result = ctypes.c_size_t()
                report['shell_exposure_restored'] = bool(user.SendMessageTimeoutW(shell, 0x111, 416, 0, 2, 2000, ctypes.byref(result)))
                if not report['shell_exposure_restored']:
                    report['status'] = 'failed-shell-exposure-restore'
            report['fixture_report'] = run.relative_to(ROOT).as_posix() + '/report.json'
            (output / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print((output / 'result.log').read_text(encoding='utf-8')[-7000:])
    return 0 if report['status'] == 'passed-native-preview-window-development' else 1


if __name__ == '__main__':
    raise SystemExit(main())
