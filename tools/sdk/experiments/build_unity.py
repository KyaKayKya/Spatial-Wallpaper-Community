"""Build a fresh independent Unity/URP/Cilbox attempt from the repository source lock."""
from pathlib import Path
import argparse,shutil,json,os,subprocess,hashlib,sys,uuid
ROOT=Path(__file__).resolve().parents[3]
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from evidence import require_source_bytes,require_build_result
UPSTREAM=Path('C:/Project/GameAI/Spatial Wallpaper/.local-assets/research/sdk-research/Basis-reuse-kit-acfca6b')
OUT=ROOT/'Validation/Content/sdk-runtime/A/unity'
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--source',type=Path,default=UPSTREAM);args=parser.parse_args()
 OUT.mkdir(parents=True,exist_ok=True)
 pointer=OUT/'current-build.json';pointer.unlink(missing_ok=True)
 lock=json.loads((ROOT/'tools/sdk/cilbox-source.lock.json').read_text(encoding='utf-8'))
 assert lock['commit']=='acfca6b1c129c9ad5786e8b693e7ad2da2cd73a0'
 records=lock['files'];expected={row['path']:row['sha256'] for row in records}
 actual={name:(args.source/'upstream'/name).read_bytes() for name in expected}
 require_source_bytes(expected,actual)
 run_id=uuid.uuid4().hex;attempt=OUT/'attempts'/run_id;attempt.mkdir(parents=True)
 # Unity 2022 package/Burst tooling still encounters Windows MAX_PATH.
 project=ROOT/'Validation/Sdk'/run_id[:12]
 for part in ['Assets/Editor','Assets/Cilbox','Assets/Author','Packages','ProjectSettings']:(project/part).mkdir(parents=True,exist_ok=True)
 for name,data in actual.items():(project/'Assets/Cilbox'/Path(name).name).write_bytes(data)
 (project/'Assets/Author/StageAAuthor.asmdef').write_text(json.dumps({'name':'StageAAuthor','references':['Cilbox']}),encoding='utf-8')
 for name,target in [('StageAAuthor.cs','Assets/Author/StageAAuthor.cs'),('StageABox.cs','Assets/StageABox.cs'),('StageABuild.cs','Assets/Editor/StageABuild.cs'),('StageAPlayer.cs','Assets/StageAPlayer.cs')]:shutil.copyfile(Path(__file__).with_name(name),project/target)
 manifest={'dependencies':{'com.unity.render-pipelines.universal':'14.0.12','com.unity.ugui':'1.0.0','com.unity.modules.physics':'1.0.0','com.unity.modules.imageconversion':'1.0.0'}}
 (project/'Packages/manifest.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
 shutil.copyfile(ROOT/'Prototype/ProjectSettings/ProjectVersion.txt',project/'ProjectSettings/ProjectVersion.txt')
 def inputs():
  return {p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (ROOT/'tools/sdk').rglob('*') if p.is_file() and p.suffix in ['.py','.cs','.cpp','.json']}
 sources=inputs()
 env=os.environ.copy();env['SDK_STAGE_A_OUTPUT']=str(attempt);env['SDK_STAGE_A_BUILD_ID']=run_id;env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
 command=['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageABuild.Run','-logFile',str(attempt/'build.log')]
 print('Building fresh independent Unity/URP/Cilbox attempt '+run_id,flush=True)
 code=subprocess.call(command,env=env)
 if code:raise RuntimeError('Unity build failed; local build.log retained; exit='+str(code))
 require_build_result(json.loads((attempt/'build-result.json').read_text()),run_id)
 if inputs()!=sources:raise RuntimeError('source inputs changed while compiling')
 player=attempt/'player'
 if not (player/'StageA.exe').is_file():raise FileNotFoundError('fresh Player absent')
 original=player/'StageA_Data/Managed/StageAAuthor.dll'
 if not original.is_file():raise FileNotFoundError('fresh author input assembly absent')
 removed={'run_id':run_id,'name':original.name,'sha256':hashlib.sha256(original.read_bytes()).hexdigest()}
 original.unlink()
 (attempt/'removed-author-assembly.json').write_text(json.dumps(removed),encoding='utf-8')
 receipt={'run_id':run_id,'basis_commit':lock['commit'],'source_lock_sha256':hashlib.sha256((ROOT/'tools/sdk/cilbox-source.lock.json').read_bytes()).hexdigest(),'files':records,'experiment_sources':sources,'generated_project':project.relative_to(ROOT).as_posix()}
 (attempt/'source-receipt.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8')
 pointer.write_text(json.dumps({'run_id':run_id}),encoding='utf-8')
 print(attempt/'source-receipt.json',flush=True)
if __name__=='__main__':main()
