"""Exercise the production LPAC Worker with converted C# and a real local LLM fixture."""
from pathlib import Path
import argparse,sys,subprocess,uuid,shutil,json,hashlib,zipfile
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from provenance import sources
from windows_framework import references,identity
from llm_fixture import LlmFixture
parser=argparse.ArgumentParser();parser.add_argument('--worker',type=Path,required=True);parser.add_argument('--author',type=Path,required=True);args=parser.parse_args()
out=ROOT/'Validation/Content/sdk-runtime/E/llm-worker'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();worker=args.worker.resolve();author=args.author.resolve();print(out,flush=True)
worker_report=json.loads((worker/'report.json').read_text());author_report=json.loads((author/'report.json').read_text())
assert {p.relative_to(worker/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (worker/'player').rglob('*') if p.is_file()}==worker_report['runtime_files']
assert hashlib.sha256((author/'scene.spatial').read_bytes()).hexdigest()==author_report['package_sha256']
with zipfile.ZipFile(author/'scene.spatial') as package:
 assert all(b'fixture-only-key' not in package.read(name) for name in package.namelist()),'test credential entered author package'
subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_validator.py'),'--output',str(out/'validator')],check=True);shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
files=[ROOT/'Supervisor/Tests/LlmAuthorChecks.cs']
for folder in ['Supervisor/Core','Supervisor/Windows','Supervisor/Hosting','Supervisor/Broker','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities']:files+=list((ROOT/folder).glob('*.cs'))
files += [ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'/name for name in ['WorkerFrameCodec.cs','RuntimeLimits.Generated.cs','WorkerExitCodes.cs']]
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
fixture=LlmFixture()
try:
 (out/'llm.json').write_text(json.dumps(fixture.config));attempts=ROOT/'Validation/Sdk'/('n'+out.name[:10]);result=subprocess.run([str(out/'Checks.exe'),str(out),str(worker),str(author),str(out/'validator'),str(attempts),str(out/'llm.json')],capture_output=True,text=True,timeout=60)
 (out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);result.check_returncode()
 assert fixture.requests and all(r['test_auth_matches'] and r['body']['model']=='fixture-model' for r in fixture.requests)
 assert 'fixture-only-key' not in (out/'worker.log').read_text(encoding='utf-8',errors='replace')
finally:fixture.close()
assert sources()==receipt,'LLM author source drift'
(out/'report.json').write_text(json.dumps({'status':'partial-LLM-CSharp-LPAC-only','sources':receipt,'framework':identity(),'worker_build_sha256':hashlib.sha256((worker/'report.json').read_bytes()).hexdigest(),'author':author_report,'checks_sha256':hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest(),'validator':json.loads((out/'validator/build.json').read_text()),'results':json.loads((out/'results.json').read_text()),'http_requests':fixture.requests},indent=2))
