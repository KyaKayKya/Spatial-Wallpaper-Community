"""Create a clean, portable five-scene author project from the verified local inputs."""
from pathlib import Path
import argparse,hashlib,json,os,shutil,sys,uuid,zipfile
ROOT=Path(__file__).resolve().parents[2];sys.path[:0]=[str(ROOT/'tools/sdk'),str(ROOT/'Validation/Dependencies/python')]
from build_execution import create,UNITY
from toolchain_process import run_toolchain

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--delivery',required=True,type=Path);p.add_argument('--output',required=True,type=Path);a=p.parse_args()
 destination=a.output.resolve();destination.mkdir(parents=True,exist_ok=False);out=ROOT/'Validation/Content/sdk-runtime/G/scenes'/('clean-author-'+uuid.uuid4().hex);out.mkdir();print(out,flush=True)
 create(destination,[])
 for project,folder in [('v-scenes','WallpaperSdkScenes'),('v-minishot','MiniShotGameSdk')]:shutil.copytree(ROOT/'Validation/Sdk'/project/'Assets'/folder,destination/'Assets'/folder)
 tar=a.delivery.resolve()/'com.spatialwallpaper.sdk-0.3.0-preview.3.tgz';name=tar.stem+'-'+hashlib.sha256(tar.read_bytes()).hexdigest()[:12]+'.tgz';shutil.copyfile(tar,destination/'Packages'/name)
 manifest=json.loads((destination/'Packages/manifest.json').read_text());manifest['dependencies']['com.spatialwallpaper.sdk']='file:'+name;(destination/'Packages/manifest.json').write_text(json.dumps(manifest,indent=2))
 shutil.copyfile(Path(__file__).with_name('CleanAuthorChecks.cs'),destination/'Assets/Editor/CleanAuthorChecks.cs');env=os.environ.copy();env['SDK_AUTHOR_OUTPUT']=str(out);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));receipt={}
 try:
  with (out/'toolchain.log').open('w') as log:run_toolchain([UNITY,'-batchmode','-projectPath',str(destination),'-executeMethod','CleanAuthorChecks.Run','-logFile',str(out/'unity.log')],ROOT,log,900,receipt,env=env)
 finally:(out/'process.json').write_text(json.dumps(receipt,indent=2))
 result=json.loads((out/'result.json').read_text(encoding='utf-8-sig'));keep=set(result['dependencies']);removed=[]
 # Generated assets are rebuildable; retain only scene and active pipeline dependencies.
 for parent in ['MiniShotGameSdk','WallpaperSdkScenes']:
  for path in (destination/'Assets'/parent/'Generated').rglob('*'):
   if path.is_file() and path.suffix!='.meta' and path.relative_to(destination).as_posix() not in keep:
    removed.append({'path':path.relative_to(destination).as_posix(),'bytes':path.stat().st_size});path.unlink();path.with_name(path.name+'.meta').unlink(missing_ok=True)
 # Details were deliberately omitted from Train's first SDK edition.
 for name in ['Details.fbx','Details.png']:
  path=destination/'Assets/WallpaperSdkScenes/Source/Train'/name
  if path.is_file():removed.append({'path':str(path.relative_to(destination)),'bytes':path.stat().st_size});path.unlink();path.with_name(path.name+'.meta').unlink(missing_ok=True)
 for path in [destination/'Assets/Editor/CleanAuthorChecks.cs',destination/'Assets/Editor/CleanAuthorChecks.cs.meta']:path.unlink(missing_ok=True)
 # Keep the report before removing this newly-created project's own cache.
 cache=destination/'Library';assert cache.resolve().is_relative_to(destination) and cache.parent==destination
 cache_bytes=sum(p.stat().st_size for p in cache.rglob('*') if p.is_file());shutil.rmtree(cache)
 for name in ['Logs','Temp','obj','UserSettings']:
  path=destination/name
  if path.is_dir():assert path.resolve().is_relative_to(destination);shutil.rmtree(path)
 (out/'cleanup.json').write_text(json.dumps({'new_project':str(destination),'cache_bytes':cache_bytes,'unused_generated':removed},indent=2))
 inventory={p.relative_to(destination).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in destination.rglob('*') if p.is_file()};(out/'author-files.json').write_text(json.dumps(inventory,indent=2));(out/'project.json').write_text(json.dumps({'project':str(destination),'sdk_tarball_sha256':hashlib.sha256(tar.read_bytes()).hexdigest(),'files':len(inventory),'scope':'portable local author project, original art redistribution unverified'},indent=2));print(destination,flush=True)
if __name__=='__main__':main()
