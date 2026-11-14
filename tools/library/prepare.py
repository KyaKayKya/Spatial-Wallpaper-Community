"""Reuse installed, licensed runtime assets without copying Unity caches or overwriting tracked files."""
from pathlib import Path
import subprocess,shutil,sys
from PIL import Image
root=Path(__file__).resolve().parents[2]
main=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip()).parent
for folder in ['Pool','ThirdParty','URPWater','Bakery','Editor/x64','MiniShotGame']:
 source=main/'Prototype/Assets'/folder
 if not source.exists():continue
 for src in source.rglob('*'):
  if not src.is_file():continue
  dst=root/src.relative_to(main)
  if not dst.exists():dst.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(src,dst)
thumbs={
 'showcase':'Validation/Showcase/unity/pinlights/views/front-render-texture.png',
 'pool':'Validation/Pool/acceptance-views/front-render-texture.png',
 'train':'Validation/Train/stills/front.png',
 'parking':'Validation/Parking/stills/front.png',
 'minishotgame':'Validation/MiniShotGame/stills/front.png',
 'limpool':str(main/'.local-assets/limpool/delivery/Evidence/stills-default/front.png'),
 'rainycafe':'Validation/RainyCafe/stills/front.png'}
for name,rel in thumbs.items():
 src=root/rel
 if not src.exists():
  print('Missing thumbnail source:',src);continue
 image=Image.open(src).convert('RGB');image.thumbnail((768,432))
 dst=root/'Prototype/Assets/Library/Resources/Library'/f'{name}.jpg';dst.parent.mkdir(parents=True,exist_ok=True);image.save(dst,quality=90)
print('Prepared local assets and verified scene photograph thumbnails')
subprocess.run([sys.executable,str(root/'tools/mita/prepare_library.py')],cwd=root,check=True)
