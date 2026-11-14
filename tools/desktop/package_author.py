"""Refresh the existing clean author delivery with the current SDK and scenes."""
from pathlib import Path, PurePosixPath
import argparse, copy, hashlib, io, json, subprocess, tarfile, zipfile
ROOT=Path(__file__).resolve().parents[2]
def sha(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def package(previous, output):
 output.mkdir(parents=True,exist_ok=True)
 name='com.spatialwallpaper.sdk-0.3.0-preview.3.tgz';sdk=ROOT/'Packages/com.spatialwallpaper.sdk'
 assert not (output/name).exists(),'use a fresh output'
 current={p.relative_to(sdk).as_posix():p for p in sdk.rglob('*') if p.is_file() and '.git' not in p.parts}
 written=set();extras=[]
 with tarfile.open(previous/name,'r:gz') as old,tarfile.open(output/name,'w:gz') as new:
  for item in old.getmembers():
   relative=PurePosixPath(item.name).relative_to('package').as_posix()
   if not item.isfile():new.addfile(item);continue
   if relative in current:content=current[relative].read_bytes();written.add(relative)
   else:
    assert relative.startswith(('Editor/Windows~/','Documentation~/ThirdPartyNotices/','ThirdPartyNotices','LICENSE','ThirdParty/')),relative
    content=old.extractfile(item).read();extras.append(relative)
   item=copy.copy(item);item.size=len(content);new.addfile(item,io.BytesIO(content))
  for relative in sorted(set(current)-written):
   content=current[relative].read_bytes();item=tarfile.TarInfo('package/'+relative);item.size=len(content);item.mode=0o644;new.addfile(item,io.BytesIO(content))
 tarball=output/name;embedded='com.spatialwallpaper.sdk-0.3.0-preview.3-'+sha(tarball)[:12]+'.tgz'
 scenes=ROOT/'Validation/Sdk/v-scenes/Assets/WallpaperSdkScenes'
 assert (scenes/'Source/RainyCafe/WindowProps.smesh').is_file()
 # Failed experiments remain evidence, not author assets.
 def keep(path):
  n=path.name.removesuffix('.meta')
  return not ('VertexLighting' in n or n in {'WindowProps.fbx','PlantPots.fbx','FurniturePots.fbx'})
 overlays={'AuthorProject/Assets/WallpaperSdkScenes/'+p.relative_to(scenes).as_posix():p for p in scenes.rglob('*') if p.is_file() and keep(p)}
 with zipfile.ZipFile(previous/'WallpaperSdk-AuthorProject.zip') as old,zipfile.ZipFile(output/'WallpaperSdk-AuthorProject.zip','w',compression=zipfile.ZIP_DEFLATED) as new:
  for member in old.infolist():
   n=member.filename;assert '..' not in PurePosixPath(n).parts and not n.startswith('/')
   assert not set(PurePosixPath(n).parts)&{'Library','Temp','obj','Logs'}
   if n.startswith('AuthorProject/Assets/WallpaperSdkScenes/'):continue
   if n.startswith('AuthorProject/Packages/com.spatialwallpaper.sdk-') and n.endswith('.tgz'):continue
   content=old.read(member)
   if n=='AuthorProject/Packages/manifest.json':
    manifest=json.loads(content);manifest['dependencies']['com.spatialwallpaper.sdk']='file:'+embedded;content=json.dumps(manifest,indent=2).encode()
   new.writestr(member,content)
  new.writestr('AuthorProject/Packages/'+embedded,tarball.read_bytes())
  for n,p in sorted(overlays.items()):new.write(p,n)
 metadata={'sdk_sha256':sha(tarball),'author_sha256':sha(output/'WallpaperSdk-AuthorProject.zip'),'retained_generated_tools':extras,'sdk_source_files':{n:sha(p) for n,p in current.items()},'scene_files':{n:sha(p) for n,p in overlays.items()}}
 (output/'author-receipt.json').write_text(json.dumps(metadata,indent=2))
 return metadata
if __name__=='__main__':
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
 common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent
 package(common/'.local-assets/releases/WallpaperSdk-20261110-preview3',a.output.resolve());print(a.output)
