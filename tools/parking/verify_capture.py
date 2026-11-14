"""Check actual framebuffer evidence, independent of material parameter assertions."""
import csv,json,math
from pathlib import Path
import numpy as np
from PIL import Image
root=Path(__file__).resolve().parents[2]/'Validation/Parking'
def pixels(path):return np.asarray(Image.open(path).convert('RGB'),dtype=float)
result={}
for name,count in [('stills',11),('box-stills',11),('video',360),('hold',360)]:
    rows=list(csv.DictReader((root/name/'capture.csv').open()))
    assert len(rows)==count,(name,len(rows))
    error=max(float(r['corner_error_px']) for r in rows)
    assert math.isfinite(error) and error<.01,(name,error)
    result[name]={'frames':len(rows),'max_corner_error_px':error}
for path in (root/'box-stills').glob('*.png'):
    frame=pixels(path)
    for ys in [slice(0,4),slice(-4,None)]:
        for xs in [slice(0,4),slice(-4,None)]:
            patch=frame[ys,xs]
            assert ((patch[:,:,1]>70)&(patch[:,:,1]>patch[:,:,0]*1.5)&(patch[:,:,2]>patch[:,:,0]*1.5)).any(),('Box guide missing at framebuffer corner',path,ys,xs)
result['box_guide_pixels']='All four framebuffer corners contain the actual guide in all 11 poses/states'
front=pixels(root/'stills/front.png');restored=pixels(root/'stills/restored-tvs.png');hidden=pixels(root/'stills/hidden-tvs.png')
restore_error=float(np.abs(front-restored).mean());assert restore_error<1,restore_error
tv_region=(slice(650,850),slice(690,910))
hidden_difference=float(np.abs(front[tv_region]-hidden[tv_region]).mean());assert hidden_difference>2,hidden_difference
plinth=(slice(598,614),slice(246,264))
base_mean=float(front[plinth].mean());assert base_mean>8,base_mean
old=root/'lighting-before/front.png'
result['visibility']={'restore_mean_rgb_error_255':restore_error,'hidden_region_mean_rgb_difference_255':hidden_difference,'plinth_mean_rgb_255':base_mean}
floor=[];screen=[]
for i in range(0,360,10):
    frame=pixels(root/'hold'/f'{i:04d}.png')
    floor.append(float(frame[775:840,740:835].mean()))
    screen.append(float(frame[728:751,758:783].mean()))
assert max(floor)-min(floor)>2,'Floor illumination did not visibly animate'
assert max(screen)-min(screen)>10,'CRT brightness did not visibly animate'
result['hold_pixels']={'sample_count':len(floor),'floor_brightness_range_255':[min(floor),max(floor)],'screen_brightness_range_255':[min(screen),max(screen)]}
(root/'visual-checks.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result,indent=2))
