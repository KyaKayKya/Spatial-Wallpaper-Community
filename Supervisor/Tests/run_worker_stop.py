"""Background LPAC stop-lane/process test; never starts Unity or desktop input."""
from pathlib import Path
import sys,subprocess,uuid,shutil,json,hashlib
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references
from provenance import sources
out=ROOT/'Validation/Content/sdk-runtime/E/worker-stop'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources()
peer=out/'peer';peer.mkdir();shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',peer/'Peer.exe.config');shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
compiler=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references()]
hosting=ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'
assert sys.argv[1:] in [[],['--negative-identity']]
negative=bool(sys.argv[1:]);reader=hosting/'WindowsWorkerStopReader.cs'
if negative:
 text=reader.read_text(encoding='utf-8-sig');needle='WorkerStopCodec.Validate(bytes,identity);';assert text.count(needle)==1
 reader=out/'WindowsWorkerStopReader.cs';reader.write_text(text.replace(needle,''),encoding='utf-8')
subprocess.run([*compiler,'-out:'+str(peer/'Peer.exe'),str(ROOT/'Supervisor/Tests/WorkerStopPeer.cs'),str(hosting/'WorkerFrameCodec.cs'),str(reader)],check=True)
files=[ROOT/'Supervisor/Tests/WorkerStopChecks.cs',hosting/'WorkerFrameCodec.cs',hosting/'WorkerExitCodes.cs',ROOT/'Supervisor/Hosting/WorkerStopChannel.cs',*list((ROOT/'Supervisor/Core').glob('*.cs')),*list((ROOT/'Supervisor/Windows').glob('*.cs'))]
subprocess.run([*compiler,'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
r=subprocess.run([str(out/'Checks.exe'),str(peer),str(out)],capture_output=True,text=True,timeout=60)
(out/'result.log').write_text(r.stdout+r.stderr,encoding='utf-8');print(r.stdout+r.stderr);print(out)
if negative:
 assert r.returncode==1 and 'System.Exception: error-exit-not-graceful-wrong' in r.stderr and 'PASS all-stop-resources-closed-wrong' in r.stdout,'wrong negative or missing cleanup'
else:r.check_returncode()
assert sources()==receipt,'source drift'
observations=None if negative else json.loads((out/'results.json').read_text())
if observations is not None:
 assert len(observations)==6 and {row['mode'] for row in observations}=={'normal','hang','nonzero','wrong','wrong-instance','wrong-generation'},'stop-case-closure'
 observations=[dict(row,case=row['mode']) for row in observations]
(out/'report.json').write_text(json.dumps({'status':'expected-invalid-stop-identity-rejected' if negative else 'passed-worker-stop-control','scope':'owned-LPAC-process-stop-not-Unity','expected_negative':negative,'sources':receipt,'results':observations,'binaries':{str(p.relative_to(out)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [out/'Checks.exe',peer/'Peer.exe']}},indent=2))
