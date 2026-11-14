from pathlib import Path
import subprocess,sys,uuid,json,hashlib
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk/experiments'));sys.path.insert(0,str(ROOT/'tools/sdk'))
from native_build import compile_probe
from provenance import sources
receipt=sources();OUT=ROOT/'Validation/Content/sdk-runtime/D/profile-boundary-enforced'/uuid.uuid4().hex;OUT.mkdir(parents=True)
worker=OUT/'worker';worker.mkdir();(worker/'.sdk-worker-owned').write_text('SDK generated profile restriction fixture')
compile_probe(ROOT/'Supervisor/Tests/ProfileBoundaryProbe.cpp',worker/'Probe.exe')
DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-out:'+str(OUT/'Checks.exe'),str(ROOT/'Supervisor/Tests/ProfileBoundaryChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),*map(str,(ROOT/'Supervisor/Windows').glob('*.cs'))]
subprocess.run(cmd,check=True,capture_output=True,text=True)
subprocess.run([str(OUT/'Checks.exe'),str(worker),str(OUT/'result.json')],check=True,timeout=20)
result=json.loads((OUT/'result.json').read_text(encoding='utf-8-sig'))
assert result['load_userenv']==0 and result['registry_location_hr']==0,result
for name in ['profile_file_write','private_registry_write_open','private_registry_write','private_registry_create_open','private_registry_create','private_registry_acl']:assert result[name]==5,(name,result)
assert sources()==receipt,'sources changed during verification'
report={'status':'profile-write-boundary-only','sources':receipt,'probe_sha256':hashlib.sha256((worker/'Probe.exe').read_bytes()).hexdigest(),'result':result}
(OUT/'report.json').write_text(json.dumps(report,indent=2));print(json.dumps(result,indent=2));print(OUT)
