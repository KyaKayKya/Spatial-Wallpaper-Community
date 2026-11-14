from pathlib import Path
import numpy as np,json
from PIL import Image,ImageDraw
r=Path(__file__).resolve().parents[2]/'Validation/Limpool';folder=r/'video-default'
previous=None;rows=[]
for k in range(300):
    im=np.asarray(Image.open(folder/f'{k:04}.png').resize((400,225))).astype(float)
    roi=im[155:220,20:380]
    if previous is not None:rows.append((float(np.abs(roi-previous).mean()),k))
    previous=roi
worst=sorted(rows,reverse=True)[:8];print(worst)
k=worst[0][1];canvas=Image.new('RGB',(1600,480));draw=ImageDraw.Draw(canvas)
for j,frame in enumerate(range(max(0,k-2),k+2)):
    im=Image.open(folder/f'{frame:04}.png').resize((800,450));x=j%2*800;y=j//2*240
    canvas.paste(im.crop((0,210,800,450)),(x,y));draw.text((x+10,y+10),str(frame),fill='red')
canvas.save(r/'flicker-before.png');(r/'flicker-before.json').write_text(json.dumps(worst))
