"""Preserve reproducible Player evidence without test presets or device identifiers."""
from pathlib import Path
from PIL import Image,ImageOps,ImageDraw,ImageFont
import hashlib,json,shutil,sys
root=Path(__file__).resolve().parents[2]
source=(root/sys.argv[1]).resolve();assert source.parent==root/'Validation/Library' and (source/'result.txt').is_file()
target=root/'Validation/Library/verified';target.mkdir(exist_ok=True)
for name in ['library-wide.png','library-800.png','library-500.png','no-wallpaper.png','library-refined.png','library-sixty.png','library-many-pages.png','library-layout-1.png','library-layout-2.png','library-layout-3.png','library-layout-4.png','result.txt','facade-coverage.txt']:
 shutil.copy2(source/name,target/name)
for name in ['showcase','pool','train','parking','minishotgame','limpool','rainycafe']:
 shutil.copy2(source/f'{name}-camera-fullscreen.png',target/f'{name}-camera-fullscreen.png')
for pattern in ['limpool-boundary-*.png','rainycafe-boundary-*.png','limpool-adapt-*.png','rainycafe-adapt-*.png']:
 for image in source.glob(pattern):shutil.copy2(image,target/image.name)
for artwork in ['limpool','rainycafe']:
 for mode in ['boundary','adapt']:
  images=sorted(source.glob(artwork+'-'+mode+'-*.png'));columns=3 if mode=='boundary' else 5
  sheet=Image.new('RGB',(columns*400,((len(images)+columns-1)//columns)*310),'#eeeeee');labels=ImageDraw.Draw(sheet)
  for i,path in enumerate(images):
   image=ImageOps.contain(Image.open(path).convert('RGB'),(392,280));x=(i%columns)*400;y=(i//columns)*310
   sheet.paste(image,(x+(400-image.width)//2,y));labels.text((x+4,y+284),path.stem,fill='#111111')
  sheet.save(target/(artwork+'-'+mode+'-contact.jpg'),quality=90)
canvas=Image.new('RGB',(1440,1020),'#F3F5F8');draw=ImageDraw.Draw(canvas)
font=ImageFont.truetype(str(root/'Prototype/Assets/Calibration/Fonts/SourceHanSansSC-Regular.otf'),22)
for i,(name,label) in enumerate(zip(['showcase','pool','train','parking','minishotgame','limpool','rainycafe'],['白色展示柜','池核','复古车厢','阈限空间','先行者','Limpool','雨夜咖啡馆'])):
 im=Image.open(source/f'{name}-camera-fullscreen.png').convert('RGB');im=ImageOps.contain(im,(464,296))
 x=(i%3)*480+8;y=(i//3)*340+8;canvas.paste(im,(x,y));draw.text((x,y+300),label,font=font,fill='#202735')
canvas.save(target/'artworks.jpg',quality=92)
files=['Prototype/Builds/Library/SpatialWallpaper.exe','Prototype/Builds/Library/SpatialWallpaper_Data/Managed/Assembly-CSharp.dll','Validation/Library/build-result.txt']
hashes={name:hashlib.sha256((root/name).read_bytes()).hexdigest() for name in files}
(target/'build-sha256.json').write_text(json.dumps(hashes,indent=2)+'\n',encoding='utf8')
shutil.copy2(root/'Validation/Library/build-result.txt',target/'build-result.txt')
print('Archived evidence',target)
