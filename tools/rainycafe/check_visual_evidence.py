"""Check actual Player image differences and save unaltered detail crops."""
import json
from pathlib import Path
import numpy as np
from PIL import Image
r=Path(__file__).resolve().parents[2]/'Validation/RainyCafe'
front=Image.open(r/'stills/front.png').convert('RGB')
off=Image.open(r/'stills-no-shadows/front.png').convert('RGB')
a=np.asarray(front,dtype=np.int16);b=np.asarray(off,dtype=np.int16)
delta=np.max(np.abs(a-b),axis=2)
roi=delta[450:850,1080:1530]
assert np.count_nonzero(roi>2)>25, 'No measured candle shadow contribution near tables'
result={'shadow_pixels_changed_over_2':int(np.count_nonzero(roi>2)), 'shadow_peak_channel_difference':int(roi.max()),'comparison':'Same actual Player eye and time; only candle shadow switch differs'}
details=r/'details';details.mkdir(exist_ok=True)
for name,box in {'candles-steam':(1100,450,1510,850),'glass-rain':(390,160,1080,570),'counter-materials':(0,315,535,640),'upper-shelf-candle':(0,70,390,295)}.items():
 front.crop(box).save(details/(name+'.png'))
before=Image.open(r/'hold/0000.png').convert('RGB');after=Image.open(r/'hold/0300.png').convert('RGB')
d=np.max(np.abs(np.asarray(before,dtype=np.int16)-np.asarray(after,dtype=np.int16)),axis=2)
for name,box in {'rain':(420,200,850,540),'candle-steam':(1180,490,1480,750),'upper-candle':(120,80,360,270)}.items():
 x0,y0,x1,y1=box;count=int(np.count_nonzero(d[y0:y1,x0:x1]>2));assert count>20,(name,count)
 result[name+'_changed_pixels']=count
(r/'visual-checks.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
