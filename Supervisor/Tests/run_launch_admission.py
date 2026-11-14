"""Prove missing admission services cannot create a renderer process."""
from pathlib import Path
import sys, subprocess, uuid, shutil, json, hashlib, platform
ROOT = Path(__file__).resolve().parents[2]
sys.path.insert(0, str(ROOT / 'tools/sdk'))
from check_contracts import DATA
from windows_framework import references
from run_production import packages
from provenance import sources
import argparse
parser=argparse.ArgumentParser();parser.add_argument('--worker',type=Path,required=True);args=parser.parse_args()
out = ROOT / 'Validation/Content/sdk-runtime/E/launch-admission' / uuid.uuid4().hex
out.mkdir(parents=True)
worker = args.worker.resolve()
receipt=sources()
worker_report=json.loads((worker/'report.json').read_text())
actual={p.relative_to(worker/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (worker/'player').rglob('*') if p.is_file()}
assert actual==worker_report['runtime_files'],'Worker image drift'
shutil.copyfile(DATA / 'Managed/Newtonsoft.Json.dll', out / 'Newtonsoft.Json.dll')
files = [ROOT / 'Supervisor/Tests/LaunchAdmissionChecks.cs']
for folder in ['Supervisor/Core', 'Supervisor/Windows', 'Supervisor/Hosting', 'Supervisor/Broker', 'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities']:
    files += list((ROOT / folder).glob('*.cs'))
for name in ['WorkerFrameCodec.cs', 'RuntimeLimits.Generated.cs', 'WorkerExitCodes.cs']:
    files.append(ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting' / name)
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'), str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'), '-nologo', '-langversion:latest', *references(), '-r:'+str(out/'Newtonsoft.Json.dll'), '-out:'+str(out/'Checks.exe'), *map(str, files)], check=True)
(out / 'bad.spatial').write_bytes(b'invalid archive')
attempts = ROOT / 'Validation/Sdk' / ('e'+out.name[:10])
ids=packages(out)
(out / 'invalid.spatial').write_bytes(b'invalid archive')
subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_validator.py'),'--output',str(out/'validator')],check=True)
result=subprocess.run([str(out/'Checks.exe'), str(worker/'player'), str(worker/'report.json'), str(attempts), str(out/'invalid.spatial'),str(out/'validator'),str(out/'healthy.spatial'),ids[0]], capture_output=True,text=True,timeout=45)
(out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr)
result.check_returncode()
assert sources()==receipt,'launch admission source drift'
(out/'report.json').write_text(json.dumps({'status':'E2-production-launch-transaction-only','sources':receipt,'platform':platform.platform(),'worker_build_sha256':hashlib.sha256((worker/'report.json').read_bytes()).hexdigest(),'validator':json.loads((out/'validator/build.json').read_text()),'checks_sha256':hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest(),'package_sha256':hashlib.sha256((out/'healthy.spatial').read_bytes()).hexdigest(),'build':ids[0],'result':result.stdout},indent=2),encoding='utf-8')
print(out)
