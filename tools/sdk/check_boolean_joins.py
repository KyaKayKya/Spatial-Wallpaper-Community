"""Real C# conversion, strict admission and VM boolean joins; no desktop input."""
import os,subprocess,uuid,json,hashlib
from build_execution import ROOT,UNITY,create
from provenance import sources
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/E/boolean-joins'/run;out.mkdir(parents=True)
project=ROOT/'Validation/Sdk'/('b'+run[:10]);create(project,[('StageBooleanJoinAuthor.cs',False),('StageBooleanJoinChecks.cs',True)])
env=os.environ.copy();env.update(SDK_BOOL_OUTPUT=str(out),SDK_BOOL_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
receipt=sources();print(out,flush=True);report={'status':'failed','sources':receipt}
try:
 subprocess.run([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageBooleanJoinChecks.Run','-logFile',str(out/'conversion.log')],env=env,timeout=900,check=True)
 result=json.loads((out/'result.json').read_text(encoding='utf-8-sig'));assert result['run_id']==run and len(result['cases'])==8
 assert sources()==receipt,'boolean join source drift'
 report.update(status='partial-real-boolean-join-regression',result=result,packages={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.glob('*.spatial')})
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
