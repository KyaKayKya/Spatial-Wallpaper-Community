from pathlib import Path
import sys,subprocess,uuid,shutil,json,hashlib
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data');sys.path.insert(0,str(ROOT/'tools/sdk'))
from provenance import sources
from windows_framework import references,identity
sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from native_build import compile_probe
out=ROOT/'Validation/Content/sdk-runtime/E/capability-pipe'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
shared=[ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs',*list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities').glob('*.cs'))]
compiler=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll')]
distribution=out/'distribution';distribution.mkdir();shutil.copyfile(out/'Newtonsoft.Json.dll',distribution/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',distribution/'Peer.exe.config')
subprocess.run([*compiler,'-out:'+str(distribution/'Peer.exe'),str(ROOT/'Supervisor/Tests/CapabilityPeer.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WindowsWorkerBoundary.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Resources/ResourceLedger.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Resources/ManagedReservation.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Resources/AllocationFrame.cs'),*map(str,(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/Client').glob('*.cs')),*map(str,shared)],check=True)
(distribution/'build.json').write_text(json.dumps({'files':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in distribution.iterdir() if p.is_file()}},indent=2))
files=[ROOT/'Supervisor/Tests/CapabilityPipeChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs',*shared]
for folder in ['Core','Windows','Hosting','Broker']:files+=list((ROOT/'Supervisor'/folder).glob('*.cs'))
subprocess.run([*compiler,'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
attempts=ROOT/'Validation/Sdk'/('j'+out.name[:10]);attempts.mkdir(parents=True)
compile_probe(ROOT/'Supervisor/Tests/AudioFaultHelper.cpp',out/'AudioFaultHelper.exe')
assert sys.argv[1:] in [[],['--negative-revoke']],'unknown fixture mode'
result=subprocess.run([str(out/'Checks.exe'),str(distribution),str(distribution/'build.json'),str(attempts),str(out/'state'),str(out/'results.json'),str(out/'AudioFaultHelper.exe'),*sys.argv[1:]],capture_output=True,text=True,timeout=45)
(out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr,flush=True);print(out,flush=True)
result.check_returncode()
assert sources()==receipt,'capability pipe source drift'
(out/'report.json').write_text(json.dumps({'status':'E2-real-LPAC-capability-pipe-fixture-only','sources':receipt,'framework_references':identity(),'client_sha256':hashlib.sha256((distribution/'Peer.exe').read_bytes()).hexdigest(),'host_sha256':hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest(),'results':json.loads((out/'results.json').read_text())},indent=2));print(out)
