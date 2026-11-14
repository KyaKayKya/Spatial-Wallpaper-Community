"""Fixed-eye frames must change outside the glass only, never seats or lamps."""
import json
from pathlib import Path
import numpy as np
from PIL import Image
root=Path(__file__).resolve().parents[2]/'Validation/Train'
base=np.asarray(Image.open(root/'hold/0000.png').convert('RGB')).astype(np.int16)
mask=np.ones(base.shape[:2],dtype=bool)
# Conservative window-and-frame ROI at the fixed reference eye, 1600x900.
# Seats, lamps, ceiling, side walls and floor remain fully under comparison.
mask[260:580,550:1050]=False
results=[]
for frame in (60,120,200,300,450,600,719):
 image=np.asarray(Image.open(root/'hold'/f'{frame:04d}.png').convert('RGB')).astype(np.int16)
 difference=np.abs(image-base).max(axis=2)
 if np.any(difference[mask]>1):
  print('Difference coordinates:',np.argwhere((difference>1)&mask).tolist()[:30])
 results.append(dict(frame=frame,changed_interior_pixels=int(np.sum(difference[mask]>1)),max_interior_difference=int(difference[mask].max()),changed_window_pixels=int(np.sum(difference[~mask]>1))))
# A few isolated depth-edge pixels can differ despite identical transforms.
# The previous moving-seat capture changed >96,000 pixels; allow at most ten
# low-intensity raster differences, and independently require zero mesh motion.
geometry=(root/'hold/geometry.txt').read_text()
passed='interiorTransformChanges=0;' in geometry and all(r['changed_interior_pixels']<=10 and r['max_interior_difference']<=16 and r['changed_window_pixels']>100 for r in results)
report=dict(passed=passed,resolution=[1600,900],allowed_isolated_pixels=10,max_channel_tolerance=16,geometry=geometry.strip(),samples=results)
(root/'rigid-interior.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report))
assert passed,'Interior moved or exterior stopped in fixed-eye capture'
