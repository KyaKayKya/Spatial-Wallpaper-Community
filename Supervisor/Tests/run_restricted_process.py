from pathlib import Path
import subprocess,uuid,sys
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from native_build import compile_probe
DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
OUT=ROOT/'Validation/Content/sdk-runtime/D/restricted-csharp'/uuid.uuid4().hex;OUT.mkdir(parents=True);worker=OUT/'worker';worker.mkdir()
compile_probe(ROOT/'Supervisor/Tests/ProcessProbe.cpp',worker/'ProcessProbe.exe')
(worker/'.sdk-worker-owned').write_text('SDK generated test runtime copy')
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-out:'+str(OUT/'Checks.exe'),str(ROOT/'Supervisor/Tests/RestrictedProcessChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),*map(str,(ROOT/'Supervisor/Windows').glob('*.cs'))]
r=subprocess.run(cmd,capture_output=True,text=True);print(r.stdout+r.stderr)
if r.returncode:raise SystemExit(r.returncode)
subprocess.run([str(OUT/'Checks.exe'),str(worker)],timeout=30,check=True)
external=OUT/'outside';external.mkdir();canary=external/'canary.txt';canary.write_text('SDK synthetic ACL canary')
import os
os.link(canary,worker/'hardlink.txt')
try:subprocess.run([str(OUT/'Checks.exe'),str(worker),str(canary),'runtime-hardlink'],timeout=30,check=True)
finally:os.unlink(worker/'hardlink.txt')
junction=worker/'junction'
subprocess.run(['cmd.exe','/c','mklink','/J',str(junction),str(external)],check=True,capture_output=True)
try:subprocess.run([str(OUT/'Checks.exe'),str(worker),str(canary),'runtime-reparse-point'],timeout=30,check=True)
finally:os.rmdir(junction)

