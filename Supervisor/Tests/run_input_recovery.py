"""Controlled lifetime checks; native acceptance is a separate required gate."""
from pathlib import Path
import sys,subprocess,uuid,json
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references
from provenance import sources
out=ROOT/'Validation/Content/sdk-runtime/E/input-recovery'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();print(out,flush=True)
report={'status':'not-complete','sources':receipt,'exit_codes':{}}
try:
 files=[ROOT/'Supervisor/Tests/InputRecoveryChecks.cs',ROOT/'Supervisor/Interactive/InputRecovery.cs']
 commands=[('compile',[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)]),('result',[str(out/'Checks.exe')])]
 for name,command in commands:
  result=subprocess.run(command,capture_output=True,text=True,timeout=30);log=result.stdout+result.stderr;(out/(name+'.log')).write_text(log,encoding='utf-8');print(log);report['exit_codes'][name]=result.returncode;result.check_returncode()
 assert sources()==receipt
 report['status']='partial-controlled-input-recovery'
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
