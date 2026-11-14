"""Exercise helper telemetry with controlled Interactive API substitutes; no desktop calls."""
from pathlib import Path
import sys,uuid,subprocess,json,hashlib
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
sys.path.insert(0,str(ROOT/'tools/sdk'))
from native_build import compile_probe
from provenance import sources
out=ROOT/'Validation/Content/sdk-runtime/E/desktop-hit-trace'/uuid.uuid4().hex
out.mkdir(parents=True);print(out,flush=True)
report={'status':'failed','sources':sources()}
try:
 compile_probe(ROOT/'Supervisor/Tests/DesktopHitTraceChecks.cpp',out/'Checks.exe')
 report['checks_sha256']=hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest()
 run=subprocess.run([str(out/'Checks.exe')],capture_output=True,text=True,timeout=10)
 (out/'result.log').write_text(run.stdout+run.stderr,encoding='utf-8');print(run.stdout+run.stderr)
 report['exit_code']=run.returncode;run.check_returncode()
 assert report['sources']==sources()
 report['status']='passed-telemetry-boundary-and-interactive-mock-checks'
finally:
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
