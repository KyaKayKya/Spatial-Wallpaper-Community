"""Classify known owned ListView geometry using the actual helper implementation."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib,platform
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk'));sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from provenance import sources
from native_build import compile_probe
out=ROOT/'Validation/Content/sdk-runtime/E/hit-classification'/uuid.uuid4().hex
out.mkdir(parents=True);receipt=sources();print(out,flush=True)
report={'status':'incomplete','sources':receipt,'system':platform.platform(),'runs':{}}
try:
 compile_probe(ROOT/'Supervisor/Tests/DesktopHitClassificationChecks.cpp',out/'Checks.exe')
 for name,args in [('positive',[]),('negative',['--negative-icon-as-blank'])]:
  result=subprocess.run([str(out/'Checks.exe'),*args],capture_output=True,text=True,timeout=15)
  log=result.stdout+result.stderr;(out/(name+'.log')).write_text(log,encoding='utf-8');print(log)
  report['runs'][name]={'exit_code':result.returncode,'log_sha256':hashlib.sha256(log.encode()).hexdigest()}
  if name=='positive':result.check_returncode()
  else:assert result.returncode==1 and 'FAIL icon expected=2 actual=1' in log,'negative classification must be detected'
 assert sources()==receipt,'source drift'
 report['status']='partial-owned-listview-classification';report['binary_sha256']=hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest()
finally:(out/'report.json').write_text(json.dumps(report,indent=2))
