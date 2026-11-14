"""Actual Win32 focus/key routing on owned fixture windows; no desktop claim."""
from pathlib import Path
import hashlib
import json
import platform
import subprocess
import uuid
from native_build import compile_probe

root = Path(__file__).resolve().parents[3]
out = root / 'Validation/Content/sdk-runtime/E/input-focus-probe' / uuid.uuid4().hex
out.mkdir(parents=True)
source = Path(__file__).with_name('input_focus_window.cpp')
exe = out / 'probe.exe'
compile_probe(source, exe)
try:
    result = subprocess.run([str(exe)], capture_output=True, text=True, timeout=10)
    report = {'exitCode': result.returncode, 'stdout': result.stdout, 'stderr': result.stderr}
except subprocess.TimeoutExpired:
    report = {'timeout': True}
report['platform'] = platform.platform()
report['scope'] = 'owned-window mechanism only; Explorer activation and mouse isolation unverified'
report['sha256'] = {str(p.relative_to(root)): hashlib.sha256(p.read_bytes()).hexdigest()
                    for p in (source, Path(__file__), exe)}
(out / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(out)
print(json.dumps(report))
raise SystemExit(124 if report.get('timeout') else report['exitCode'])
