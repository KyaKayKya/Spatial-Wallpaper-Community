from pathlib import Path
from PIL import Image,ImageDraw
import subprocess
r=Path(__file__).resolve().parents[2]
main=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=r,text=True).strip()).parent
old=main/'.local-assets/limpool/delivery-v1/Evidence/stills-default/front.png'
if not old.exists():old=main/'.local-assets/limpool/delivery/Evidence/stills-default/front.png'
new=r/'Validation/Limpool/stills-default/front.png'
canvas=Image.new('RGB',(1600,940),'#182128');draw=ImageDraw.Draw(canvas)
for x,p,label in [(0,old,'Previous delivery'),(800,new,'Revised standalone Player')]:
 im=Image.open(p).resize((800,450));canvas.paste(im,(x,30));draw.text((x+12,10),label,fill='white')
 im=Image.open(p).crop((170,470,780,790)).resize((800,420));canvas.paste(im,(x,510));draw.text((x+12,490),'Wall joint / lit water detail',fill='white')
canvas.save(r/'Validation/Limpool/comparison.png')
