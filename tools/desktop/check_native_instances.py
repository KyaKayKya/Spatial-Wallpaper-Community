"""Render-only child integration; not formal Launch/desktop/RC acceptance."""
from pathlib import Path
import argparse
import ctypes as c
from ctypes import wintypes as w
import hashlib
import json
import os
import struct
import subprocess
import sys
import time
import uuid

ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'Validation/Dependencies/python'))
from PIL import Image
from check_main_manager import screenshot
from tray_roundtrip import windows

k = c.WinDLL('kernel32', use_last_error=True)
k.CreateNamedPipeW.argtypes = [w.LPCWSTR, w.DWORD, w.DWORD, w.DWORD, w.DWORD, w.DWORD, w.DWORD, c.c_void_p]
k.CreateNamedPipeW.restype = w.HANDLE
k.ConnectNamedPipe.argtypes = [w.HANDLE, c.c_void_p]
k.PeekNamedPipe.argtypes = [w.HANDLE, c.c_void_p, w.DWORD, c.c_void_p, c.POINTER(w.DWORD), c.c_void_p]
k.ReadFile.argtypes = [w.HANDLE, c.c_void_p, w.DWORD, c.POINTER(w.DWORD), c.c_void_p]
k.WriteFile.argtypes = [w.HANDLE, c.c_void_p, w.DWORD, c.POINTER(w.DWORD), c.c_void_p]
k.CloseHandle.argtypes = [w.HANDLE]
k.GetCurrentProcess.restype = w.HANDLE
k.GetProcessTimes.argtypes = [w.HANDLE, c.c_void_p, c.c_void_p, c.c_void_p, c.c_void_p]


def birth():
    values = [c.c_ulonglong() for _ in range(4)]
    assert k.GetProcessTimes(k.GetCurrentProcess(), *[c.byref(v) for v in values])
    return values[0].value


class Instance:
    def __init__(self, player, artwork, output, script=None):
        self.process = None
        self.pipe = None
        self.nonce = uuid.uuid4().hex
        self.output = output
        name = 'SpatialWallpaper.NativeCheck.' + uuid.uuid4().hex
        self.pipe = k.CreateNamedPipeW('\\\\.\\pipe\\' + name, 3, 1, 1, 16384, 16384, 0, None)
        if self.pipe == w.HANDLE(-1).value:
            raise c.WinError(c.get_last_error())
        config = {'version': 1, 'artwork': artwork, 'session': uuid.uuid4().hex, 'generation': 1,
                  'owner': {'pid': os.getpid(), 'creation_filetime': birth()}, 'pipe': name, 'nonce': self.nonce,
                  'pose': {'width': .53, 'height': .298125, 'keep_height': True, 'eye': [0, 0, -.67], 'fresh': False}}
        if script:
            config['script'] = script
        path = output / (artwork + '-instance.json')
        path.write_text(json.dumps(config), encoding='utf-8')
        self.process = subprocess.Popen([str(player), '--native-render-session', str(path), '-screen-fullscreen', '0',
                                         '-screen-width', '800', '-screen-height', '450', '-logFile', str(output / (artwork + '.log'))])
        try:
            deadline = time.monotonic() + 30
            while not k.ConnectNamedPipe(self.pipe, None):
                error = c.get_last_error()
                if error == 535:
                    break
                if error != 536:
                    raise c.WinError(error)
                assert self.process.poll() is None, 'native child exited before connection'
                if time.monotonic() > deadline:
                    raise TimeoutError('native control connection')
                time.sleep(.02)
        except BaseException:
            self.close()
            raise

    def exact(self, length):
        output = bytearray()
        deadline = time.monotonic() + 5
        while len(output) < length:
            available = w.DWORD()
            if not k.PeekNamedPipe(self.pipe, None, 0, None, c.byref(available), None):
                raise c.WinError(c.get_last_error())
            if not available.value:
                if time.monotonic() > deadline:
                    raise TimeoutError('native control reply')
                time.sleep(.005)
                continue
            data = c.create_string_buffer(min(available.value, length - len(output)))
            count = w.DWORD()
            if not k.ReadFile(self.pipe, data, len(data), c.byref(count), None):
                raise c.WinError(c.get_last_error())
            output.extend(data.raw[:count.value])
        return bytes(output)

    def command(self, operation='status', **fields):
        data = json.dumps({'nonce': self.nonce, 'operation': operation, **fields}, separators=(',', ':')).encode()
        packet = struct.pack('<I', len(data)) + data
        count = w.DWORD()
        assert k.WriteFile(self.pipe, packet, len(packet), c.byref(count), None) and count.value == len(packet)
        length, = struct.unpack('<I', self.exact(4))
        assert 0 < length <= 16384
        result = json.loads(self.exact(length))
        assert result['nonce'] == self.nonce
        return result

    def ready(self):
        deadline = time.monotonic() + 30
        while time.monotonic() < deadline:
            value = self.command()
            if value.get('ready'):
                assert value['pid'] == self.process.pid
                return value
            time.sleep(.05)
        raise TimeoutError('native scene ready')

    def close(self):
        if self.process and self.process.poll() is None:
            try:
                self.command('stop')
                self.process.wait(timeout=12)
            except BaseException:
                self.process.kill()
                self.process.wait(timeout=5)
        if self.pipe:
            k.CloseHandle(self.pipe)
            self.pipe = None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--player', type=Path, required=True)
    parser.add_argument('--script', type=Path)
    parser.add_argument('--worker', type=Path)
    parser.add_argument('--helper', type=Path)
    parser.add_argument('--ownership-checker', type=Path, help='checks real Player HWND ownership without attaching to the desktop')
    parser.add_argument('--no-capture', action='store_true', help='functional protocol only; never counts as visible image evidence')
    args = parser.parse_args()
    out = ROOT / 'Validation/NB' / uuid.uuid4().hex[:12]
    out.mkdir(parents=True)
    print(out, flush=True)
    owned = []
    report = {'status': 'failed', 'scope': 'render-only child process isolation; no DesktopRuntime/Launch/input/performance acceptance',
              'assembly_sha256': hashlib.sha256((args.player.parent / 'SpatialWallpaper_Data/Managed/Assembly-CSharp.dll').read_bytes()).hexdigest()}
    try:
        for artwork in ('showcase', 'mita'):
            specification = None
            if args.script and artwork == 'showcase':
                assert args.worker and args.helper
                payload = json.loads(args.script.read_text(encoding='utf-8-sig'))
                inventory = json.loads((args.worker / 'build.json').read_text(encoding='utf-8'))
                specification = {'node_paths': ['InteriorContent_A/ReplaceableSlot'], 'helper': str(args.helper.resolve()),
                                 'helper_sha256': hashlib.sha256(args.helper.read_bytes()).hexdigest(),
                                 'bootstrap_cache': str((out / 'bootstrap').resolve()),
                                 'launch': {'worker_image': str(args.worker.resolve()), 'worker_files': inventory['runtime_files'],
                                            'attempts': str((ROOT / 'Validation/SB' / out.name / 'attempts').resolve()),
                                            'attempt': uuid.uuid4().hex, 'program': str(args.script.resolve()),
                                            'program_sha256': hashlib.sha256(args.script.read_bytes()).hexdigest(), 'build': payload['build_id']}}
            instance = Instance(args.player.resolve(), artwork, out, specification)
            owned.append(instance)
            state = instance.ready()
            report[artwork] = state
            if args.ownership_checker:
                checked = subprocess.run([str(args.ownership_checker.resolve()), str(state['pid']), str(state['creation_filetime']), str(state['window']), str(state['render_thread'])], capture_output=True, text=True, timeout=10)
                (out / (artwork + '-window-owner.log')).write_text(checked.stdout + checked.stderr, encoding='utf-8')
                checked.check_returncode()
                report[artwork + '_window_ownership'] = 'passed-actual-player-no-desktop-attachment'
            assert state.get('render_errors', 0) == 0, 'native scene emitted render/behaviour errors'
            if specification:
                deadline = time.monotonic() + 20
                while state['script_batches'] < 10 and time.monotonic() < deadline:
                    assert not state['script_error'], state['script_error']
                    time.sleep(.05)
                    state = instance.command()
                report['script'] = state
                assert state['script_batches'] >= 10, 'real Unity/native script bridge did not commit'
            if not args.no_capture:
                screenshot(instance.process.pid, out / (artwork + '.png'))
                image = Image.open(out / (artwork + '.png')).convert('RGB')
                assert max(hi - lo for lo, hi in image.getextrema()) > 40, 'actual native image is flat'
        assert report['showcase']['pipeline'] != 'Built-in' and report['mita']['pipeline'] == 'Built-in'
        before = owned[1].command()['frame']
        owned[0].process.kill()
        owned[0].process.wait(timeout=5)
        time.sleep(.5)
        after = owned[1].command()
        assert after['frame'] > before and after['ready'], 'other pipeline continues after owned peer crash'
        assert after.get('render_errors', 0) == 0, 'surviving native scene emitted errors'
        report['survivor'] = after
        report['status'] = 'passed-render-only-isolation-not-product-acceptance'
        report['capture_attempted'] = not args.no_capture
    finally:
        for instance in reversed(owned):
            instance.close()
        report['owned_processes_remaining'] = [i.process.pid for i in owned if i.process.poll() is None]
        (out / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    return 0


if __name__ == '__main__':
    raise SystemExit(main())
