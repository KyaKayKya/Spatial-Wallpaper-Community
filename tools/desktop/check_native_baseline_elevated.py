"""One UAC prompt for official PresentMon; eight bounded native baselines, no build or recording."""
from pathlib import Path
import argparse
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
import shutil
import subprocess
import time
import uuid
from check_native_baseline import ROOT, ORIGINALS, Client, psutil, read, observation, exposed_desktop, ImageGrab, summarize, finish, require_complete_timing


class ShellExecuteInfo(c.Structure):
    _fields_ = [('cbSize', w.DWORD), ('fMask', w.ULONG), ('hwnd', w.HWND), ('lpVerb', w.LPCWSTR),
                ('lpFile', w.LPCWSTR), ('lpParameters', w.LPCWSTR), ('lpDirectory', w.LPCWSTR),
                ('nShow', c.c_int), ('hInstApp', w.HINSTANCE), ('lpIDList', c.c_void_p),
                ('lpClass', w.LPCWSTR), ('hkeyClass', w.HKEY), ('dwHotKey', w.DWORD),
                ('hIcon', w.HANDLE), ('hProcess', w.HANDLE)]


class ElevatedCapture:
    def __init__(self, executable, arguments):
        self.kernel = c.WinDLL('kernel32', use_last_error=True)
        self.kernel.GetExitCodeProcess.argtypes = [w.HANDLE, c.POINTER(w.DWORD)]
        self.kernel.CloseHandle.argtypes = [w.HANDLE]
        self.kernel.TerminateProcess.argtypes = [w.HANDLE, w.UINT]
        shell = c.WinDLL('shell32', use_last_error=True)
        shell.ShellExecuteExW.argtypes = [c.POINTER(ShellExecuteInfo)]
        shell.ShellExecuteExW.restype = w.BOOL
        request = ShellExecuteInfo()
        request.cbSize, request.fMask, request.lpVerb = c.sizeof(request), 0x40, 'runas'
        request.lpFile, request.lpParameters, request.lpDirectory = str(executable), subprocess.list2cmdline(arguments), str(executable.parent)
        request.nShow = 0
        print('UAC: official PresentMon; allow the Windows prompt to start bounded read-only capture.', flush=True)
        if not shell.ShellExecuteExW(c.byref(request)):
            raise c.WinError(c.get_last_error())
        self.handle = request.hProcess

    def poll(self):
        result = w.DWORD()
        if not self.kernel.GetExitCodeProcess(self.handle, c.byref(result)):
            raise c.WinError(c.get_last_error())
        return None if result.value == 259 else result.value

    def wait(self, timeout):
        deadline = time.monotonic() + timeout
        while self.poll() is None:
            if time.monotonic() > deadline:
                raise TimeoutError('elevated PresentMon timed capture did not exit')
            time.sleep(.2)
        return self.poll()

    def terminate(self):
        if not self.kernel.TerminateProcess(self.handle, 1):
            raise c.WinError(c.get_last_error())

    def close(self):
        self.kernel.CloseHandle(self.handle)


def qpc_ms():
    count, frequency = c.c_longlong(), c.c_longlong()
    kernel = c.windll.kernel32
    assert kernel.QueryPerformanceCounter(c.byref(count)) and kernel.QueryPerformanceFrequency(c.byref(frequency))
    return count.value * 1000 / frequency.value


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--launcher', type=Path, required=True)
    parser.add_argument('--calibration', type=Path, required=True)
    parser.add_argument('--seconds', type=int, default=60)
    args = parser.parse_args()
    assert args.seconds == 60, 'this acceptance capture is exactly 60 seconds per original'
    output = ROOT / 'Validation/NB' / uuid.uuid4().hex[:12]
    output.mkdir(parents=True)
    print(output, flush=True)
    calibration, desktop = output / 'calibration', output / 'desktop'
    shutil.copytree(args.calibration, calibration)
    settings = read(calibration / 'settings.json')
    profile = read(calibration / 'presets' / (settings['selected_preset_id'] + '.json'))
    launcher = args.launcher.resolve()
    executable = ROOT / 'Validation/Dependencies/PresentMon/2.6.0/PresentMon-2.6.0-x64.exe'
    digest = hashlib.sha256(executable.read_bytes()).hexdigest()
    assert digest == 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af'
    report = {'status': 'failed', 'scope': __doc__, 'seconds_per_scene': 60, 'source_commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(),
              'assembly_sha256': hashlib.sha256((launcher.parent / 'SpatialWallpaper_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),
              'presentmon_sha256': digest, 'timed_capture_limit_seconds': 600, 'cases': [],
              'not_measured': ['Unity per-thread CPU timing', 'per-process VRAM', 'moving-eye performance', 'physical multi-display', 'long frozen RC tests']}
    client = monitor = None
    try:
        process = subprocess.Popen([str(launcher), '--desktop-data-root', str(desktop), '--data-root', str(calibration), '--tracking-scenario', 'fixed',
                                    '-screen-fullscreen', '0', '-screen-width', '800', '-screen-height', '640', '-logFile', str(output / 'manager.log')])
        descriptor = desktop / 'runtime/connection.json'
        deadline = time.monotonic() + 120
        while not descriptor.exists():
            assert process.poll() in (None, 0) and time.monotonic() < deadline, 'runtime discovery failed'
            time.sleep(.1)
        client = Client(read(descriptor))
        while not client.call('GetDesktopState', {}).get('ready'):
            assert time.monotonic() < deadline, 'runtime initialization timeout'
            time.sleep(.1)
        client.call('DesktopCalibrationSelection', {'selected': profile['id'], 'applied': profile, 'pause_when_invisible': False})
        client.call('ConfigureDesktopCamera', {'camera_id': 'simulation', 'camera_name': 'fixed synthetic', 'mode_id': None, 'simulate': True})

        def apply(scene):
            state = client.call('ApplyDesktop', {'artwork_id': scene, 'display_id': profile['manual']['display_id'], 'preset_id': profile['id'], 'preset_revision': profile['revision']})
            assert state['succeeded'], state
            return state

        state = apply(ORIGINALS[0])
        pid = state['pid']
        target = next(display for display in state['displays'] if display['id'] == state['display_id'])
        user, gdi = c.windll.user32, c.windll.gdi32
        user.GetDC.argtypes, user.GetDC.restype = [w.HWND], w.HDC
        user.ReleaseDC.argtypes = [w.HWND, w.HDC]
        gdi.GetDeviceCaps.argtypes = [w.HDC, c.c_int]
        dc = user.GetDC(None)
        try:
            report['primary_display_refresh_hz'] = gdi.GetDeviceCaps(dc, 116)
        finally:
            user.ReleaseDC(None, dc)
        assert len(state['displays']) == 1, 'this bounded capture requires verified single-target display attribution'
        rectangle = (target['x'], target['y'], target['x'] + target['width'], target['y'] + target['height'])
        report['target'] = target
        report['pid'] = pid
        with exposed_desktop(target):
            monitor = ElevatedCapture(executable.resolve(), ['--process_id', str(pid), '--output_file', str(output / 'presents.csv'), '--qpc_time_ms',
                                                          '--session_name', 'SpatialNativeBaseline-' + uuid.uuid4().hex, '--timed', '600', '--terminate_after_timed', '--no_console_stats'])
            deadline = time.monotonic() + 30
            while not (output / 'presents.csv').exists():
                assert monitor.poll() is None and time.monotonic() < deadline, 'elevated ETW did not start; inspect Windows permission and capture output'
                time.sleep(.1)
            owned = psutil.Process(pid)
            for index, scene in enumerate(ORIGINALS):
                if index:
                    apply(scene)
                time.sleep(8 if scene == 'mita' else 3)
                before = client.call('GetDesktopState', {})
                observation(before, scene)
                assert before['render_errors'] == 0 and before['camera_started'] and before['pid'] == pid
                folder = output / scene
                folder.mkdir()
                (folder / 'before.json').write_text(json.dumps(before, indent=2))
                ImageGrab.grab(bbox=rectangle, all_screens=True).save(folder / 'desktop.png')
                time.sleep(.5)
                owned.cpu_percent()
                start, samples = qpc_ms(), []
                print('Sampling', scene, '60 seconds; no recording or build', flush=True)
                deadline = time.monotonic() + 60
                while time.monotonic() < deadline:
                    assert monitor.poll() is None, 'ETW exited before all scene windows completed'
                    samples.append({'qpc_ms': qpc_ms(), 'cpu_percent_one_core_units': owned.cpu_percent(), 'rss_bytes': owned.memory_info().rss})
                    time.sleep(.5)
                end = qpc_ms()
                after = client.call('GetDesktopState', {})
                observation(after, scene)
                assert after['render_errors'] == 0
                (folder / 'after.json').write_text(json.dumps(after, indent=2))
                (folder / 'process.json').write_text(json.dumps(samples))
                report['cases'].append({'artwork': scene, 'start_qpc_ms': start, 'end_qpc_ms': end})
                (output / 'progress.json').write_text(json.dumps(report, indent=2))
                print('Sample complete', scene, flush=True)
            report['presentmon_exit_code'] = monitor.wait(timeout=150)
            assert report['presentmon_exit_code'] == 0, 'elevated PresentMon did not exit successfully'
        for case in report['cases']:
            case['timings'] = summarize(output / 'presents.csv', pid, case['start_qpc_ms'], case['end_qpc_ms'], report['primary_display_refresh_hz'])
            assert require_complete_timing(case['timings'], case['end_qpc_ms'] - case['start_qpc_ms'], report['primary_display_refresh_hz']), case['timings']['completeness_failures']
        report['status'] = 'captured-native-actual-display-gpu-development-baseline-not-RC'
    except Exception as failure:
        report['error'] = repr(failure)
        raise
    finally:
        try:
            finish(client, monitor, launcher, desktop, report)
        except Exception as failure:
            report['cleanup_error'], report['status'] = repr(failure), 'failed-cleanup-unproven'
        finally:
            if monitor:
                monitor.close()
            (output / 'report.json').write_text(json.dumps(report, indent=2, ensure_ascii=False))
    print('Report:', output / 'report.json', flush=True)
    return 0 if report['status'] == 'captured-native-actual-display-gpu-development-baseline-not-RC' else 1


if __name__ == '__main__':
    raise SystemExit(main())
