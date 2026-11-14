"""Fresh input author compilation/export/admission; not native-input acceptance."""
import os,subprocess,uuid,json,hashlib,argparse
from build_execution import ROOT,UNITY,create
from provenance import sources
from toolchain_process import run_toolchain
parser=argparse.ArgumentParser();parser.add_argument('--runtime-fixture',action='store_true');parser.add_argument('--continuity-fixture',action='store_true');args=parser.parse_args();assert not(args.continuity_fixture and args.runtime_fixture)
run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/E/input-author'/run;out.mkdir(parents=True)
project=ROOT/'Validation/Sdk'/('i'+run[:10]);create(project,[('StageEInputAuthor.cs',False),('StageEInputRuntimeAuthor.cs',False),('StageEInputContinuityAuthor.cs',False),('StageEInputExport.cs',True)])
receipt=sources();env=os.environ.copy();env.update(SDK_E_OUTPUT=str(out),SDK_E_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));print(out,flush=True)
env['SDK_E_INPUT_RUNTIME']='1' if args.runtime_fixture else '0'
env['SDK_E_INPUT_CONTINUITY']='1' if args.continuity_fixture else '0'
report={'status':'failed','sources':receipt}
try:
 report['toolchain']={}
 with (out/'toolchain.log').open('w',encoding='utf-8') as log:
  run_toolchain([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageEInputExport.Run','-logFile',str(out/'conversion.log')],ROOT,log,900,report['toolchain'],env=env)
 author=json.loads((out/'author.json').read_text(encoding='utf-8-sig'));assert author['run_id']==run
 assert sources()==receipt,'input author source drift'
 report.update(status='partial-input-CSharp-export-only',author=author,package_sha256=hashlib.sha256((out/'scene.spatial').read_bytes()).hexdigest())
finally:
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out,flush=True)
