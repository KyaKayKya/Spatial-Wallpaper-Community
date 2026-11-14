from pathlib import Path
from PIL import Image
import numpy as np,sys,json
r=Path(__file__).resolve().parents[2]/'Validation/Limpool';result={}
for label in sys.argv[1:]:
 frames=[np.asarray(Image.open(p),dtype=np.float32)[650:850,250:1350].mean(axis=2) for p in sorted((r/label).glob('[0-9][0-9][0-9][0-9].png'))]
 values=[float(np.abs(frames[i+1]-2*frames[i]+frames[i-1]).mean()) for i in range(1,len(frames)-1)]
 result[label]={'frames':len(frames),'temporal_second_difference_mean':float(np.mean(values)),'p95':float(np.quantile(values,.95))}
print(json.dumps(result,indent=2));(r/'temporal-metric.json').write_text(json.dumps(result,indent=2))
