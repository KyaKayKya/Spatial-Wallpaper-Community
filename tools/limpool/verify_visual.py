"""GPU readback regressions for opaque depth, repeated eye and repaired joint."""
from pathlib import Path
from PIL import Image
import numpy as np,json
r=Path(__file__).resolve().parents[2]/'Validation/Limpool'
depth=np.asarray(Image.open(r/'stills-depth-proof/front.png'))[650:850,250:1350]
repeat=json.loads((r/'repeat-eye/repeat-eye.json').read_text())
joint=json.loads((r/'joint-check.json').read_text())
result={'depth_debug_red_median':float(np.median(depth[:,:,0])),'depth_note':'Debug red encodes reconstructed water optical depth through display tonemapping. A zero-depth regression is black in red; this checks actual GPU output, not exact physical absorption.', 'repeat_eye':repeat,'left_joint':joint}
result['pass']=bool(result['depth_debug_red_median']>30 and repeat['return_mean_abs']<1 and repeat['return_fraction_over_8']<.001 and joint['pass'])
(r/'visual-regression.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
assert result['pass']
