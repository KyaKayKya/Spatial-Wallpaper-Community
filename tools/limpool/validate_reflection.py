"""One-build A/B regression: same frozen wave, eye path and scene, only prefilter changes."""
from pathlib import Path
from PIL import Image
import subprocess,sys,json,hashlib
import numpy as np
r=Path(__file__).resolve().parents[2];out=r/'Validation/Limpool'
(out/'reflection-stability.json').write_text(json.dumps({'pass':False,'status':'running'}))
def run(*args):subprocess.run([sys.executable,str(r/'tools/limpool'/args[0])]+list(args[1:]),cwd=r,check=True)
if '--analyze-only' not in sys.argv:
    run('diagnostic.py','prefilter-before','--raw-reflection','--fresnel-normal-strength','1','--frames','900')
    run('diagnostic.py','prefilter-after','--frames','900')
run('track_water.py','prefilter-before','prefilter-after')
if '--analyze-only' not in sys.argv:run('player.py','stills','split-energy','--water-debug','13')
metrics=json.loads((out/'tracked-water.json').read_text())
for side in ['left','right']:
    before=metrics['prefilter-before'][side];after=metrics['prefilter-after'][side]
    assert after['p99']<before['p99']*.55,(side,metrics)
    assert after['fraction_over_10']<before['fraction_over_10']*.1,(side,metrics)
energy=np.asarray(Image.open(out/'stills-split-energy/front.png'))[740:880,300:1300]
assert energy.max()<3,'HDR split reconstruction exceeded shader tolerance'
build=r/'Prototype/Builds/Limpool';hashes={str(p.relative_to(build)):hashlib.sha256(p.read_bytes()).hexdigest() for p in build.rglob('*') if p.is_file()}
report={'method':'900 frames, frozen water time=12s, identical left/right, up/down, near/far eye path, fixed water-plane points projected into each frame; before disables radiance split and restores Fresnel normal strength=1. RGB-mean second difference includes natural reflection motion and is not a perceptual flicker certification.','build_fingerprint':hashlib.sha256(json.dumps(hashes,sort_keys=True).encode()).hexdigest(),'metrics':metrics,'hdr_split_tolerance':'absolute .002 + .001*abs(source HDR), mode13 outputs only excess error x100','energy_excess_rgb_max':float(energy.max()),'energy_excess_rgb_p999':float(np.quantile(energy,.999)),'pass':True}
(out/'reflection-stability.json').write_text(json.dumps(report,indent=2))
run('reflection_contact.py')
print(json.dumps(report,indent=2))
