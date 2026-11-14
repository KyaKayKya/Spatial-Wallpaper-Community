"""Build the shared production Worker. No test mode, author code, or trusted sample geometry."""
from pathlib import Path
import argparse,subprocess,os,json,uuid,hashlib,shutil
from build_execution import ROOT,UNITY,create
from provenance import sources
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--reuse-project',type=Path);args=parser.parse_args()
 run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/D/worker'/run;out.mkdir(parents=True)
 project=ROOT/'Validation/Sdk'/('p'+run[:10]);files=[('SdkWorkerBuild.cs',True),('StageCPipeline.cs',True)]
 if args.reuse_project:
  project=args.reuse_project.resolve()
  if not project.is_relative_to((ROOT/'Validation/Sdk').resolve()) or not project.name.startswith('p') or {p.name for p in (project/'Assets').rglob('*.cs')}!={p[0] for p in files}:raise ValueError('not an owned production Worker build project')
 from release_identity import identity
 version=identity();version.pop('package_sha256')
 create(project,files);receipt=sources();env=os.environ.copy();env.update(SDK_WORKER_OUTPUT=str(out),SDK_WORKER_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
 from toolchain_process import run_toolchain
 process={}
 with (out/'toolchain.log').open('w') as log:run_toolchain([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','SdkWorkerBuild.Run','-logFile',str(out/'build.log')],ROOT,log,600,process,env=env)
 value=json.loads((out/'worker-build.json').read_text(encoding='utf-8-sig'));assert value['run_id']==run and value['result']=='Succeeded' and value['errors']==0
 for debug in (out/'player').iterdir():
  if debug.is_dir() and debug.name.endswith('_DoNotShip'):
   target=out/debug.name
   if not debug.resolve().is_relative_to(out.resolve()) or not target.resolve().is_relative_to(out.resolve()):raise ValueError('Worker debug move outside owned output')
   shutil.move(str(debug),str(target))
 from assembly_inventory import inspect
 inventory=inspect(out/'player/Worker_Data/Managed',worker='SdkWorker');assert inventory['SpatialWallpaper.Sdk.dll']['identity']==version,'Worker actual assembly identity mismatch';assert sources()==receipt,'Worker sources changed during build'
 dependencies={}
 for name in ['Packages/manifest.json','Packages/packages-lock.json','ProjectSettings/ProjectVersion.txt']:
  source=project/name
  if not source.is_file():raise ValueError('Worker resolved dependency input missing: '+name)
  target=out/'dependencies'/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,target);dependencies[name]=hashlib.sha256(target.read_bytes()).hexdigest()
 report={'identity':version,'dependency_files':dependencies,'build_process':process,'run_id':run,'sources':receipt,'worker_types':inventory,'runtime_files':{p.relative_to(out/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (out/'player').rglob('*') if p.is_file()},'build':value,'project':project.relative_to(ROOT).as_posix(),'status':'production-Worker-built-not-acceptance'}
 (out/'report.json').write_text(json.dumps(report,indent=2));print(out)
if __name__=='__main__':main()
