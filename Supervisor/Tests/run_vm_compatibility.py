"""Use preserved old and current production Validators to prove VM identity isolation."""
from pathlib import Path
import argparse,sys,subprocess,uuid,shutil,json,hashlib
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from provenance import sources
from windows_framework import references
p=argparse.ArgumentParser();p.add_argument('--old-validator',type=Path,required=True);p.add_argument('--old-author',type=Path,required=True);p.add_argument('--new-author',type=Path,required=True);args=p.parse_args()
out=ROOT/'Validation/Content/sdk-runtime/E/vm-compatibility'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();print(out,flush=True)
old=args.old_validator.resolve();old_author=args.old_author.resolve();new_author=args.new_author.resolve()
old_report=json.loads((old/'build.json').read_text())
assert all(hashlib.sha256((old/name).read_bytes()).hexdigest()==digest for name,digest in old_report['files'].items())
for author in [old_author,new_author]:assert hashlib.sha256((author/'scene.spatial').read_bytes()).hexdigest()==json.loads((author/'report.json').read_text())['package_sha256']
subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_validator.py'),'--output',str(out/'validator')],check=True)
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
files=[ROOT/'Supervisor/Tests/VmCompatibilityChecks.cs']
for folder in ['Supervisor/Core','Supervisor/Windows','Supervisor/Hosting','Supervisor/Broker','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities']:files+=list((ROOT/folder).glob('*.cs'))
files += [ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'/name for name in ['WorkerFrameCodec.cs','WorkerExitCodes.cs']]
old_limits=out/'OldRuntimeLimits.Generated.cs';old_limits.write_bytes(subprocess.check_output(['git','show','5e1114c:Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs'],cwd=ROOT))
results=[]
for driver,limits in enumerate([old_limits,ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs']):
 exe=out/f'Checks{driver}.exe'
 subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(exe),*map(str,files),str(limits)],check=True)
 r=subprocess.run([str(exe),str(old),str(out/'validator'),str(old_author/'scene.spatial'),str(new_author/'scene.spatial'),str(ROOT/'Validation/Sdk'/('m'+out.name[:10])),str(out/f'results{driver}.json'),str(driver)],capture_output=True,text=True,timeout=30)
 (out/f'result{driver}.log').write_text(r.stdout+r.stderr,encoding='utf-8');print(r.stdout+r.stderr);r.check_returncode();results+=json.loads((out/f'results{driver}.json').read_text())
assert sources()==receipt
(out/'report.json').write_text(json.dumps({'sources':receipt,'old_limits_sha256':hashlib.sha256(old_limits.read_bytes()).hexdigest(),'old_validator_sha256':hashlib.sha256((old/'build.json').read_bytes()).hexdigest(),'new_validator':json.loads((out/'validator/build.json').read_text()),'old_author_sha256':hashlib.sha256((old_author/'scene.spatial').read_bytes()).hexdigest(),'new_author_sha256':hashlib.sha256((new_author/'scene.spatial').read_bytes()).hexdigest(),'results':results},indent=2),encoding='utf-8')
