from pathlib import Path
from PIL import Image
import numpy as np
r=Path(__file__).resolve().parents[2]/'Validation/Limpool'
a=np.asarray(Image.open(r/'stills-default/front.png'),dtype=float)
b=np.asarray(Image.open(r/'stills-no-sea/front.png'),dtype=float)
d=np.abs(a-b)[250:450,610:970]
print('Sea beams sky ROI mean/max RGB difference',d.mean(),d.max())
