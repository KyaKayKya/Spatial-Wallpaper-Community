from pathlib import Path
import subprocess,uuid
from check_contracts import ROOT,DATA
from windows_framework import references
out=ROOT/'Validation/Content/sdk-runtime/E/capability-wire'/uuid.uuid4().hex;out.mkdir(parents=True)
sources=[ROOT/'tools/sdk/CapabilityWireChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs',*list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities').glob('*.cs'))]
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,sources)],check=True)
subprocess.run([str(out/'Checks.exe')],check=True,timeout=15);print(out)
