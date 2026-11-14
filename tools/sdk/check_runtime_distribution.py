"""Canonical Windows runtime distribution file boundaries."""
from pathlib import Path
import subprocess,uuid,json,hashlib,shutil
from provenance import sources
from check_contracts import ROOT,DATA
from windows_framework import references
out=ROOT/'Validation/Content/sdk-runtime/F/runtime-distribution'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True);report={'status':'failed','sources':sources(),'scope':'synthetic inventory alias and boundary controls; not actual runtime binary provenance'}
try:
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config');files=[ROOT/'tools/sdk/RuntimeDistributionChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs',*[ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows'/name for name in ['TrustedRuntimeDistribution.cs','HostExitContract.cs']]]
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]
 for name,argv in [('compile',command),('result',[str(out/'Checks.exe'),str(out)])]:
  done=subprocess.run(argv,capture_output=True,text=True,timeout=20);(out/(name+'.log')).write_text(done.stdout+done.stderr,encoding='utf-8');print(done.stdout+done.stderr);report[name+'_exit']=done.returncode;done.check_returncode()
 assert report['sources']==sources();report.update(status='passed-canonical-runtime-distribution-boundaries',binary_sha256=hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest())
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
