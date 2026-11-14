"""Measure same frozen scene/eye after a complete leftward excursion."""
from pathlib import Path
import subprocess,sys,json
import numpy as np
from PIL import Image
r=Path(__file__).resolve().parents[2];out=r/'Validation/Limpool/repeat-eye';out.mkdir(parents=True,exist_ok=True)
cmd=[str(r/'Prototype/Builds/Limpool/Limpool.exe'),'-force-d3d11','-screen-fullscreen','0','-screen-width','1600','-screen-height','900','-logFile',str(out/'player.log'),'--limpool-capture',str(out),'--video','--frames','153','--freeze-effects']
subprocess.run(cmd,check=True)
a=np.asarray(Image.open(out/'0000.png'),dtype=float)[650:850,250:1350]
b=np.asarray(Image.open(out/'0150.png'),dtype=float)[650:850,250:1350]
c=np.asarray(Image.open(out/'0151.png'),dtype=float)[650:850,250:1350]
d=np.abs(a-b);settle=np.abs(b-c)
result={'method':'Frozen shader time, center-left-center path; compare identical center eye at frames 0 and 150. Water ROI x250:1350,y650:850. RGB 8-bit pixel difference, not a perceptual flicker score.', 'return_mean_abs':float(d.mean()),'return_p95_abs':float(np.quantile(d,.95)),'return_fraction_over_8':float((d.max(axis=2)>8).mean()),'next_frame_mean_abs':float(settle.mean())}
(out/'repeat-eye.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2))
