"""Re-export the existing LLM author for the native preview; no repeated Play gate."""
import os,uuid,json,hashlib
from build_execution import ROOT,UNITY,create
from provenance import sources
from toolchain_process import run_toolchain
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/F/preview-llm-author'/run;out.mkdir(parents=True);print(out,flush=True);project=ROOT/'Validation/Sdk'/('q'+run[:10]);create(project,[('StageELlmAuthor.cs',False),('StageELlmExport.cs',True)]);receipt=sources();env=os.environ.copy();env.update(SDK_E_OUTPUT=str(out),SDK_E_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));report={'status':'failed','sources':receipt,'scope':'fresh export of existing eight-request/cancel/store author for new preview wiring; not repeated full Play or RC'}
try:
 report['toolchain']={}
 with (out/'toolchain.log').open('w',encoding='utf-8') as log:run_toolchain([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageELlmExport.Run','-logFile',str(out/'conversion.log')],ROOT,log,600,report['toolchain'],env=env)
 author=json.loads((out/'author.json').read_text(encoding='utf-8-sig'));assert author['run_id']==run;assert receipt==sources();report.update(status='passed-preview-LLM-author-export',author=author,package_sha256=hashlib.sha256((out/'scene.spatial').read_bytes()).hexdigest())
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out,flush=True)
