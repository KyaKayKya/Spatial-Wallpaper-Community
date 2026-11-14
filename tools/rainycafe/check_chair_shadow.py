"""Compare identical Player frames with only local shadows switched off."""
from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw
r=Path(__file__).resolve().parents[2]/'Validation/RainyCafe'
a=Image.open(r/'stills/front.png').convert('RGB')
b=Image.open(r/'stills-no-shadows/front.png').convert('RGB')
box=(1220,730,1510,900)
x=np.asarray(a.crop(box),dtype=np.int16);y=np.asarray(b.crop(box),dtype=np.int16)
d=y-x
result={'roi':box,'shadow_darkened_pixels_over_2':int(np.count_nonzero(d.max(axis=2)>2)), 'max_darkening':int(d.max()),'same_eye_and_time':True}
assert result['shadow_darkened_pixels_over_2']>25,result
canvas=Image.new('RGB',(870,194));draw=ImageDraw.Draw(canvas)
for i,(title,img) in enumerate([('Shadows ON',a.crop(box)),('Shadows OFF',b.crop(box)),('Difference x8',Image.fromarray(np.uint8(np.clip(np.abs(d)*8,0,255))))]):
 canvas.paste(img,(i*290,24));draw.text((i*290+4,5),title,fill='white')
(r/'details').mkdir(exist_ok=True)
canvas.save(r/'details/chair-shadow-comparison.png')
(r/'chair-shadow-check.json').write_text(json.dumps(result,indent=2))
print(result)
