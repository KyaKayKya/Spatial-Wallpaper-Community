"""Launch the Pool Player with paths resolved independently of the shell."""
from pathlib import Path
import subprocess
import sys
import ctypes
import json
import time
import hashlib
from ctypes import wintypes

root = Path(__file__).resolve().parents[2]
mode = sys.argv[1]
name = sys.argv[2]
output = root / 'Validation/Pool' / name
output.mkdir(parents=True, exist_ok=True)
extra=sys.argv[3:]
def option(name,default):
    if name not in extra:return default
    i=extra.index(name);value=extra[i+1];del extra[i:i+2];return value
width=option('-screen-width','1600');height=option('-screen-height','900')
args = [str(root / 'Prototype/Builds/Pool/Pool.exe'), '-screen-fullscreen', '0',
        '-screen-width', width, '-screen-height', height, '-logFile', str(output / 'player.log')]
if mode == 'capture':
    args += ['--capture-output', str(output)]
elif mode == 'benchmark':
    args += ['--benchmark', '--output', str(output)]
elif mode == 'cycle':
    args += ['--scene-cycle-output', str(output)]
elif mode == 'checks':
    args += ['--pool-checks',str(output)]
else:
    raise ValueError(mode)
args += extra
def digest(path):
    h=hashlib.sha256()
    with path.open('rb') as handle:
        for block in iter(lambda:handle.read(1024*1024),b''):h.update(block)
    return h.hexdigest()
build=root/'Prototype/Builds/Pool'
manifest={'arguments':args,'build_files':{},'sources':{},'started_utc':time.strftime('%Y-%m-%dT%H:%M:%SZ',time.gmtime())}
for path in sorted(build.rglob('*')):
    if path.is_file():manifest['build_files'][path.relative_to(build).as_posix()]=digest(path)
for folder in ['Prototype/Assets/Pool','Prototype/Assets/Display','Prototype/Assets/Showcase/Runtime','Prototype/Assets/Tracking']:
    for path in sorted((root/folder).rglob('*')):
        if path.is_file():manifest['sources'][path.relative_to(root).as_posix()]=digest(path)
(output/'run-manifest.json').write_text(json.dumps(manifest,indent=2))
process = subprocess.Popen(args, cwd=root / 'Prototype')
if mode == 'benchmark':
    time.sleep(3)
    user32 = ctypes.windll.user32
    user32.GetForegroundWindow.restype = wintypes.HWND
    records = []
    @ctypes.WINFUNCTYPE(wintypes.BOOL, wintypes.HWND, wintypes.LPARAM)
    def inspect_window(hwnd, _):
        pid = wintypes.DWORD()
        user32.GetWindowThreadProcessId(hwnd, ctypes.byref(pid))
        if pid.value == process.pid and user32.IsWindowVisible(hwnd):
            rect = wintypes.RECT()
            user32.GetClientRect(hwnd, ctypes.byref(rect))
            records.append(dict(visible=True, minimized=bool(user32.IsIconic(hwnd)),
                                foreground=hwnd == user32.GetForegroundWindow(),
                                width=rect.right, height=rect.bottom))
        return True
    user32.EnumWindows(inspect_window, 0)
    (output / 'window-state.json').write_text(json.dumps(records, indent=2))
code = process.wait(timeout=900)
print('Player exit', code, output)
sys.exit(code)
