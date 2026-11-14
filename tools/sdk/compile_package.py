from pathlib import Path
import subprocess,os,uuid,json,shutil
from fixtures import sample,archive,ins
from provenance import sources
from toolchain_process import run_toolchain
ROOT=Path(__file__).resolve().parents[2]
def main():
 source_hashes=sources()
 run=uuid.uuid4().hex;project=ROOT/'Validation/Sdk'/('u'+run[:10]);output=ROOT/'Validation/Content/sdk-runtime/B'/('unity-'+run)
 for d in ['Assets/Editor','Packages','ProjectSettings']:(project/d).mkdir(parents=True,exist_ok=True)
 output.mkdir(parents=True)
 dependencies={'com.unity.render-pipelines.universal':'14.0.12','com.unity.nuget.newtonsoft-json':'3.2.1','com.unity.modules.physics':'1.0.0','com.spatialwallpaper.sdk':'file:'+(ROOT/'Packages/com.spatialwallpaper.sdk').as_posix()}
 (project/'Packages/manifest.json').write_text(json.dumps({'dependencies':dependencies}),encoding='utf-8')
 shutil.copyfile(ROOT/'Prototype/ProjectSettings/ProjectVersion.txt',project/'ProjectSettings/ProjectVersion.txt')
 shutil.copyfile(ROOT/'tools/sdk/experiments/StageBChecks.cs',project/'Assets/Editor/StageBChecks.cs')
 shutil.copyfile(ROOT/'tools/sdk/experiments/ContractProbe.shader',project/'Assets/ContractProbe.shader')
 p,c=sample();(output/'positive.spatial').write_bytes(archive(p,c))
 p['classes'][0]['methods'][0]['body']=[ins('call','System.IO::File::ReadAllText'),ins('ret')];(output/'malicious.spatial').write_bytes(archive(p,c))
 env=os.environ.copy();env.update(SDK_B_OUTPUT=str(output),SDK_B_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
 cmd=['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageBChecks.Run','-logFile',str(output/'unity.log')]
 toolchain={}
 try:
  with (output/'toolchain.log').open('w',encoding='utf-8') as log:run_toolchain(cmd,ROOT,log,600,toolchain,env=env)
 finally:
  (output/'toolchain-process.json').write_text(json.dumps(toolchain,indent=2),encoding='utf-8');print(output,flush=True)
 data=json.loads((output/'unity-result.json').read_text(encoding='utf-8-sig'))
 if data['run_id']!=run or data['engine']!='2022.3.62f3' or not data['rejected']:raise RuntimeError('wrong Unity result')
 report=ROOT/'Validation/Content/sdk-runtime/reports/stage-B-unity.json'
 if sources()!=source_hashes:raise RuntimeError('source changed during Unity compile')
 data['builtin_shaders']=json.loads((output/'builtin-shaders.json').read_text(encoding='utf-8-sig'));
 if data['builtin_shaders']!=json.loads((ROOT/'tools/sdk/rules/v1.json').read_text())['builtin_shaders']:raise RuntimeError('builtin shader rules do not match pinned engine/pipeline')
 data['source_hashes']=source_hashes;data['fixture_bundle']=str(output/'bundles/probe.shaderbundle');data['toolchain']=toolchain;report.write_text(json.dumps(data,indent=2),encoding='utf-8');print(report)
 return 0
if __name__=='__main__':raise SystemExit(main())
