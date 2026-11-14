"""Measure selected screenshot regions against the previous lighting revision."""
import io,json,subprocess
from pathlib import Path
import numpy as np
from PIL import Image
root=Path(__file__).resolve().parents[2]
baseline='dc20e83'
before=np.asarray(Image.open(io.BytesIO(subprocess.check_output(['git','show',baseline+':Validation/Train/stills/front.png'],cwd=root))).convert('RGB'))/255.
after=np.asarray(Image.open(root/'Validation/Train/stills/front.png').convert('RGB'))/255.
regions={'near_left_wall':[30,120,290,390],'near_right_wall':[1310,120,1570,390],
         'seat':[260,460,480,550],'exterior':[630,320,970,510],'left_lamp':[485,345,530,380]}
result={}
for name,(x0,y0,x1,y1) in regions.items():
 values=[]
 for image in (before,after):
  values.append(float((image[y0:y1,x0:x1]@np.array([.2126,.7152,.0722])).mean()))
 result[name]={'before_srgb_luma':values[0],'after_srgb_luma':values[1],'ratio':values[1]/values[0]}
report={'baseline':baseline,'note':'Encoded screenshot luma, not physical illuminance or artistic acceptance.','regions':result}
(root/'Validation/Train/lighting-comparison.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
assert all(.15<result[name]['ratio']<.85 for name in ('near_left_wall','near_right_wall','seat','exterior')),'Lighting reduction is absent or shadows are excessively crushed'
assert result['left_lamp']['ratio']>.6,'The practical lamp lost its warm focal brightness'
