from pathlib import Path
import subprocess,sys,json,uuid,time,os,shutil
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
sys.path.insert(0,str(ROOT/'tools/sdk'));sys.path.insert(0,str(ROOT/'Supervisor/Tests'))
from run_production import packages
from fixtures import sample,archive,ins
import zipfile,io
from provenance import sources
from endpoint_wait import wait_endpoint,read_endpoint
import hashlib,platform
sys.path.insert(0,str(ROOT/'tools/sdk/experiments'));from native_build import compile_probe
import argparse
p=argparse.ArgumentParser();p.add_argument('--worker',type=Path,required=True);args=p.parse_args();worker=args.worker.resolve()
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/D/host'/run;out.mkdir(parents=True);ids=packages(out);receipt=sources()
(out/'invalid.spatial').write_bytes(b'invalid SDK archive')
p,c=sample();p['classes'][0]['methods'][0]['body']=[ins('ldc.i4',8192),ins('ldc.i4',8192),ins('call','SpatialWallpaper.Sdk::Resources::CreateTexture(System.Int32,System.Int32)->Texture'),ins('pop'),ins('ret')]
data=archive(p,c);(out/'resource.spatial').write_bytes(data)
with zipfile.ZipFile(io.BytesIO(data)) as z:resource_id=json.loads(z.read('manifest.json'))['build_id']
(out/'diagnostic-cases.json').write_text(json.dumps([{'file':'invalid.spatial','build':ids[0],'reason':'Admission','reported':'admission-rejected'},{'file':'bad.spatial','build':ids[1],'reason':'Budget','reported':'execution-budget'},{'file':'resource.spatial','build':resource_id,'reason':'Budget','reported':'resource-budget'},{'file':'healthy.spatial','build':ids[0],'reason':'ProcessExit','reported':'unknown'}]))
subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_supervisor.py'),'--output',str(out/'backend')],check=True)
subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_validator.py'),'--output',str(out/'validator')],check=True)
shutil.copyfile(out/'backend/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(out/'Client.exe'),str(ROOT/'Supervisor/Tests/HostChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),*map(str,(ROOT/'Supervisor/Windows').glob('*.cs'))],check=True)
probe=ROOT/'Validation/Sdk'/('t'+run[:10]);probe.mkdir();(probe/'.sdk-worker-owned').write_text('owned native management boundary probe');compile_probe(ROOT/'Supervisor/Tests/TokenBoundaryProbe.cpp',probe/'Probe.exe')
state=out/'state';attempts=ROOT/'Validation/Sdk'/('h'+run[:10]);config=out/'config.json';config.write_text(json.dumps({'image':str(worker/'player'),'inventory':str(worker/'report.json'),'validator':str(out/'validator'),'state':str(state),'attempts':str(attempts),'width':640,'height':360}))
with (out/'host.stdout').open('w') as log:
 host=subprocess.Popen([str(out/'backend/Supervisor.exe'),str(config)],stdout=log,stderr=subprocess.STDOUT)
 try:
  deadline=time.monotonic()+15
  while not (state/'endpoint.json').exists():
   if host.poll() is not None or time.monotonic()>deadline:raise RuntimeError('Supervisor failed to publish endpoint: '+str(out))
   time.sleep(.02)
  subprocess.run([str(out/'Client.exe'),str(state/'endpoint.json'),str(out/'healthy.spatial'),ids[0],str(out/'client-result.json'),str(probe)],check=True,timeout=90)
  assert host.wait(timeout=10)==0
  durable=json.loads((state/'journal/state.json').read_text(encoding='utf-8-sig'))
  for case in json.loads((out/'diagnostic-results.json').read_text()):
   entry=next(e for e in durable['history'] if e['attempt']==case['attempt'] and e['diagnostic'])
   assert entry['event']==case['reason'] and json.loads(entry['diagnostic'])['worker_reported_reason']==case['reported']
  previous=read_endpoint(state/'endpoint.json')['auth'] if (state/'endpoint.json').exists() else None
  host=subprocess.Popen([str(out/'backend/Supervisor.exe'),str(config)],stdout=log,stderr=subprocess.STDOUT)
  wait_endpoint(lambda:read_endpoint(state/'endpoint.json'),lambda:host.poll() is None,previous,15)
  subprocess.run([str(out/'Client.exe'),str(state/'endpoint.json'),str(out/'diagnostic-results.json')],check=True,timeout=15)
  assert host.wait(timeout=10)==0
 finally:
  if host.poll() is None:host.terminate();host.wait(timeout=10)
assert sources()==receipt,'host verification source changed'
report={'status':'independent-Supervisor-management-observations','platform':platform.platform(),'sources':receipt,'worker_build_sha256':hashlib.sha256((worker/'report.json').read_bytes()).hexdigest(),'backend_build':json.loads((out/'backend/build.json').read_text()),'diagnostic_cases':json.loads((out/'diagnostic-results.json').read_text(encoding='utf-8-sig')),'diagnostics_survived_backend_restart':True,'result':json.loads((out/'client-result.json').read_text(encoding='utf-8-sig'))}
(out/'report.json').write_text(json.dumps(report,indent=2));print(out)
