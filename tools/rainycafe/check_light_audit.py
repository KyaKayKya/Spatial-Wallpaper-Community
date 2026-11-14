"""Verify the shelf candle is present in the actual fragment light list."""
from pathlib import Path
import sys,json
import numpy as np
from PIL import Image
r=Path(__file__).resolve().parents[2]/'Validation/RainyCafe'
stage=sys.argv[1]
a=np.asarray(Image.open(r/'light-audit/front.png').convert('RGB').crop((252,204,274,225)))
result={'time_s':15,'roi':[252,204,274,225],'median_rgb':np.median(a.reshape(-1,3),axis=0).tolist(),'target_present_fraction':float(np.mean(a[:,:,1]>200))}
if stage=='after':assert result['target_present_fraction']>.95,result
(r/f'candle-light-list-{stage}.json').write_text(json.dumps(result,indent=2))
Image.fromarray(a).resize((220,210),Image.Resampling.NEAREST).save(r/f'details/candle-light-list-{stage}.png')
print(result)
