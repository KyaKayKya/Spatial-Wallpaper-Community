"""Check actual Player waterline captures, including the exact surface eye."""
from pathlib import Path
import csv,json,sys
import numpy as np
from PIL import Image
r=Path(__file__).resolve().parents[2];out=r/'Validation/Limpool'
folder=out/(sys.argv[1] if len(sys.argv)>1 else 'underwater-final-default')
rows=list(csv.DictReader((folder/'capture.csv').read_text().splitlines()))
assert len(rows)==54
assert max(float(x['corner_error_px']) for x in rows)<.01
assert max(float(x['reflection_uv_error']) for x in rows)<.00001
assert sum(int(x['underwater_clip_failures']) for x in rows)==0
colors={}
for index in [26,27,28,29]:
    path=folder/f'eye-{index:03}.png';legacy=folder/f'eye-{index:02}.png'
    assert not (path.exists() and legacy.exists()),'Mixed old/new capture names: use a fresh output directory'
    if not path.exists():path=legacy
    image=np.asarray(Image.open(path))
    # Left near pool floor, below the waterline and outside the sunshaft.
    rgb=image[800:840,400:550,:3].mean(axis=(0,1))
    colors[str(index)]=rgb.tolist()
    assert rgb[2]>rgb[0]*1.5,('Dry-looking pool floor at surface/below eye',index,rgb)
report={'passed':True,'folder':str(folder),'eyes':len(rows),'max_corner_error_px':max(float(x['corner_error_px']) for x in rows),'clip_failures':0,'floor_rgb':colors}
(out/'underwater-check.json').write_text(json.dumps(report,indent=2))
print(json.dumps(report,indent=2))
