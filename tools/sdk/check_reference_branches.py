"""Real C# null/reference branch conversion, strict admission and ordinary/VM parity."""
import os,subprocess,uuid,json,hashlib
from build_execution import ROOT,UNITY,create
from provenance import sources
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/E/reference-branches'/run;out.mkdir(parents=True)
project=ROOT/'Validation/Sdk'/('r'+run[:10]);create(project,[('StageReferenceBranchAuthor.cs',False),('StageReferenceBranchChecks.cs',True)])
env=os.environ.copy();env.update(SDK_REF_OUTPUT=str(out),SDK_REF_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
receipt=sources();print(out,flush=True);report={'status':'failed','sources':receipt}
try:
 subprocess.run([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageReferenceBranchChecks.Run','-logFile',str(out/'conversion.log')],env=env,timeout=900,check=True,creationflags=subprocess.CREATE_NO_WINDOW)
 result=json.loads((out/'result.json').read_text(encoding='utf-8-sig'));assert result['run_id']==run and len(result['cases'])==3
 assert sources()==receipt,'reference branch source drift'
 report.update(status='real-reference-branch-regression',result=result,packages={p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in out.glob('*.spatial')})
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
