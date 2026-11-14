"""Build an explicitly diagnostic Worker, never a release candidate or acceptance substitute."""
from pathlib import Path
import os,sys,json,uuid,hashlib,subprocess,shutil
from build_execution import ROOT,UNITY,create
from provenance import sources
run=uuid.uuid4().hex
out=ROOT/'Validation/Content/sdk-runtime/E/input-diagnostic-worker'/run
out.mkdir(parents=True);print(out,flush=True)
project=ROOT/'Validation/Sdk'/('diag'+run[:10])
create(project,[('SdkWorkerBuild.cs',True),('StageCPipeline.cs',True),('InputDiagnosticObserver.cs',False)])
builder=project/'Assets/Editor/SdkWorkerBuild.cs'
text=builder.read_text();needle='new GameObject("SDK Worker").AddComponent<WorkerRuntime>();'
assert text.count(needle)==1
builder.write_text(text.replace(needle,needle+'new GameObject("Experimental input observer").AddComponent<InputDiagnosticObserver>();'))
receipt=sources();env=os.environ.copy();env.update(SDK_WORKER_OUTPUT=str(out),SDK_WORKER_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
report={'run_id':run,'status':'diagnostic-build-failed','release_candidate':False,'sources':receipt,'project':str(project.relative_to(ROOT))}
try:
 subprocess.run([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','SdkWorkerBuild.Run','-logFile',str(out/'build.log')],env=env,timeout=900,check=True)
 value=json.loads((out/'worker-build.json').read_text(encoding='utf-8-sig'));assert value['run_id']==run and value['result']=='Succeeded' and value['errors']==0
 for debug in (out/'player').iterdir():
  if debug.is_dir() and debug.name.endswith('_DoNotShip'):shutil.move(str(debug),str(out/debug.name))
 assert sources()==receipt,'diagnostic build source drift'
 report.update(status='diagnostic-only-not-production-acceptance',build=value,runtime_files={p.relative_to(out/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (out/'player').rglob('*') if p.is_file()},project_sources={str(p.relative_to(project)):hashlib.sha256(p.read_bytes()).hexdigest() for p in (project/'Assets').rglob('*.cs')})
finally:(out/'report.json').write_text(json.dumps(report,indent=2))
