"""Related controlled Runtime cleanup contract; not actual OS cleanup failure."""
from pathlib import Path
import argparse,subprocess,uuid,shutil,json,hashlib
from check_contracts import ROOT,DATA
from provenance import sources
from windows_framework import references
p=argparse.ArgumentParser();p.add_argument('--case',choices=['worker-dispose','ordinary-rejection','attempt-constructor','attempt-constructor-cleanup-failure'],required=True);args=p.parse_args();out=ROOT/'Validation/Content/sdk-runtime/F/runtime-cleanup-contract'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True);report={'status':'failed','case':args.case,'sources':sources(),'scope':__doc__}
try:
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
 files=[ROOT/'Supervisor/Tests/RuntimeCleanupChecks.cs',ROOT/'Supervisor/Hosting/OwnedAttempt.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs']+list((ROOT/'Supervisor/Core').glob('*.cs'))
 with (out/'compile.log').open('w') as log:subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-define:SDK_SUPERVISOR_TESTS',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],stdout=log,stderr=subprocess.STDOUT,check=True)
 r=subprocess.run([str(out/'Checks.exe'),str(out/'journal'),args.case],capture_output=True,text=True,timeout=30);(out/'checks.log').write_text(r.stdout+r.stderr);report['exit_code']=r.returncode;r.check_returncode();assert sources()==report['sources'];report['status']='passed-development-controlled-runtime-cleanup'
finally:
 report['sources_unchanged']=sources()==report['sources'];(out/'report.json').write_text(json.dumps(report,indent=2));print(out)
