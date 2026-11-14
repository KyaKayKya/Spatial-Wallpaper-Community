"""Run fixed-size input protocol and Worker frame-state checks."""
import subprocess,uuid,json
from check_contracts import ROOT,DATA
from windows_framework import references
from provenance import sources
out=ROOT/'Validation/Content/sdk-runtime/E/input-snapshot'/uuid.uuid4().hex
out.mkdir(parents=True);receipt=sources();print(out,flush=True)
files=[ROOT/'tools/sdk/InputSnapshotChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs']
command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)]
for name,args in [('compile',command),('result',[str(out/'Checks.exe')])]:
 result=subprocess.run(args,capture_output=True,text=True,timeout=30)
 (out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8')
 print(result.stdout+result.stderr);result.check_returncode()
assert receipt==sources(),'input snapshot source drift'
(out/'report.json').write_text(json.dumps({'status':'partial-input-snapshot-only','sources':receipt},indent=2))
