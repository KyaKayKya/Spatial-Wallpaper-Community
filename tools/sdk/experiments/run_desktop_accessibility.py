"""Read-only native desktop hit-test feasibility; exit 4 means desktop occluded."""
from pathlib import Path
import json
import subprocess
import uuid
import sys
import hashlib
import platform
from native_build import compile_probe

root = Path(__file__).resolve().parents[3]
out = root / 'Validation/Content/sdk-runtime/E/input-probe' / uuid.uuid4().hex
out.mkdir(parents=True)
source = Path(__file__).with_name('desktop_accessibility.cpp')
exe = out / 'probe.exe'
compile_probe(source, exe)
try:
    result = subprocess.run([str(exe),*sys.argv[1:]], capture_output=True, text=True, timeout=15)
    report = {'exitCode': result.returncode, 'stdout': result.stdout, 'stderr': result.stderr}
except subprocess.TimeoutExpired:
    report = {'timeout': True}
report['mode'] = sys.argv[1:] or ['visible-point']
report['platform'] = platform.platform()
report['sha256'] = {str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in (source,Path(__file__),exe)}
(out / 'report.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
print(out)
print(json.dumps(report))
sys.exit(124 if report.get('timeout') else report['exitCode'])
