"""Measure fixed-eye shelf-cup illumination continuity over the full video."""
from pathlib import Path
import json,sys
import csv
import numpy as np
from PIL import Image
r=Path(__file__).resolve().parents[2]/'Validation/RainyCafe'
stage=sys.argv[1]
values=[]
for f in sorted((r/'hold').glob('[0-9][0-9][0-9][0-9].png')):
 a=np.asarray(Image.open(f).convert('RGB').crop((252,204,274,225)),dtype=np.float32)
 values.append(float(a.mean()))
a=np.asarray(values)
result={'frames':len(values),'roi':[252,204,274,225], 'minimum_mean_channel':float(a.min()),'maximum_mean_channel':float(a.max()),'minimum_to_maximum':float(a.min()/a.max()),'max_adjacent_frame_jump':float(np.abs(np.diff(a)).max())}
if stage=='after':
 assert len(values)==1350 and result['minimum_to_maximum']>.75 and result['max_adjacent_frame_jump']<3,result
 rows=list(csv.DictReader((r/'hold/capture.csv').open()))
 result['rain_particle_count_min']=min(int(row['rain_particle_count']) for row in rows)
 assert result['rain_particle_count_min']>500,result
(r/f'candle-stability-{stage}.json').write_text(json.dumps(result,indent=2))
print(result)
