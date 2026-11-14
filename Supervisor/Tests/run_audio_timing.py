"""Deterministic suspension and stale-capture regressions for native audio timing."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'tools/sdk/experiments'))
from native_build import compile_probe
out=root/'Validation/Content/sdk-runtime/E/audio-timing'/uuid.uuid4().hex;out.mkdir(parents=True)
source=Path(__file__).with_name('AudioTimingChecks.cpp');exe=out/'Checks.exe';print(out,flush=True)
compile_probe(source,exe)
result=subprocess.run([str(exe)],capture_output=True,text=True,timeout=5)
(out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr)
files=(source,root/'Supervisor/Interactive/AudioTiming.h',exe)
(out/'report.json').write_text(json.dumps({'exitCode':result.returncode,'sha256':{str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}},indent=2))
result.check_returncode()
