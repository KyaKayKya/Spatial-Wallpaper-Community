"""Exercise the trusted input state machine; this is not native desktop acceptance."""
from pathlib import Path
import sys, subprocess, uuid, json
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references
from provenance import sources
out=ROOT/'Validation/Content/sdk-runtime/E/input-gate'/uuid.uuid4().hex
out.mkdir(parents=True)
receipt=sources()
files=[ROOT/'Supervisor/Tests/InputGateChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputGate.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs']
command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)]
print(out,flush=True)
result=subprocess.run(command,capture_output=True,text=True)
(out/'compile.log').write_text(result.stdout+result.stderr,encoding='utf-8')
print(result.stdout+result.stderr)
result.check_returncode()
result=subprocess.run([str(out/'Checks.exe')],capture_output=True,text=True,timeout=10)
(out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8')
print(result.stdout+result.stderr)
result.check_returncode()
assert sources()==receipt,'input gate source drift'
(out/'report.json').write_text(json.dumps({'status':'partial-input-gate-only','sources':receipt},indent=2))
