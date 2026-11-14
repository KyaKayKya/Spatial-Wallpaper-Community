"""Stage existing five SDK packages and a matching runtime for the main product."""
from pathlib import Path
import argparse,hashlib,json,shutil,subprocess,sys,zipfile
ROOT=Path(__file__).resolve().parents[2]
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source',type=Path);p.add_argument('--runtime',type=Path);p.add_argument('--prepare',action='store_true');p.add_argument('--replace',nargs=2,action='append',default=[],metavar=('SCENE','PACKAGE'));args=p.parse_args()
 overrides=dict(args.replace);assert len(overrides)==len(args.replace),'duplicate scene override'
 common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent
 source=args.source or common/'.local-assets/releases/WallpaperSdk-20261110-preview3'
 names={'MiniShotGame':('minishotgame','先行者'),'Train':('train','复古车厢'),'Showcase':('showcase','白色展示柜'),'Parking':('parking','阈限空间'),'RainyCafe':('rainycafe','雨夜咖啡馆')}
 assert set(overrides)<=set(names),'unknown scene override'
 target=ROOT/'Prototype/Assets/StreamingAssets/sdk-content' if args.prepare else ROOT/'Prototype/Builds/Library/SpatialWallpaper_Data/StreamingAssets/sdk-content';target.mkdir(parents=True,exist_ok=True)
 entries=[]
 for record in json.loads((source/'packages.json').read_text()):
  package=Path(overrides[record['name']]).resolve() if record['name'] in overrides else source/record['package']
  digest=hashlib.sha256(package.read_bytes()).hexdigest()
  if record['name'] not in overrides:assert digest==record['sha256'],'existing package changed'
  with zipfile.ZipFile(package) as z:manifest=json.loads(z.read('manifest.json'))
  assert manifest['sdk']=='0.3.0-preview.3' and manifest['engine']=='2022.3.62f3' and manifest['pipeline']=='14.0.12'
  original,title=names[record['name']];shutil.copy2(package,target/package.name)
  entries.append(dict(file=package.name,original_id=original,name=title,build_id=manifest['build_id'],sha256=digest))
 (target/'catalog.json').write_text(json.dumps(entries,indent=2,ensure_ascii=False),encoding='utf-8')
 if not args.prepare:
  if args.runtime is None:p.error('--runtime is required for packaging the built product')
  dest=ROOT/'Prototype/Builds/Library/sdk-runtime'
  if dest.exists():raise FileExistsError('refuse overwriting an existing runtime; use a new build destination')
  shutil.copytree(args.runtime,dest)
 print('Staged existing SDK packages:',target)
if __name__=='__main__':main()
