"""Actual Editor API -> immutable package -> trusted native preview and stop."""
from pathlib import Path
import argparse,os,uuid,json,hashlib
from build_execution import ROOT,UNITY,create
from provenance import sources
from toolchain_process import run_toolchain
parser=argparse.ArgumentParser();parser.add_argument('--runtime',type=Path,required=True);parser.add_argument('--case',choices=['normal','recovery','reload','restart'],default='normal');parser.add_argument('--reuse-project',type=Path);args=parser.parse_args();run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/F/editor-preview'/run;out.mkdir(parents=True);print(out,flush=True)
project=args.reuse_project.resolve() if args.reuse_project else ROOT/'Validation/Sdk'/('v'+run[:10])
if args.reuse_project:assert project.parent==(ROOT/'Validation/Sdk').resolve() and (project/'Assets/Editor').is_dir()
create(project,[('StageFEditorPreviewChecks.cs',True)]);receipt=sources();env=os.environ.copy();env.update(SDK_F_OUTPUT=str(out),SDK_F_RUN=run,SDK_F_EDITOR_CASE=args.case,SDK_F_RUNTIME=str(args.runtime.resolve()),SDK_F_ATTEMPTS=str(ROOT/'Validation/Sdk'/('z'+run[:10])));env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));report={'status':'failed','case':args.case,'sources':receipt,'clean_project':not bool(args.reuse_project),'project':str(project),'scope':'actual Editor API, native image, Worker and immutable export, not interactive UI/reload or RC'}
try:
 report['toolchain']={}
 with (out/'toolchain.log').open('w',encoding='utf-8') as log:run_toolchain([UNITY,'-batchmode','-projectPath',str(project),'-executeMethod','StageFEditorPreviewChecks.Run','-logFile',str(out/'unity.log')],ROOT,log,600,report['toolchain'],env=env)
 result=json.loads((out/'result.json').read_text(encoding='utf-8-sig'));assert result['run_id']==run;assert receipt==sources();report.update(status='passed-selected-editor-preview',result=result,package_sha256=hashlib.sha256((out/'first.spatial').read_bytes()).hexdigest())
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out,flush=True)
