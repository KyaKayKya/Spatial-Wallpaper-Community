"""Direct OS probes with ordinary-process positive controls and strict verdicts."""
from pathlib import Path
import subprocess,socket,json,os,platform,hashlib,uuid,sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from evidence import require_permissions
from windows_sandbox import launch,u
from native_build import compile_probe
ROOT=Path(__file__).resolve().parents[3]
OUT=ROOT/'Validation/Content/sdk-runtime/A'/uuid.uuid4().hex
OUT.mkdir(parents=True);runtime=OUT/'runtime';runtime.mkdir()
exe=runtime/'PermissionProbe.exe'
compile_probe(Path(__file__).with_name('PermissionProbe.cpp'),exe)
sentinel=OUT/'user-private.txt';sentinel.write_text('SDK permission sentinel')
listener=socket.socket();listener.bind(('127.0.0.1',0));listener.listen(8);port=listener.getsockname()[1]
report={'platform':platform.platform(),'commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'probe_sha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'cases':{}}
os.environ['SPATIAL_TEST_PARENT_SECRET']='non-secret-canary'
u.keybd_event(0x87,0,0,0)
try:
 control=OUT/'control.json'
 subprocess.run([str(exe),str(control),str(sentinel),str(port),str(os.getpid())],check=True,timeout=10)
 report['cases']['full-trust']=json.loads(control.read_text(encoding='utf-8-sig'))
 for isolated in (False,True):
  result=runtime/'output'/('private.json' if isolated else 'interactive.json')
  code,sid=launch(exe,[result,sentinel,port,os.getpid()],runtime,isolated)
  key='private-desktop' if isolated else 'interactive-desktop'
  report['cases'][key]={'exit_code':code}
  if result.exists():report['cases'][key].update(json.loads(result.read_text(encoding='utf-8-sig')))
 require_permissions(report['cases'])
 report['status']='passed-observed-permissions-only'
except BaseException as error:
 report['status']='failed';report['error']=repr(error);raise
finally:
 u.keybd_event(0x87,0,2,0);os.environ.pop('SPATIAL_TEST_PARENT_SECRET',None);listener.close()
 path=OUT/'permissions.json';path.write_text(json.dumps(report,indent=2),encoding='utf-8')
 print(json.dumps(report,indent=2));print(path)
