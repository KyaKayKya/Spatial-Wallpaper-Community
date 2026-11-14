"""Real Supervisor queues/journal with controlled process sample boundaries."""
import subprocess,uuid,json,hashlib,shutil
from provenance import sources
from check_contracts import ROOT,DATA
from windows_framework import references
out=ROOT/'Validation/Content/sdk-runtime/F/supervised-session-owner'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
report={'status':'failed','sources':sources(),'scope':'production Supervisor lifecycle and journal; controlled Worker process boundaries; not LPAC or presentation'}
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
files=[ROOT/'tools/sdk/SupervisedSessionOwnerChecks.cs',ROOT/'tools/sdk/FrameTransportChecks.cs',ROOT/'tools/sdk/InspectionTransportChecks.cs',*list((ROOT/'Supervisor/Session').glob('*.cs')),*list((ROOT/'Supervisor/Core').glob('*.cs')),
 *[ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'/name for name in ('RuntimeSession.cs','RuntimePackageInspection.cs','WorkerFrameCodec.cs','WorkerExitCodes.cs')]]
files += [ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs', *[ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows'/name for name in ('SupervisorManagerConnection.cs','SupervisorSessionClient.cs')]]
files += [ROOT/'Supervisor/Host.cs']
files += [ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PinnedProcessOwner.cs']
for folder in ['Supervisor/Windows','Supervisor/Hosting','Supervisor/Broker','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities']:files += list((ROOT/folder).glob('*.cs'))
try:
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-main:SupervisedSessionOwnerChecks',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]
 for name,args in [('compile',command),('result',[str(out/'Checks.exe'),str(out/'journal')])]:
  result=subprocess.run(args,capture_output=True,text=True,timeout=30);(out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);report[name+'_exit']=result.returncode;result.check_returncode()
 assert sources()==report['sources'];report.update(status='passed-controlled-supervisor-session-only',binary_sha256=hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest())
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
