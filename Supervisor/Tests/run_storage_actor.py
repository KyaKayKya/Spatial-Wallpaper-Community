from pathlib import Path
import sys,subprocess,uuid,shutil,json,hashlib
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data');sys.path.insert(0,str(ROOT/'tools/sdk'))
from provenance import sources
from windows_framework import references,identity
out=ROOT/'Validation/Content/sdk-runtime/E/storage-actor'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
native=[str(ROOT/'Supervisor/Windows'/name) for name in ['Native.cs','RestrictedProcess.cs','TrustedAudioProcess.cs']]
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-define:SDK_SUPERVISOR_TESTS',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),str(ROOT/'Supervisor/Tests/StorageActorChecks.cs'),str(ROOT/'Supervisor/Core/FaultJournal.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),*native,*map(str,(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities').glob('*.cs')),*map(str,(ROOT/'Supervisor/Broker').glob('*.cs'))],check=True)
subprocess.run([str(out/'Checks.exe'),str(out/'state')],check=True,timeout=30);assert sources()==receipt,'actor source drift'
(out/'report.json').write_text(json.dumps({'status':'E2-storage-actor-core-only','sources':receipt,'framework_references':identity(),'binary_sha256':hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest()},indent=2));print(out)
