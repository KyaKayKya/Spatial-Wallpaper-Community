"""Build and validate the SDK MiniShotGame in one reusable, isolated author project."""
from pathlib import Path
import argparse,hashlib,json,os,shutil,sys,uuid
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk'))
from build_execution import UNITY,create
from toolchain_process import run_toolchain

def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('action',choices=['picking','build','vm','geometry','preview','play-view']);parser.add_argument('--runtime',type=Path);parser.add_argument('--sdk-tgz',type=Path);args=parser.parse_args()
 out=ROOT/'Validation/Content/sdk-runtime/G/minishot'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
 project=ROOT/'Validation/Sdk/v-minishot';create(project,[])
 source=ROOT/'Prototype/Assets/MiniShotGameSdk'
 if source.is_dir():shutil.copytree(source,project/'Assets/MiniShotGameSdk',dirs_exist_ok=True)
 editor=project/'Assets/Editor';editor.mkdir(exist_ok=True)
 shutil.copyfile(ROOT/'tools/minishot/MiniShotGameChecks.cs',editor/'MiniShotGameChecks.cs')
 if args.action=='vm':shutil.copyfile(ROOT/'tools/minishot/MiniShotVmChecks.cs',editor/'MiniShotVmChecks.cs')
 if args.action=='geometry':shutil.copyfile(ROOT/'tools/minishot/MiniShotGeometryChecks.cs',editor/'MiniShotGeometryChecks.cs')
 if args.action=='preview':shutil.copyfile(ROOT/'tools/minishot/MiniShotSdkPreview.cs',editor/'MiniShotSdkPreview.cs')
 if args.action=='play-view':shutil.copyfile(ROOT/'tools/minishot/MiniShotPlayViewChecks.cs',editor/'MiniShotPlayViewChecks.cs')
 if args.sdk_tgz:
  tarball=args.sdk_tgz.resolve();installed_name=tarball.stem+'-'+hashlib.sha256(tarball.read_bytes()).hexdigest()[:12]+'.tgz';destination=project/'Packages'/installed_name;shutil.copyfile(tarball,destination)
  manifest=json.loads((project/'Packages/manifest.json').read_text());manifest['dependencies']['com.spatialwallpaper.sdk']='file:'+installed_name;(project/'Packages/manifest.json').write_text(json.dumps(manifest,indent=2))
 env=os.environ.copy();env.update(SDK_MINISHOT_OUTPUT=str(out));env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
 method='MiniShotGameChecks.Run'
 report={'status':'failed','project':str(project),'scope':args.action,'process':{}}
 if args.action!='picking':
  import importlib.util
  audio_spec=importlib.util.spec_from_file_location('wallpaper_audio',ROOT/'tools/wallpapers/audio.py');audio_module=importlib.util.module_from_spec(audio_spec);audio_spec.loader.exec_module(audio_module);audio_module.prepare(project,'MiniShotGame',folder='MiniShotGameSdk')
  import subprocess
  common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent
  exported=common/'Prototype/Assets/MiniShotGame/Generated';manifest=json.loads((exported/'manifest.json').read_text())
  target=project/'Assets/MiniShotGameSdk/Source';target.mkdir(parents=True,exist_ok=True)
  for item in manifest['objects']:
   assert Path(item['file']).name==item['file'];p=exported/item['file'];assert hashlib.sha256(p.read_bytes()).hexdigest()==item['sha256'];shutil.copyfile(p,target/item['file'])
  shutil.copyfile(exported/'manifest.json',target/'manifest.json')
  shaders=project/'Assets/MiniShotGameSdk/Shaders';shaders.mkdir(parents=True,exist_ok=True)
  for name in ['FilteredGrid.shader','VertexEmission.shader']:shutil.copyfile(ROOT/'Prototype/Assets/MiniShotGame/Shaders'/name,shaders/name)
  method={'build':'MiniShotSdkBuilder.Build','vm':'MiniShotVmChecks.Run','geometry':'MiniShotGeometryChecks.Run','preview':'MiniShotSdkPreview.Run','play-view':'MiniShotPlayViewChecks.Run'}[args.action]
  if args.runtime:env['SDK_MINISHOT_RUNTIME']=str(args.runtime.resolve())
 inputs=[*source.rglob('*.cs'),*source.rglob('*.shader'),*Path(__file__).parent.glob('*.cs'),Path(__file__)]
 report['author_sources']={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs}
 if args.sdk_tgz:report['installed_tarball']={'path':str(tarball),'sha256':hashlib.sha256(tarball.read_bytes()).hexdigest()}
 try:
  with (out/'toolchain.log').open('w') as log:run_toolchain([UNITY,'-batchmode','-projectPath',str(project),'-executeMethod',method,'-logFile',str(out/'unity.log')],ROOT,log,900,report['process'],env=env)
  result='picking.json' if args.action=='picking' else 'result.json';report['result']=json.loads((out/result).read_text(encoding='utf-8-sig'));report['status']='passed-development-'+args.action+'-not-RC'
  if args.sdk_tgz:
   resolved=json.loads((project/'Packages/packages-lock.json').read_text())['dependencies']['com.spatialwallpaper.sdk'];assert resolved['source']=='local-tarball','SDK was not resolved from the supplied tarball';report['resolved_sdk']=resolved
   for name in ['manifest.json','packages-lock.json']:shutil.copyfile(project/'Packages'/name,out/name)
  assert report['author_sources']=={p.relative_to(ROOT).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in inputs},'author source changed during validation'
 finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')

if __name__=='__main__':main()
