from pathlib import Path
import argparse,json,subprocess,os,uuid,shutil,hashlib
from build_execution import ROOT,UNITY,create
from provenance import sources
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--package',type=Path,required=True);parser.add_argument('--reuse-project',type=Path);args=parser.parse_args()
 run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/D/stream-worker'/run;out.mkdir(parents=True);project=ROOT/'Validation/Sdk'/('s'+run[:10])
 if args.reuse_project:
  project=args.reuse_project.resolve()
  if not project.is_relative_to((ROOT/'Validation/Sdk').resolve()) or not project.name.startswith('s'):raise ValueError('not an owned streaming fixture project')
  expected={'StageDWorker.cs','StageCVisuals.cs','StageCPipeline.cs','StageDWorkerBuild.cs'}
  if {p.name for p in (project/'Assets').rglob('*.cs')}!=expected:raise ValueError('unexpected project sources')
 create(project,[('StageDWorker.cs',False),('StageCVisuals.cs',False),('StageCPipeline.cs',True),('StageDWorkerBuild.cs',True)])
 shutil.copyfile(args.package,out/'scene.spatial');receipt=sources();(out/'build-source-receipt.json').write_text(json.dumps(receipt,indent=2))
 env=os.environ.copy();env.update(SDK_D_OUTPUT=str(out),SDK_D_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
 code=subprocess.call([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageDWorkerBuild.Run','-logFile',str(out/'build.log')],env=env,timeout=900)
 if code:raise RuntimeError('fresh streaming Worker build failed: '+str(out))
 value=json.loads((out/'worker-build.json').read_text(encoding='utf-8-sig'));assert value['run_id']==run and value['result']=='Succeeded' and value['errors']==0
 assert sources()==receipt,'source changed during Worker build'
 from assembly_inventory import inspect
 inventory=inspect(out/'player/StageD_Data/Managed',worker='StageDWorker')
 assert not any('Cilbox' in name or 'SdkCallbackNegative' in name for name in inventory)
 forbidden={'StageCAuthor','StageCPeer','StageCData','StageCSpawn','StageCBoundaryAuthor','StageCNullAuthor','StageCNullData'}
 assert not any(set(value['types'] or [])&forbidden for value in inventory.values())
 runtime_files={p.relative_to(out/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (out/'player').rglob('*') if p.is_file()}
 report={'run_id':run,'sources':receipt,'runtime_files':runtime_files,'worker_sources':{p.relative_to(project).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (project/'Assets').rglob('*.cs')},'worker_types':inventory,'package_sha256':hashlib.sha256((out/'scene.spatial').read_bytes()).hexdigest(),'status':'built-not-runtime-acceptance'}
 (out/'report.json').write_text(json.dumps(report,indent=2));print(out)
 return 0
if __name__=='__main__':raise SystemExit(main())
