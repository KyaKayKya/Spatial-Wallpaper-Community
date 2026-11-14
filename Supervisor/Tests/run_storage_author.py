"""Run a converted C# author through production admission, LPAC Worker and storage broker."""
from pathlib import Path
import argparse,sys,subprocess,uuid,shutil,json,hashlib,platform
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from provenance import sources
from windows_framework import references,identity
parser=argparse.ArgumentParser();parser.add_argument('--worker',type=Path,required=True);parser.add_argument('--author',type=Path,required=True);args=parser.parse_args()
out=ROOT/'Validation/Content/sdk-runtime/E/storage-worker'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();worker=args.worker.resolve();author=args.author.resolve()
worker_report=json.loads((worker/'report.json').read_text());author_report=json.loads((author/'report.json').read_text())
assert {p.relative_to(worker/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (worker/'player').rglob('*') if p.is_file()}==worker_report['runtime_files']
assert hashlib.sha256((author/'scene.spatial').read_bytes()).hexdigest()==author_report['package_sha256']
subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_validator.py'),'--output',str(out/'validator')],check=True)
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
files=[ROOT/'Supervisor/Tests/StorageAuthorChecks.cs']
for folder in ['Supervisor/Core','Supervisor/Windows','Supervisor/Hosting','Supervisor/Broker','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities']:files+=list((ROOT/folder).glob('*.cs'))
files += [ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'/name for name in ['WorkerFrameCodec.cs','RuntimeLimits.Generated.cs','WorkerExitCodes.cs']]
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
attempts=ROOT/'Validation/Sdk'/('k'+out.name[:10]);result=subprocess.run([str(out/'Checks.exe'),str(out),str(worker),str(author),str(out/'validator'),str(attempts)],capture_output=True,text=True,timeout=60)
(out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(out,flush=True);print(result.stdout+result.stderr);result.check_returncode()
assert sources()==receipt,'storage author source drift'
(out/'report.json').write_text(json.dumps({'status':'E-CSharp-Worker-storage-only','sources':receipt,'framework':identity(),'platform':platform.platform(),'worker_build_sha256':hashlib.sha256((worker/'report.json').read_bytes()).hexdigest(),'author':author_report,'checks_sha256':hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest(),'validator':json.loads((out/'validator/build.json').read_text()),'results':json.loads((out/'results.json').read_text())},indent=2))
