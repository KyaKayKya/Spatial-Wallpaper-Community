"""Fresh modern static resource export and exact unsupported-state rejections."""
import os,uuid,json,hashlib,argparse
from pathlib import Path
from build_execution import ROOT,UNITY,create
from provenance import sources
from toolchain_process import run_toolchain
parser=argparse.ArgumentParser();parser.add_argument('--reuse-project',type=Path);args=parser.parse_args()
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/F/static-author'/run;out.mkdir(parents=True);print(out,flush=True)
project=args.reuse_project.resolve() if args.reuse_project else ROOT/'Validation/Sdk'/('s'+run[:10])
if args.reuse_project:assert project.parent==(ROOT/'Validation/Sdk').resolve() and project.name.startswith('s') and (project/'Assets/Editor/StageFStaticExport.cs').is_file()
create(project,[('StageFStaticExport.cs',True),('StageFResourceAuthor.cs',False)]);receipt=sources();env=os.environ.copy();env.update(SDK_F_OUTPUT=str(out),SDK_F_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));report={'status':'failed','clean_project':not bool(args.reuse_project),'project':str(project),'sources':receipt,'scope':'modern static mesh/material/texture export/admission and unsupported state rejections, not rendered fidelity or RC'}
try:
 report['toolchain']={}
 with (out/'toolchain.log').open('w',encoding='utf-8') as log:run_toolchain([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageFStaticExport.Run','-logFile',str(out/'conversion.log')],ROOT,log,600,report['toolchain'],env=env)
 author=json.loads((out/'author.json').read_text(encoding='utf-8-sig'));assert author['run_id']==run;assert sources()==receipt
 report.update(status='passed-selected-static-export-rejections',author=author,package_sha256=hashlib.sha256((out/'scene.spatial').read_bytes()).hexdigest())
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out,flush=True)
