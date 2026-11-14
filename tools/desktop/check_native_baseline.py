"""Capture original Launch native scenes and bounded ETW baselines, never an RC claim."""
from pathlib import Path
import argparse
import collections
import csv
import ctypes
import hashlib
import json
import math
import shutil
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Validation/Dependencies/python'))
sys.path.insert(0, str(ROOT / 'tools/content'))
from PIL import ImageGrab
import psutil
from run_e2e import Client
from check_main_manager import exposed_desktop
from check_builtin_visibility import observation

ORIGINALS = ['showcase', 'pool', 'train', 'parking', 'minishotgame', 'limpool', 'rainycafe', 'mita']


def read(path):
    return json.loads(path.read_text(encoding='utf-8-sig'))


def percentile(values, fraction):
    return sorted(values)[max(0, math.ceil(len(values) * fraction) - 1)]


def owned_players(launcher, desktop):
    result = []
    expected = str(launcher.parent / 'SpatialWallpaper.exe').casefold()
    for process in psutil.process_iter(['name', 'exe', 'cmdline']):
        try:
            if (process.info['name'] or '').lower() == 'spatialwallpaper.exe' and (process.info['exe'] or '').casefold() == expected and str(desktop) in (process.info['cmdline'] or []):
                result.append(process)
        except (psutil.NoSuchProcess, psutil.AccessDenied):
            pass
    return result


def finish(client, monitor, launcher, desktop, report):
    if monitor and monitor.poll() is None:
        try:
            monitor.terminate()
            monitor.wait(timeout=10)
        except Exception as failure:
            report['monitor_cleanup_error'] = repr(failure)
    if client:
        try:
            client.call('QuitDesktop', {})
        except Exception as failure:
            report['quit_error'] = repr(failure)
        finally:
            try:
                client.close()
            except Exception as failure:
                report['client_close_error'] = repr(failure)
    time.sleep(3)
    remaining = owned_players(launcher, desktop)
    if remaining:
        # Only our exact executable and unique isolated data root may be closed.
        from ctypes import wintypes
        ids = {process.pid for process in remaining}
        visitor = ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
        user = ctypes.windll.user32
        user.GetWindowThreadProcessId.argtypes = [wintypes.HWND, ctypes.POINTER(wintypes.DWORD)]
        user.PostMessageW.argtypes = [wintypes.HWND, wintypes.UINT, wintypes.WPARAM, wintypes.LPARAM]
        user.GetClassNameW.argtypes = [wintypes.HWND, wintypes.LPWSTR, ctypes.c_int]
        user.GetWindowTextLengthW.argtypes = [wintypes.HWND]
        @visitor
        def close(hwnd, data):
            pid = wintypes.DWORD()
            user.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
            name = ctypes.create_unicode_buffer(128)
            if pid.value in ids and user.GetWindowTextLengthW(hwnd) > 0 and user.GetClassNameW(hwnd, name, 128) > 0 and name.value == 'UnityWndClass':
                user.PostMessageW(hwnd, 0x10, 0, 0)
            return True
        user.EnumWindows(close, 0)
        _, alive = psutil.wait_procs(remaining, timeout=10)
        report['fallback_closed_owned_players'] = sorted(ids)
        for process in alive:
            try:
                process.terminate()
            except psutil.NoSuchProcess:
                pass
        if alive:
            report['forced_owned_player_termination'] = [process.pid for process in alive]
            psutil.wait_procs(alive, timeout=10)
    report['leftover_owned_players'] = [process.pid for process in owned_players(launcher, desktop)]
    if report['leftover_owned_players'] or any(key in report for key in ('quit_error', 'client_close_error', 'monitor_cleanup_error', 'fallback_closed_owned_players')):
        report['status'] = 'failed-owned-player-cleanup'


def summarize(path, pid, start_qpc_ms=None, end_qpc_ms=None, refresh_hz=None):
    with path.open(encoding='utf-8-sig', newline='') as stream:
        rows = list(csv.DictReader(stream))
    rows = [row for row in rows if int(row['ProcessID']) == pid]
    if start_qpc_ms is not None:
        qpc_field = next((name for name in ('CPUStartQPCTimeInMs', 'CPUStartQPCTime') if rows and name in rows[0]), None)
        assert end_qpc_ms is not None and qpc_field, 'ETW QPC time boundary missing'
        rows = [row for row in rows if start_qpc_ms <= float(row[qpc_field]) < end_qpc_ms]
    counts = collections.Counter(row['SwapChainAddress'] for row in rows)
    if not counts:
        raise AssertionError('no process-specific ETW presents')
    chain = counts.most_common(1)[0][0]
    rows = [row for row in rows if row['SwapChainAddress'] == chain]
    result = {'swapchain': chain, 'swapchain_counts': dict(counts), 'rows': len(rows), 'metric_coverage': {}}
    for label, names in {'present_interval_ms': ['MsBetweenPresents', 'FrameTime'],
                         'cpu_busy_ms': ['MsCPUBusy', 'CPUBusy'],
                         'gpu_time_ms': ['MsGPUTime', 'GPUTime'],
                         'displayed_duration_ms': ['DisplayedTime', 'MsBetweenDisplayChange']}.items():
        key = next((name for name in names if name in rows[0]), None)
        counts = collections.Counter()
        values = []
        for row in rows:
            raw = row.get(key) if key else None
            try:
                value = float(raw)
            except (TypeError, ValueError):
                counts['missing' if raw in (None, '', 'NA') else 'invalid'] += 1
                continue
            if not math.isfinite(value) or value < 0:
                counts['invalid'] += 1
                continue
            counts['valid'] += 1
            counts['zero' if value == 0 else 'positive'] += 1
            # Zero CPU/GPU work is a valid observation, not a missing frame.
            if value > 0 or label in ('cpu_busy_ms', 'gpu_time_ms'):
                values.append(value)
        result['metric_coverage'][label] = {'field': key, **{name: counts[name] for name in ('valid', 'positive', 'zero', 'missing', 'invalid')},
                                            'valid_fraction': counts['valid'] / len(rows)}
        result[label] = None if not values else {'samples': len(values), 'p50': percentile(values, .5),
                                                 'p95': percentile(values, .95), 'p99': percentile(values, .99)}
        if label == 'displayed_duration_ms' and values:
            result['displayed_duration_total_ms'] = sum(values)
            worst = sorted(values, reverse=True)[:max(1, math.ceil(len(values) * .01))]
            result['displayed_1_percent_low_fps'] = 1000 / (sum(worst) / len(worst))
            result['displayed_average_fps'] = 1000 / (sum(values) / len(values))
            result['over_two_60hz_periods_fraction'] = sum(value > 1000 / 30 for value in values) / len(values)
            if refresh_hz and refresh_hz > 1:
                result['over_two_refresh_periods_fraction'] = sum(value > 2000 / refresh_hz for value in values) / len(values)
    result['not_measured'] = [key for key in ('gpu_time_ms', 'displayed_duration_ms') if result[key] is None]
    return result


def require_complete_timing(result, duration_ms, refresh_hz):
    """Gate actual time coverage; a handful of non-NA rows cannot prove a window."""
    failures = []
    if result['rows'] < 100:
        failures.append('fewer than 100 presents')
    for metric in ('present_interval_ms', 'cpu_busy_ms', 'gpu_time_ms'):
        coverage = result['metric_coverage'][metric]
        if coverage['valid_fraction'] < .95 or coverage['positive'] == 0:
            failures.append(metric + ': insufficient finite timing coverage')
    display = result['displayed_duration_ms']
    ratio = result.get('displayed_duration_total_ms', 0) / duration_ms
    # NA displayed rows can be legitimate dropped presents. Require the actual
    # displayed durations to cover the window rather than pretending NA is zero.
    if display is None or display['samples'] < 100 or not .95 <= ratio <= 1.05:
        failures.append('displayed duration does not cover the measurement window')
    result['measurement_duration_ms'] = duration_ms
    result['displayed_time_coverage_fraction'] = ratio
    result['primary_display_refresh_hz'] = refresh_hz
    result['completeness_failures'] = failures
    result['complete'] = not failures
    return not failures


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--launcher', type=Path, required=True)
    parser.add_argument('--calibration', type=Path, required=True)
    parser.add_argument('--blueprints', type=Path)
    parser.add_argument('--presentmon', type=Path, default=ROOT / 'Validation/Dependencies/PresentMon/2.6.0/PresentMon-2.6.0-x64.exe')
    parser.add_argument('--seconds', type=int, default=60)
    parser.add_argument('--no-etw', action='store_true', help='Capture visuals only; explicitly leave actual display/GPU timing unmeasured')
    parser.add_argument('--scenes', nargs='+', default=ORIGINALS)
    args = parser.parse_args()
    assert 1 <= args.seconds <= 60, 'bounded development sample only'
    assert set(args.scenes) <= set(ORIGINALS)
    # Blueprint's .NET importer adds hash and atomic-write suffixes. Keep this
    # owned test root short enough for the existing Windows MAX_PATH contract.
    output = ROOT / 'Validation/NB' / uuid.uuid4().hex[:12]
    output.mkdir(parents=True)
    print(output, flush=True)
    calibration, desktop = output / 'calibration', output / 'desktop'
    shutil.copytree(args.calibration, calibration)
    settings = read(calibration / 'settings.json')
    profile = read(calibration / 'presets' / (settings['selected_preset_id'] + '.json'))
    scenes = list(args.scenes)
    if args.blueprints:
        shutil.copytree(args.blueprints, desktop / 'blueprints')
        work, record = next(iter(read(desktop / 'blueprints/index.json')['works'].items()))
        scenes.append('blueprint-' + work + '-' + record['latest'])
    launcher = args.launcher.resolve()
    report = {'status': 'failed', 'scope': __doc__, 'seconds_per_scene': args.seconds,
              'launcher': str(launcher), 'assembly_sha256': hashlib.sha256((launcher.parent / 'SpatialWallpaper_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest(),
              'presentmon_sha256': hashlib.sha256(args.presentmon.read_bytes()).hexdigest(), 'cases': [],
              'not_measured': ['Unity per-thread CPU timing', 'per-process VRAM', 'physical multi-monitor', 'moving-eye performance', 'RC long tests']}
    client = process = monitor = None
    try:
        process = subprocess.Popen([str(launcher), '--desktop-data-root', str(desktop), '--data-root', str(calibration),
                                    '--tracking-scenario', 'fixed',
                                    '-screen-fullscreen', '0', '-screen-width', '800', '-screen-height', '640', '-logFile', str(output / 'manager.log')])
        descriptor = desktop / 'runtime/connection.json'
        deadline = time.monotonic() + 120
        while not descriptor.exists():
            assert time.monotonic() < deadline and process.poll() in (None, 0), 'Launch runtime unavailable'
            time.sleep(.1)
        client = Client(read(descriptor))
        while not client.call('GetDesktopState', {}).get('ready'):
            assert time.monotonic() < deadline, 'runtime initialization timeout'
            time.sleep(.1)
        client.call('DesktopCalibrationSelection', {'selected': profile['id'], 'applied': profile, 'pause_when_invisible': False})
        client.call('ConfigureDesktopCamera', {'camera_id': 'simulation', 'camera_name': 'fixed synthetic', 'mode_id': None, 'simulate': True})
        for scene in scenes:
            case = {'artwork': scene}
            report['cases'].append(case)
            state = client.call('ApplyDesktop', {'artwork_id': scene, 'display_id': profile['manual']['display_id'], 'preset_id': profile['id'], 'preset_revision': profile['revision']})
            assert state['succeeded'], state
            target = next(item for item in state['displays'] if item['id'] == state['display_id'])
            folder = output / scene
            folder.mkdir()
            with exposed_desktop(target):
                time.sleep(8 if scene == 'mita' else 3)
                before = client.call('GetDesktopState', {})
                observation(before, scene)
                assert before['camera_started'], 'packaged synthetic Tracker did not start'
                assert before['render_errors'] == 0, 'native render errors before capture'
                (folder / 'before.json').write_text(json.dumps(before, indent=2))
                rectangle = (target['x'], target['y'], target['x'] + target['width'], target['y'] + target['height'])
                ImageGrab.grab(bbox=rectangle, all_screens=True).save(folder / 'desktop.png')
                # Validation-only desktop recording finishes before timing; never a rendering transport.
                subprocess.run(['ffmpeg', '-hide_banner', '-loglevel', 'error', '-f', 'gdigrab', '-framerate', '10',
                                '-offset_x', str(target['x']), '-offset_y', str(target['y']), '-video_size', str(target['width']) + 'x' + str(target['height']),
                                '-i', 'desktop', '-t', '3', '-c:v', 'libx264', '-preset', 'fast', '-crf', '18', '-pix_fmt', 'yuv420p', str(folder / 'animation.mp4')], check=True)
                if args.no_etw:
                    case['timings'] = None
                    case['timing_blocker'] = 'ETW elevation unavailable; capture and video do not prove actual presentation cadence or GPU time'
                    case['visual_capture_completed'] = True
                    print(scene, 'visuals captured; timings unmeasured', flush=True)
                    continue
                time.sleep(2)
                owned = psutil.Process(before['pid'])
                owned.cpu_percent()
                with (folder / 'presentmon.log').open('w') as log:
                    monitor = subprocess.Popen([str(args.presentmon.resolve()), '--process_id', str(before['pid']), '--output_file', str(folder / 'presents.csv'),
                                                '--session_name', 'SpatialNativeBaseline-' + uuid.uuid4().hex, '--timed', str(args.seconds), '--terminate_after_timed', '--no_console_stats'], stdout=log, stderr=log)
                    samples = []
                    capture_deadline = time.monotonic() + args.seconds + 30
                    while monitor.poll() is None:
                        assert time.monotonic() < capture_deadline, 'PresentMon timed capture did not exit'
                        samples.append({'time': time.monotonic(), 'cpu_percent_one_core_units': owned.cpu_percent(), 'rss_bytes': owned.memory_info().rss})
                        time.sleep(.5)
                assert monitor.returncode == 0, 'PresentMon capture failed; inspect log'
                after = client.call('GetDesktopState', {})
                observation(after, scene)
                assert after['render_errors'] == 0, 'native render errors during timing'
                (folder / 'after.json').write_text(json.dumps(after, indent=2))
                (folder / 'process.json').write_text(json.dumps(samples))
                ImageGrab.grab(bbox=rectangle, all_screens=True).save(folder / 'desktop-end.png')
            case['timings'] = summarize(folder / 'presents.csv', before['pid'])
            assert require_complete_timing(case['timings'], args.seconds * 1000, None), case['timings']['completeness_failures']
            case['passed'] = True
            print(scene, json.dumps(case['timings']), flush=True)
        report['status'] = 'captured-native-visual-baseline-timing-unmeasured' if args.no_etw else 'captured-native-development-baseline-not-RC'
    except Exception as failure:
        report['error'] = repr(failure)
        raise
    finally:
        try:
            finish(client, monitor, launcher, desktop, report)
        except Exception as failure:
            report['status'] = 'failed-cleanup-unproven'
            report['cleanup_error'] = repr(failure)
        finally:
            (output / 'report.json').write_text(json.dumps(report, indent=2, ensure_ascii=False))
    return 0 if report['status'] in ('captured-native-development-baseline-not-RC', 'captured-native-visual-baseline-timing-unmeasured') else 1


if __name__ == '__main__':
    raise SystemExit(main())
