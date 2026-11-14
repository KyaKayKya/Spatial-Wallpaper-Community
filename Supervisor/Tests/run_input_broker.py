"""Trusted input publisher isolation, not native input acceptance."""
from pathlib import Path
import sys,subprocess,uuid,json
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references
from provenance import sources
out=ROOT/'Validation/Content/sdk-runtime/E/input-broker'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();print(out,flush=True)
files=[ROOT/p for p in ['Supervisor/Tests/InputBrokerChecks.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputBroker.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputGate.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/WindowsMachineClock.cs','Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs']]
command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)]
for name,args in [('compile',command),('result',[str(out/'Checks.exe')])]:
 result=subprocess.run(args,capture_output=True,text=True,timeout=20);(out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);result.check_returncode()
assert sources()==receipt,'input broker source drift'
(out/'report.json').write_text(json.dumps({'status':'partial-trusted-input-publisher','sources':receipt},indent=2))
