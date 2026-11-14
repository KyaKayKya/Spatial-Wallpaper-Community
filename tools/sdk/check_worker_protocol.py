from pathlib import Path
import subprocess,uuid
from check_contracts import DATA,ROOT,OUT as CONTRACT_OUT,compile_checks
import shutil
if compile_checks():raise SystemExit(1)
OUT=ROOT/'Validation/Content/sdk-runtime/D/protocol'/uuid.uuid4().hex;OUT.mkdir(parents=True)
sources=list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting').glob('*.cs'))+list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Resources').glob('*.cs'))
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(CONTRACT_OUT/'ContractChecks.exe'),'-out:'+str(OUT/'Checks.exe'),str(ROOT/'tools/sdk/WorkerFrameChecks.cs'),str(ROOT/'tools/sdk/FrameMailboxChecks.cs'),*map(str,sources)]
r=subprocess.run(cmd,capture_output=True,text=True);print(r.stdout+r.stderr)
if r.returncode:raise SystemExit(r.returncode)
shutil.copyfile(CONTRACT_OUT/'ContractChecks.exe',OUT/'ContractChecks.exe')
raise SystemExit(subprocess.call([str(OUT/'Checks.exe')],timeout=30))
