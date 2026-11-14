"""Bounded same-Showcase native bridge timings; actual display/GPU, never RC."""
import argparse
import ctypes as c
from ctypes import wintypes as w
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
from check_contracts import DATA
from windows_framework import references, FRAMEWORK
from case_process import Job
from check_native_baseline import summarize, require_complete_timing, exposed_desktop, ImageGrab, psutil
from check_native_baseline_elevated import ElevatedCapture, qpc_ms


def read(path):
    try:
        return json.loads(path.read_text(encoding='utf-8-sig'))
    except (FileNotFoundError, json.JSONDecodeError):
        return None  # The trusted fixture writes its progress independently.


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    for name in ['supervisor', 'player', 'worker', 'program']:
        parser.add_argument('--' + name, type=pathlib.Path, required=True)
    parser.add_argument('--seconds', type=int, choices=[10, 60], default=10)
    parser.add_argument('--vsync', type=int, choices=[-1, 0, 1], default=-1)
    parser.add_argument('--queued-frames', type=int, choices=[-1, 1, 2], default=-1)
    parser.add_argument('--preview-click-load', action='store_true')
    parser.add_argument('--trace-input', action='store_true')
    parser.add_argument('--input-diagnostic', action='store_true')
    parser.add_argument('--native-focus-probe', action='store_true')
    args = parser.parse_args()
    if args.input_diagnostic:
        assert args.preview_click_load and args.trace_input and args.seconds == 10
    if args.native_focus_probe:
        assert args.input_diagnostic
    if args.preview_click_load and (args.vsync != -1 or args.queued_frames != -1):
        parser.error('preview click-load measures the automatic product policy, without diagnostic overrides')
    out = ROOT / 'Validation/Content/sdk-runtime/native-render/task3/native-bridge-timing' / uuid.uuid4().hex[:12]
    out.mkdir(parents=True)
    run = ROOT / 'Validation/NB' / ('native-bridge-timing-' + out.name)
    run.mkdir(parents=True)
    print(out, flush=True)
    for name in ['Supervisor.exe', 'Newtonsoft.Json.dll']:
        shutil.copyfile(args.supervisor / name, out / name)
    shutil.copyfile(args.supervisor / 'Supervisor.exe.config', out / 'NativeBridgeTimingChecks.exe.config')
    sources = [ROOT / ('tools/desktop/NativePreviewTimingChecks.cs' if args.preview_click_load else 'tools/desktop/NativeBridgeTimingChecks.cs')] + [ROOT / 'Prototype/Assets/Desktop/Windows' / name for name in ['NativeRenderProcess.cs', 'NativePreviewTarget.cs' if args.preview_click_load else 'NativeDesktopTarget.cs', 'WindowsDesktopWindow.cs']] + [ROOT / 'Prototype/Assets/Desktop/Core/DesktopDisplay.cs']
    capture_source = ROOT / 'tools/desktop/PresentMonPhaseCaptureHost.cs'
    frozen_paths = sources + [capture_source, pathlib.Path(__file__), args.program]
    if args.native_focus_probe:
        frozen_paths += [ROOT / 'tools/desktop/NativeFocusTrace.cpp', ROOT / 'tools/sdk/experiments/native_build.py']
    frozen_paths += [p for p in args.supervisor.rglob('*') if p.is_file()]
    if args.preview_click_load:
        frozen_paths += [ROOT / 'tools/desktop/native_input_cleanup.py', ROOT / 'tools/desktop/check_native_preview_window.py']
    frozen_paths += [p for p in args.worker.rglob('*') if p.is_file()]
    frozen_paths += [p for p in args.player.parent.rglob('*') if p.is_file()]
    frozen_hashes = {str(p.resolve()): hashlib.sha256(p.read_bytes()).hexdigest() for p in frozen_paths}
    preview_references = ['-r:' + str(FRAMEWORK / name) for name in ['System.Drawing.dll', 'System.Windows.Forms.dll']] if args.preview_click_load else []
    command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'), '-nologo', '-langversion:latest', '-platform:x64', *references(), *preview_references, '-r:' + str(out / 'Supervisor.exe'), '-r:' + str(out / 'Newtonsoft.Json.dll'), '-out:' + str(out / 'NativeBridgeTimingChecks.exe'), *map(str, sources)]
    compiled = subprocess.run(command, capture_output=True, text=True)
    (out / 'compile.log').write_text(compiled.stdout + compiled.stderr, encoding='utf-8')
    print(compiled.stdout + compiled.stderr, flush=True)
    if compiled.returncode:
        return compiled.returncode
    compiled = subprocess.run([str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'), '-nologo', '-langversion:latest', '-platform:x64', *references(), '-r:' + str(out / 'Newtonsoft.Json.dll'), '-out:' + str(out / 'PresentMonPhaseCaptureHost.exe'), str(capture_source)], capture_output=True, text=True)
    (out / 'capture-compile.log').write_text(compiled.stdout + compiled.stderr, encoding='utf-8')
    if compiled.returncode:
        print(compiled.stdout + compiled.stderr)
        return compiled.returncode
    if args.native_focus_probe:
        sys.path.insert(0, str(ROOT / 'tools/sdk/experiments'))
        from native_build import compile_probe
        compile_probe(ROOT / 'tools/desktop/NativeFocusTrace.cpp', out / 'NativeFocusTrace.dll', library=True)
    compiled_hashes = {str(p.resolve()): hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir() if p.suffix in ['.exe', '.dll', '.config']}
    executable = ROOT / 'Validation/Dependencies/PresentMon/2.6.0/PresentMon-2.6.0-x64.exe'
    digest = hashlib.sha256(executable.read_bytes()).hexdigest()
    assert digest == 'b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af'
    from check_main_manager import u
    display = {'x': 0, 'y': 0, 'width': u.GetSystemMetrics(0), 'height': u.GetSystemMetrics(1)}
    user, gdi = c.windll.user32, c.windll.gdi32
    user.GetDC.argtypes, user.GetDC.restype = [w.HWND], w.HDC
    user.ReleaseDC.argtypes = [w.HWND, w.HDC]
    gdi.GetDeviceCaps.argtypes = [w.HDC, c.c_int]
    dc = user.GetDC(None)
    try:
        refresh = gdi.GetDeviceCaps(dc, 116)
    finally:
        user.ReleaseDC(None, dc)
    report = {'status': 'failed', 'scope': __doc__, 'run': run.relative_to(ROOT).as_posix(), 'primary_display_refresh_hz': refresh,
              'preview_click_load': args.preview_click_load, 'input_diagnostic': args.input_diagnostic, 'trace_input': args.trace_input, 'native_focus_probe': args.native_focus_probe,
              'source_commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], text=True).strip(), 'presentmon_sha256': digest,
              'assembly_sha256': hashlib.sha256((args.player.parent / 'SpatialWallpaper_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),
              'program_sha256': hashlib.sha256(args.program.read_bytes()).hexdigest(), 'cases': [],
              'not_measured': ['Physical Tracker latency', 'per-process VRAM', 'Unity per-thread CPU', 'other original assets', 'formal Launch combinations', 'RC long tests']}
    job, process, monitor, injector_creation = Job(), None, None, None
    cursor = w.POINT()
    assert user.GetCursorPos(c.byref(cursor))
    cleanup = {}
    logs = (out / 'result.log').open('w', encoding='utf-8')
    command = [str(out / 'NativeBridgeTimingChecks.exe'), str(args.player.resolve()), str(run), str((args.supervisor / 'Supervisor.exe').resolve()), str(args.worker.resolve()), str(args.program.resolve()), str(args.seconds), str(args.vsync), str(args.queued_frames)]
    if args.trace_input:
        assert args.preview_click_load
        command.append('--trace-input')
    if args.input_diagnostic:
        command.append('--input-diagnostic')
    if args.native_focus_probe:
        probe = (out / 'NativeFocusTrace.dll').resolve()
        command += ['--native-focus-probe', str(probe), compiled_hashes[str(probe)]]
    try:
        with exposed_desktop(display):
            process = subprocess.Popen(command, stdin=subprocess.PIPE, stdout=logs, stderr=logs, text=True)
            job.assign(process)
            if args.preview_click_load:
                from check_native_preview_window import process_creation
                injector_creation = process_creation(process._handle)
            cleanup['assigned_before_release'] = True
            process.stdin.write('RUN\n')
            process.stdin.close()

            def wait_json(path, seconds):
                deadline = time.monotonic() + seconds
                while True:
                    result = read(path)
                    if result is not None:
                        return result
                    assert process.poll() is None, 'trusted timing driver exited; inspect result.log/report.json'
                    assert time.monotonic() < deadline, 'timing fixture deadline: ' + str(path)
                    if monitor:
                        assert monitor.poll() is None, 'ETW exited before measured phases completed'
                    time.sleep(.05)

            for movement in (['fixed'] if args.input_diagnostic else ['fixed', 'moving']):
                for mode in (['bridge'] if args.input_diagnostic else ['no-bridge', 'bridge', 'script-fault']):
                    folder = run / (movement + '-' + mode)
                    ready = wait_json(folder / 'ready.json', 50)
                    assert ready['display'] == display, 'unexpected target display bounds'
                    if monitor is None and not args.input_diagnostic:
                        print('UAC host: six bounded official PresentMon captures, each restricted to the original renderer PID.', flush=True)
                        monitor = ElevatedCapture((out / 'PresentMonPhaseCaptureHost.exe').resolve(), [str(executable.resolve()), str(run), str(args.player.resolve())])
                    assert monitor is None or monitor.poll() is None, 'ETW exited before sampling'
                    deadline = time.monotonic() + 40
                    while not args.input_diagnostic and not (folder / 'capture-armed').exists():
                        assert monitor.poll() is None and process.poll() is None and time.monotonic() < deadline, 'per-PID capture did not arm; inspect presentmon-host.log'
                        time.sleep(.05)
                    rectangle = (0, 0, display['width'], display['height'])
                    ImageGrab.grab(bbox=rectangle, all_screens=True).save(folder / 'desktop-before.png')
                    owned = psutil.Process(ready['pid'])
                    # Pin the renderer identity against PID reuse before sampling.
                    creation = owned.create_time()
                    owned.cpu_percent()
                    (folder / 'go').write_text('start measured window', encoding='utf-8')
                    samples, deadline = [], time.monotonic() + args.seconds + 15
                    while read(folder / 'sample.json') is None:
                        assert process.poll() is None and (monitor is None or monitor.poll() is None), 'timing process exited during measurement'
                        assert time.monotonic() < deadline and owned.create_time() == creation, 'measurement deadline or process identity changed'
                        samples.append({'qpc_ms': qpc_ms(), 'cpu_percent_one_core_units': owned.cpu_percent(), 'rss_bytes': owned.memory_info().rss})
                        time.sleep(.5)
                    row = read(folder / 'sample.json')
                    assert row is not None
                    samples = [s for s in samples if row['start_qpc_ms'] <= s['qpc_ms'] < row['end_qpc_ms']]
                    (folder / 'process.json').write_text(json.dumps(samples, indent=2), encoding='utf-8')
                    ImageGrab.grab(bbox=rectangle, all_screens=True).save(folder / 'desktop-after.png')
                    (folder / 'captured').write_text('actual desktop screenshots outside timing window', encoding='utf-8')
                    report['cases'].append({'name': folder.name, 'pid': ready['pid'], 'start_qpc_ms': row['start_qpc_ms'], 'end_qpc_ms': row['end_qpc_ms'], 'process_samples': len(samples), 'process_creation_unix': creation})
                    (out / 'progress.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
                    print('Measured', folder.name, flush=True)
            assert process.wait(timeout=40) == 0, 'timing driver failed cleanup'
            fixture = read(run / 'report.json')
            assert fixture and fixture['status'] == 'controlled-native-bridge-timing-phases-complete-not-RC', fixture
        # Only the official monitor remains; allow its bounded capture to finish.
        report['presentmon_exit_code'] = monitor.wait(timeout=180) if monitor else None
        assert args.input_diagnostic or report['presentmon_exit_code'] == 0, 'PresentMon failed'
        for row in ([] if args.input_diagnostic else report['cases']):
            row['timings'] = summarize(run / row['name'] / 'presents.csv', row['pid'], row['start_qpc_ms'], row['end_qpc_ms'], refresh)
            assert require_complete_timing(row['timings'], row['end_qpc_ms'] - row['start_qpc_ms'], refresh), row['timings']['completeness_failures']
        report['status'] = 'diagnosed-native-input-focus-only-not-performance' if args.input_diagnostic else 'captured-controlled-native-bridge-display-gpu-development-not-RC'
    except Exception as error:
        report['error'] = repr(error)
        print(report['error'], flush=True)
    finally:
        if report['status'] not in ['captured-controlled-native-bridge-display-gpu-development-not-RC', 'diagnosed-native-input-focus-only-not-performance']:
            (run / 'abort').write_text('failed parent or driver; stop only this capture coordinator', encoding='utf-8')
        if process is not None and process.poll() is None:
            (run / 'abort').write_text('parent failed; close owned target', encoding='utf-8')
            try:
                process.wait(timeout=35)
            except subprocess.TimeoutExpired:
                cleanup['graceful_abort_deadline'] = True
        if monitor:
            try:
                if monitor.poll() is None:
                    # Let the fixed bounded official capture close its ETW
                    # session and preserve diagnostics even after a QA failure.
                    report['presentmon_exit_code_after_failure'] = monitor.wait(timeout=200)
            except Exception as error:
                report['status'], report['monitor_cleanup_error'] = 'failed', repr(error)
            finally:
                monitor.close()
        try:
            active = job.active()
            if process is not None and process.poll() is not None:
                deadline = time.monotonic() + .5
                while active and time.monotonic() < deadline:
                    time.sleep(.01)
                    active = job.active()
            cleanup['active_before_fallback'] = active
            if active:
                cleanup['remaining_job_members'] = job.members()
                job.terminate()
            deadline = time.monotonic() + 5
            while job.active() and time.monotonic() < deadline:
                time.sleep(.01)
            cleanup['active_after_cleanup'] = job.active()
            job.wait_members()
            if args.preview_click_load:
                try:
                    from native_input_cleanup import release_owed
                    if process is not None:
                        cleanup['injector_exit_code'] = process.wait(timeout=5)
                    ledger = run / 'injection-owed.json'
                    if ledger.is_file():
                        assert not cleanup['active_after_cleanup'] and process.poll() is not None
                        cleanup['input'] = release_owed(read(ledger), process.pid, injector_creation)
                        assert cleanup['input']['stable_clear'] and not cleanup['input']['remaining_held']
                except Exception as error:
                    report['status'], cleanup['input_error'] = 'failed-owned-input-cleanup', repr(error)
                finally:
                    user.SetCursorPos(cursor.x, cursor.y)
            if active or cleanup['active_after_cleanup']:
                report['status'] = 'failed-owned-job-fallback-required'
        finally:
            job.close()
            logs.close()
        report['exit_code'] = 0 if report['status'] in ['captured-controlled-native-bridge-display-gpu-development-not-RC', 'diagnosed-native-input-focus-only-not-performance'] else 1
        report['frozen_input_sha256'] = frozen_hashes
        report['compiled_sha256'] = compiled_hashes
        report['inputs_unchanged'] = all(p.is_file() and hashlib.sha256(p.read_bytes()).hexdigest() == digest for path, digest in {**frozen_hashes, **compiled_hashes}.items() for p in [pathlib.Path(path)])
        if not report['inputs_unchanged']:
            report['status'], report['exit_code'] = 'failed-inputs-changed-during-capture', 1
        report['source_sha256'] = {s.relative_to(ROOT).as_posix(): frozen_hashes[str(s.resolve())] for s in sources + [pathlib.Path(__file__)]}
        (out / 'job-cleanup.json').write_text(json.dumps(cleanup, indent=2), encoding='utf-8')
        (out / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print('Report:', out / 'report.json', flush=True)
    return report['exit_code']


if __name__ == '__main__':
    raise SystemExit(main())
