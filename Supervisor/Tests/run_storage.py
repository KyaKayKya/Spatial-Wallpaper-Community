from pathlib import Path
import subprocess,uuid,shutil,json,sys,hashlib,time
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
sys.path.insert(0,str(ROOT/'tools/sdk'));from provenance import sources
from windows_framework import references,identity
sys.path.insert(0,str(ROOT/'tools/sdk/experiments'));from native_build import compile_probe
out=ROOT/'Validation/Content/sdk-runtime/E/storage'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources()
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-define:SDK_SUPERVISOR_TESTS','-r:'+str(out/'Newtonsoft.Json.dll'),*references(),'-out:'+str(out/'Checks.exe'),str(ROOT/'Supervisor/Tests/StorageChecks.cs'),str(ROOT/'Supervisor/Core/FaultJournal.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),*map(str,(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities').glob('*.cs')),*map(str,(ROOT/'Supervisor/Broker').glob('*.cs'))]
cmd += [str(ROOT/'Supervisor/Windows'/name) for name in ['Native.cs','RestrictedProcess.cs','TrustedAudioProcess.cs']]
cmd += [str(ROOT/'Supervisor/Tests/StorageAtomicChecks.cs')]
subprocess.run(cmd,check=True);subprocess.run([str(out/'Checks.exe'),str(out/'state')],check=True,timeout=90)
for phase,recovery in [('crash','recover'),('crash-after','recover-after')]:
 child=subprocess.Popen([str(out/'Checks.exe'),str(out/'state'),phase],stdout=subprocess.DEVNULL,stderr=subprocess.DEVNULL)
 try:
  deadline=time.monotonic()+10
  while not (out/'state'/('ready-to-'+phase)).exists():
   if child.poll() is not None or time.monotonic()>deadline:raise RuntimeError('interruption fixture did not reach '+phase)
   time.sleep(.01)
  child.terminate();child.wait(timeout=5)
 finally:
  if child.poll() is None:child.terminate();child.wait(timeout=5)
 subprocess.run([str(out/'Checks.exe'),str(out/'state'),recovery],check=True,timeout=15)
probe=ROOT/'Validation/Sdk'/('k'+out.name[:10]);probe.mkdir(parents=True);(probe/'.sdk-worker-owned').write_text('SDK private storage boundary fixture');compile_probe(ROOT/'Supervisor/Tests/TokenBoundaryProbe.cpp',probe/'Probe.exe')
boundary=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Boundary.exe'),str(ROOT/'Supervisor/Tests/StorageBoundaryChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),*map(str,(ROOT/'Supervisor/Windows').glob('*.cs'))]
subprocess.run(boundary,check=True);subprocess.run([str(out/'Boundary.exe'),str(probe),str(out/'state/storage/live-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa/state.bin'),str(out/'boundary.json')],check=True,timeout=15)
assert sources()==receipt,'storage source drift'
(out/'report.json').write_text(json.dumps({'status':'E1-storage-core-only','framework_references':identity(),'native_storage_boundary':json.loads((out/'boundary.json').read_text(encoding='utf-8-sig')),'sources':receipt,'binary_sha256':hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest()},indent=2));print(out)
