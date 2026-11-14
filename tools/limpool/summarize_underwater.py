"""Collect measured low-eye regressions from the same final standalone build."""
from pathlib import Path
import csv,json,re,argparse
from PIL import Image,ImageDraw
r=Path(__file__).resolve().parents[2];out=r/'Validation/Limpool'
parser=argparse.ArgumentParser();parser.add_argument('--library-smoke',type=Path,required=True);args=parser.parse_args()
groups={'water-above':('stills-default',15),'waterline-default':('underwater-final-default',54),'waterline-low':('underwater-low-low',54),'crossing-video':('video-default',360)}
report={'passed':True,'date':'2026-10-25','api':'Direct3D11','resolution':[1600,900],'simulated_eye':True,'baseline_clip_failures':108,'runs':{}}
fingerprints=set()
for name,(folder,count) in groups.items():
    p=out/folder;rows=list(csv.DictReader((p/'capture.csv').read_text().splitlines()));assert len(rows)==count
    assert 'pass=True' in (p/'checks.txt').read_text()
    log=(p/'player.log').read_text(errors='replace')
    assert not re.search(r'Screen position out of view frustum|Shader error|NullReferenceException|ArgumentException|AssertionException',log)
    run=json.loads((p/'run.json').read_text());fingerprints.add(run['build_fingerprint'])
    report['runs'][name]={'folder':folder,'frames':count,'max_corner_error_px':max(float(x['corner_error_px']) for x in rows),'max_reflection_uv_error':max(float(x['reflection_uv_error']) for x in rows),'clip_failures':sum(int(x['underwater_clip_failures']) for x in rows),'build_fingerprint':run['build_fingerprint']}
assert len(fingerprints)==1
report['standalone_build_fingerprint']=fingerprints.pop()
report['floor_pixels']=json.loads((out/'underwater-check.json').read_text())['floor_rgb']
smoke=args.library_smoke if args.library_smoke.is_absolute() else r/args.library_smoke
assert (smoke/'result.txt').read_text().startswith('PASS') and not (smoke/'errors.txt').exists()
report['library_smoke']=smoke.relative_to(r).as_posix();report['library_result']=(smoke/'result.txt').read_text().strip()
report['limits']='Approximate planar reflection/refraction and homogeneous absorption, not physically complete underwater rendering. No new six-quality performance run or real tracking acceptance.'
(out/'underwater-regression.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
contact=Image.new('RGB',(1600,490),(18,25,30));draw=ImageDraw.Draw(contact)
for x,path,label in [(0,out/'underwater-before-default/eye-24.png','Before: camera below water'),(800,out/'underwater-final-default/eye-29.png','After: same eye, water volume and underside')]:
    im=Image.open(path).convert('RGB');im.thumbnail((800,450));contact.paste(im,(x,40));draw.text((x+12,12),label,fill='white')
contact.save(out/'underwater-comparison.jpg',quality=95)
print(json.dumps(report,ensure_ascii=False,indent=2))
