"""Compare the same frozen-water eye path; pixel metrics do not prove perceptual stability."""
from pathlib import Path
from PIL import Image, ImageDraw
import numpy as np,json
r=Path(__file__).resolve().parents[2]/'Validation/Limpool'
canvas=Image.new('RGB',(1200,660),'#20252b');draw=ImageDraw.Draw(canvas);report={}
for row,label in enumerate(['glint-before','glint-after']):
    arrays=[]
    for p in sorted((r/label).glob('[0-9][0-9][0-9][0-9].png')):
        arrays.append(np.asarray(Image.open(p),dtype=np.float32)[650:895,990:1240].mean(axis=2))
    second=np.array([np.abs(arrays[i+1]-2*arrays[i]+arrays[i-1]) for i in range(1,len(arrays)-1)])
    report[label]={'frames':len(arrays),'roi':[990,650,1240,895],'second_difference_mean':float(second.mean()),'second_difference_p95':float(np.quantile(second,.95))}
    for col,frame in enumerate([0,15,30,45,60,75]):
        im=Image.open(r/label/f'{frame:04}.png').crop((990,650,1240,895)).resize((200,294))
        canvas.paste(im,(col*200,row*330+30));draw.text((col*200+5,row*330+8),f'{label} / {frame}',fill='white')
canvas.save(r/'glint-comparison.png')
(r/'glint-comparison.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
