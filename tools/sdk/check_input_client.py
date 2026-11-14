"""Real anonymous-pipe input delivery through the shared author binding."""
import subprocess,uuid,json,hashlib,shutil
from provenance import sources
from check_contracts import ROOT,DATA
from windows_framework import references
out=ROOT/'Validation/Content/sdk-runtime/E/input-client'/uuid.uuid4().hex
out.mkdir(parents=True);receipt=sources();print(out,flush=True)
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
files=[ROOT/'tools/sdk/InputClientChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs',
 *[ROOT/('Packages/com.spatialwallpaper.sdk/Runtime/Resources/'+name+'.cs') for name in ['ResourceLedger','ManagedReservation','AllocationFrame']],
 *list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities').rglob('*.cs'))]
command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]
for name,args in [('compile',command),('result',[str(out/'Checks.exe')])]:
 result=subprocess.run(args,capture_output=True,text=True,timeout=20)
 (out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8')
 print(result.stdout+result.stderr);result.check_returncode()
assert sources()==receipt,'input client source drift'
(out/'report.json').write_text(json.dumps({'status':'partial-real-pipe-shared-input-client','sources':receipt,'binary_sha256':hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest()},indent=2))
