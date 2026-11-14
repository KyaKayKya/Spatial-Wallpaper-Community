"""Check that native intensity encoding preserves the previous lit appearance."""
from pathlib import Path
import io,json,subprocess
import numpy as np
from PIL import Image
r=Path(__file__).resolve().parents[2]
before=Image.open(io.BytesIO(subprocess.check_output(['git','show','8fe94c5:Validation/RainyCafe/stills/front.png'],cwd=r))).convert('RGB')
after=Image.open(r/'Validation/RainyCafe/stills-legacy-rain/front.png').convert('RGB')
a=np.asarray(before,dtype=np.int16);b=np.asarray(after,dtype=np.int16)
d=np.abs(a-b)
result={'reference_commit':'8fe94c5','same_time_s':2,'full_frame_mean_absolute_channel_difference':float(d.mean()),'cup_roi_mean_absolute_channel_difference':float(d[204:225,252:274].mean()),'cup_roi_before_mean':float(a[204:225,252:274].mean()),'cup_roi_after_mean':float(b[204:225,252:274].mean())}
assert result['cup_roi_mean_absolute_channel_difference']<2 and result['full_frame_mean_absolute_channel_difference']<2,result
(r/'Validation/RainyCafe/candle-energy-check.json').write_text(json.dumps(result,indent=2));print(result)
