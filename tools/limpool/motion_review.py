from pathlib import Path
from PIL import Image,ImageDraw
import numpy as np,json
r=Path(__file__).resolve().parents[2]/'Validation/Limpool';folder=r/'video-default'
canvas=Image.new('RGB',(1600,800),'#182128');draw=ImageDraw.Draw(canvas)
for j,i in enumerate([0,75,150,225,255,256,257,300,375,450,525,600,675,750,825,899]):
 im=Image.open(folder/f'{i:04d}.png').crop((250,550,1250,900)).resize((400,170))
 x=(j%4)*400;y=(j//4)*200;canvas.paste(im,(x,y+25));draw.text((x+8,y+6),f'Frame {i} / {i/30:.2f}s',fill='white')
canvas.save(r/'motion-contact.png')
means=[]
for i in range(900):
 a=np.asarray(Image.open(folder/f'{i:04d}.png'),dtype=np.float32)[650:850,250:1350]
 means.append(float(a.mean()))
delta=np.abs(np.diff(means));result={'water_roi_mean_luminance_frame_change_max':float(delta.max()),'p95':float(np.quantile(delta,.95)),'max_at_frame':int(delta.argmax()+1),'note':'Camera and animation move; this is a diagnostic, not a flicker pass criterion.'}
(r/'motion-review.json').write_text(json.dumps(result,indent=2));print(result)
