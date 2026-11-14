"""Validate the coarse trusted audio reducer independently of capture support."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib
root=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(root/'tools/sdk/experiments'))
from native_build import compile_probe
out=root/'Validation/Content/sdk-runtime/E/audio-features'/uuid.uuid4().hex
out.mkdir(parents=True);print(out,flush=True)
source=Path(__file__).with_name('AudioFeaturesChecks.cpp');exe=out/'Checks.exe'
compile_probe(source,exe)
result=subprocess.run([str(exe)],capture_output=True,text=True,timeout=10)
(out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);result.check_returncode()
paths=(source,root/'Supervisor/Interactive/AudioFeatures.h',exe)
(out/'report.json').write_text(json.dumps({'status':'partial-coarse-features-only','sha256':{str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}},indent=2))
