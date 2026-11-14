"""Owned loopback mock service; no external provider or model validation."""
import subprocess,uuid,json,hashlib,shutil
from provenance import sources
from check_contracts import ROOT,DATA
from windows_framework import references
out=ROOT/'Validation/Content/sdk-runtime/F/deterministic-llm'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True);report={'status':'failed','sources':sources(),'scope':'owned local fixed HTTP response, byte parsing, authentication and stop; not external service or model quality'}
try:
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config');files=[ROOT/'tools/sdk/DeterministicLlmChecks.cs',*list((ROOT/'Supervisor/Preview').glob('DeterministicLlmServer.cs'))];command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]
 for name,argv in [('compile',command),('result',[str(out/'Checks.exe'),str(out)])]:
  done=subprocess.run(argv,capture_output=True,text=True,timeout=20);(out/(name+'.log')).write_text(done.stdout+done.stderr,encoding='utf-8');print(done.stdout+done.stderr);report[name+'_exit']=done.returncode;done.check_returncode()
 assert report['sources']==sources();report.update(status='passed-controlled-deterministic-loopback-service',result=json.loads((out/'result.json').read_text()))
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
