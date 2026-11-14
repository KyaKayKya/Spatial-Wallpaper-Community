from pathlib import Path
import subprocess,uuid,sys,shutil,json,platform,hashlib
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from native_build import compile_probe
sys.path.insert(0,str(ROOT/'tools/sdk'))
from provenance import sources
receipt=sources()
DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
OUT=ROOT/'Validation/Content/sdk-runtime/D/process-supervision'/uuid.uuid4().hex;OUT.mkdir(parents=True);source=OUT/'source';source.mkdir()
compile_probe(ROOT/'Supervisor/Tests/ProcessProbe.cpp',source/'ProcessProbe.exe')
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',OUT/'Newtonsoft.Json.dll')
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(OUT/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(OUT/'Checks.exe'),str(ROOT/'Supervisor/Tests/ProcessSupervisionChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),*map(str,(ROOT/'Supervisor/Windows').glob('*.cs')),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs'),*map(str,(ROOT/'Supervisor/Core').glob('*.cs'))]
r=subprocess.run(cmd,capture_output=True,text=True);print(r.stdout+r.stderr)
if r.returncode:raise SystemExit(r.returncode)
r=subprocess.run([str(OUT/'Checks.exe'),str(source),str(OUT)],timeout=30)
if r.returncode:raise SystemExit(r.returncode)
assert sources()==receipt,'source changed during process supervision probe'
report={'status':'native-process-fixture-only','sources':receipt,'platform':platform.platform(),'results':json.loads((OUT/'results.json').read_text()),'binaries':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in [OUT/'Checks.exe',source/'ProcessProbe.exe']}}
(OUT/'report.json').write_text(json.dumps(report,indent=2))
(ROOT/'Validation/Content/sdk-runtime/reports/stage-D-process-partial.json').write_text(json.dumps(report,indent=2))
print(OUT)
