from pathlib import Path
import subprocess,uuid,json,hashlib
from provenance import sources
from check_contracts import ROOT,DATA
from windows_framework import references
import shutil
out=ROOT/'Validation/Content/sdk-runtime/E/capability-client'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources()
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
files=[ROOT/'tools/sdk/CapabilityMailboxChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Resources/ResourceLedger.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Resources/ManagedReservation.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Resources/AllocationFrame.cs',*list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities').rglob('*.cs'))]
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
subprocess.run([str(out/'Checks.exe')],check=True,timeout=15);assert sources()==receipt,'capability client source drift'
(out/'report.json').write_text(json.dumps({'status':'E2-client-mailbox-core-only','sources':receipt,'binary_sha256':hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest()},indent=2));print(out)
