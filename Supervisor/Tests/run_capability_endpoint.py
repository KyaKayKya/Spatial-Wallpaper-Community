from pathlib import Path
import sys,subprocess,uuid,shutil,json,hashlib
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data');sys.path.insert(0,str(ROOT/'tools/sdk'))
from provenance import sources
from windows_framework import references
out=ROOT/'Validation/Content/sdk-runtime/E/capability-endpoint'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
files=[ROOT/'Supervisor/Tests/CapabilityEndpointChecks.cs',ROOT/'Supervisor/Core/FaultJournal.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs',*list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities').glob('*.cs')),*list((ROOT/'Supervisor/Broker').glob('*.cs'))]
files += [ROOT/'Supervisor/Windows'/name for name in ['Native.cs','RestrictedProcess.cs','TrustedAudioProcess.cs']]
assert sys.argv[1:] in [[],['--negative-close-race']]
negative=bool(sys.argv[1:])
if negative:
 source=ROOT/'Supervisor/Broker/CapabilityEndpoint.cs';text=source.read_text(encoding='utf-8-sig');needle='void Fault(){';commit='lock(gate){if(Closed)return;Interlocked.Exchange(ref fault,1);Interlocked.Exchange(ref closed,1);}Close();'
 assert text.count(needle)==1 and text.count(commit)==1
 copy=out/'CapabilityEndpoint.cs';copy.write_text(text.replace(needle,'void Fault(){bool wasClosed=Closed;').replace(commit,'if(wasClosed)return;Interlocked.Exchange(ref fault,1);Close();'),encoding='utf-8');files=[copy if p==source else p for p in files]
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-define:SDK_SUPERVISOR_TESTS',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
r=subprocess.run([str(out/'Checks.exe'),str(out/'state')],capture_output=True,text=True,timeout=30);log=r.stdout+r.stderr;(out/'result.log').write_text(log,encoding='utf-8');print(log)
if negative:assert r.returncode==1 and 'System.Exception: stop-fault-publication-race' in log,'wrong negative result'
else:r.check_returncode()
assert sources()==receipt,'endpoint source drift'
(out/'report.json').write_text(json.dumps({'status':'E2-endpoint-core-only','expected_negative':negative,'sources':receipt,'binary_sha256':hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest()},indent=2));print(out)
