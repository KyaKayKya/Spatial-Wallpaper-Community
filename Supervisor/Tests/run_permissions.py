"""Direct OS permissions under the production LPAC/Job launcher, with unrestricted controls."""
from pathlib import Path
import subprocess,sys,os,socket,json,uuid,shutil,hashlib
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data');sys.path.insert(0,str(ROOT/'tools/sdk'));sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from provenance import sources
from native_build import compile_probe
from evidence import require_production_permissions
from windows_sandbox import u
receipt=sources();out=ROOT/'Validation/Content/sdk-runtime/D/permissions'/uuid.uuid4().hex;out.mkdir(parents=True);worker=out/'worker';worker.mkdir();(worker/'.sdk-worker-owned').write_text('owned OS permission fixture')
compile_probe(ROOT/'tools/sdk/experiments/PermissionProbe.cpp',worker/'Probe.exe');sentinel=out/'private.txt';sentinel.write_text('SDK synthetic private canary')
listener=socket.socket();listener.bind(('127.0.0.1',0));listener.listen(8);port=str(listener.getsockname()[1]);os.environ['SPATIAL_TEST_PARENT_SECRET']='synthetic SDK canary';u.keybd_event(0x87,0,0,0)
try:
 subprocess.run([str(worker/'Probe.exe'),str(out/'control.json'),str(sentinel),port,str(os.getpid())],check=True,timeout=10)
 subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-out:'+str(out/'Checks.exe'),str(ROOT/'Supervisor/Tests/PermissionBoundaryChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),*map(str,(ROOT/'Supervisor/Windows').glob('*.cs'))],check=True)
 subprocess.run([str(out/'Checks.exe'),str(worker),str(sentinel),port,str(os.getpid()),str(out/'restricted.json')],check=True,timeout=20)
 control=json.loads((out/'control.json').read_text(encoding='utf-8-sig'));restricted=json.loads((out/'restricted.json').read_text(encoding='utf-8-sig'))
 require_production_permissions(control,restricted)
 assert sources()==receipt,'OS permission verifier source changed'
 report={'status':'production-launcher-direct-OS-permissions','sources':receipt,'probe_sha256':hashlib.sha256((worker/'Probe.exe').read_bytes()).hexdigest(),'control':control,'restricted':restricted}
 (out/'report.json').write_text(json.dumps(report,indent=2));print(json.dumps(restricted,indent=2));print(out)
finally:u.keybd_event(0x87,0,2,0);os.environ.pop('SPATIAL_TEST_PARENT_SECRET',None);listener.close()
