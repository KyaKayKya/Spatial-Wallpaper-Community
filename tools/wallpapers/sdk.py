"""Build successive SDK migrations in a single isolated reusable author project."""
from pathlib import Path
import argparse,hashlib,json,os,shutil,sys,uuid,subprocess
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
sys.path.insert(0,str(ROOT/'Validation/Dependencies/python'))
from build_execution import create,UNITY
from toolchain_process import run_toolchain
from audio import prepare

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('scene',choices=['Train','Showcase','Parking','RainyCafe']);p.add_argument('action',choices=['build','preview','geometry','budget','play','mesh']);p.add_argument('--runtime',type=Path);p.add_argument('--sdk-tgz',type=Path);a=p.parse_args()
 out=ROOT/'Validation/Content/sdk-runtime/G/scenes'/a.scene/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
 project=ROOT/'Validation/Sdk/v-scenes';create(project,[]);shutil.copytree(ROOT/'Prototype/Assets/WallpaperSdkScenes',project/'Assets/WallpaperSdkScenes',dirs_exist_ok=True);prepare(project,a.scene,folder='WallpaperSdkScenes/'+a.scene)
 common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent
 source=project/'Assets/WallpaperSdkScenes/Source'/a.scene;source.mkdir(parents=True,exist_ok=True)
 if a.scene=='Train':
  original=common/'Prototype/Assets/Train';low=common/'.local-assets/sdk-scene-20261110/Train/Reduced'
  manifest=json.loads((low/'geometry-source.json').read_text());
  for item in manifest:
   model=low/(item['name']+'.fbx');assert hashlib.sha256(model.read_bytes()).hexdigest()==item['output_sha256'];shutil.copyfile(model,source/model.name)
  shutil.copyfile(low/'geometry-source.json',source/'geometry-source.json')
  for name in ['Architecture','Furnishings','Details','Lamps','SnowyPark']:shutil.copyfile(original/'Generated'/(name+'.png'),source/(name+'.png'))
  shutil.copyfile(original/'Textures/SnowTrees.png',source/'SnowTrees.png')
  shaders=project/'Assets/WallpaperSdkScenes/Train/Shaders';shaders.mkdir(parents=True,exist_ok=True);text=(ROOT/'Prototype/Assets/Train/Shaders/WinterJourney.shader').read_text(encoding='utf-8')
  for i in range(6):(shaders/('WinterJourney'+str(i)+'.shader')).write_text(text.replace('"Train/WinterJourney"','"TrainSdk/WinterJourney'+str(i)+'"').replace('"Queue"="Transparent"','"Queue"="Transparent+'+str(i)+'"').replace('float t=_JourneyTime;','float t=_Time.y;'),encoding='utf-8')
 if a.scene=='Showcase':
  import showcase;showcase.prepare(source,common,ROOT)
 if a.scene=='Parking':
  import parking;parking.prepare(source,common,ROOT)
 if a.scene=='RainyCafe':
  import rainycafe;rainycafe.prepare(source,common,ROOT)
 if a.sdk_tgz:
  tarball=a.sdk_tgz.resolve();installed_name=tarball.stem+'-'+hashlib.sha256(tarball.read_bytes()).hexdigest()[:12]+'.tgz';shutil.copyfile(tarball,project/'Packages'/installed_name);manifest=json.loads((project/'Packages/manifest.json').read_text());manifest['dependencies']['com.spatialwallpaper.sdk']='file:'+installed_name;(project/'Packages/manifest.json').write_text(json.dumps(manifest,indent=2))
 env=os.environ.copy();env.update(SDK_SCENE=a.scene,SDK_SCENE_OUTPUT=str(out));env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
 if a.runtime:env['SDK_SCENE_RUNTIME']=str(a.runtime.resolve())
 method='WallpaperSceneBuilder.Build';report={'status':'failed','project':str(project),'scene':a.scene,'action':a.action,'process':{}}
 if a.action!='build':shutil.copyfile(Path(__file__).with_name('SceneChecks.cs'),project/'Assets/Editor/SceneChecks.cs');method='SceneChecks.'+a.action.title()
 if a.action=='play':shutil.copyfile(Path(__file__).with_name('PlaySoundChecks.cs'),project/'Assets/Editor/PlaySoundChecks.cs');method='PlaySoundChecks.Run'
 if a.action in ('preview','play'):
  from experiments.native_build import compile_probe
  compile_probe(Path(__file__).with_name('process_sound_capture.cpp'),out/'Capture.exe');env['SDK_SCENE_CAPTURE']=str(out/'Capture.exe')
 try:
  with (out/'toolchain.log').open('w') as log:run_toolchain([UNITY,'-batchmode','-projectPath',str(project),'-executeMethod',method,'-logFile',str(out/'unity.log')],ROOT,log,900,report['process'],env=env)
  report['result']=json.loads((out/'result.json').read_text(encoding='utf-8-sig'));report['status']='passed-development-'+a.action+'-not-RC'
 finally:(out/'report.json').write_text(json.dumps(report,indent=2))
if __name__=='__main__':main()
