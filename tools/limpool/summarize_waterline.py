"""Aggregate the Unity 2022 critical-waterline acceptance evidence."""
from pathlib import Path
import argparse,csv,hashlib,json,re,subprocess
from PIL import Image,ImageDraw
r=Path(__file__).resolve().parents[2];out=r/'Validation/Limpool'
p=argparse.ArgumentParser()
for n in ['library-smoke','desktop-run','visibility']:p.add_argument('--'+n,type=Path,required=True)
args=p.parse_args();report={'passed':True,'date':'2026-10-26','unity':'2022.3.62f3','urp':'14.0.12','api':'D3D11','resolution':[1600,900],'simulated_eye_only':True,'standalone_source_commit':'421df85','integration_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=r,text=True).strip(),'runs':{}};fingerprints=set()
for folder,count in [('waterline-normal-default',15),('waterline-final-default',144),('waterline-final-low',144),('underwater-final-default',54),('waterline-coverage-before-default',144),('waterline-coverage-final-default',144),('video-default',900),('hold-default',900)]:
 f=out/folder;rows=list(csv.DictReader((f/'capture.csv').read_text().splitlines()));assert len(rows)==count and 'pass=True' in (f/'checks.txt').read_text()
 log=(f/'player.log').read_text(errors='replace');assert not re.search(r'Shader error|\w+Exception|Assertion failed',log)
 fingerprints.add(json.loads((f/'run.json').read_text())['build_fingerprint']);report['runs'][folder]={'frames':count,'max_corner_error_px':max(float(x['corner_error_px']) for x in rows),'max_reflection_uv_error':max(float(x['reflection_uv_error']) for x in rows),'clip_failures':sum(int(x['underwater_clip_failures']) for x in rows),'reflection_far_corner_diagnostics':log.count('Screen position out of view frustum')}
 if folder=='hold-default':assert len({(x['eye_x'],x['eye_y'],x['eye_z']) for x in rows})==1
assert len(fingerprints)==1;report['standalone_build_fingerprint']=fingerprints.pop()
for q in ['default','low']:
 data=json.loads((out/('bench-'+q)/'run.json').read_text());assert data['build_fingerprint']==report['standalone_build_fingerprint'] and '--benchmark-waterline' in data['command']
control=json.loads((out/'waterline-coverage-before-default/waterline-review.json').read_text());fixed=json.loads((out/'waterline-coverage-final-default/waterline-review.json').read_text());assert not control['passed'] and fixed['passed']
report['coverage_control_failures']=len(control['critical_roi_failures']);report['coverage_fixed_failures']=len(fixed['critical_roi_failures']);report['video_review']=json.loads((out/'waterline-video-review.json').read_text())
report['performance']=json.loads((out/'waterline-performance.json').read_text());report['hardware']=(out/'bench-default/hardware.txt').read_text().splitlines()
report['performance_protocol']={'fps_cap':60,'vsync':False,'warmup_s':10,'fixed_s':60,'moving_s':60,'retained_seconds':[[12,68],[72,128]],'timings_deduplicated':True,'capture_disabled':True,'limits':'Two qualities, not a six-effect ablation. GPU clocks not recorded; no claim that low quality reduces GPU median. CPU frame time includes the frame-cap wait.'}
for key,path in [('library_smoke',args.library_smoke),('desktop_run',args.desktop_run),('native_visibility',args.visibility)]:
 path=path if path.is_absolute() else r/path
 if key=='library_smoke':assert (path/'result.txt').read_text().startswith('PASS') and not (path/'errors.txt').exists();result=(path/'result.txt').read_text().strip()
 else:result=json.loads((path/'result.json').read_text());assert result['passed']
 report[key]={'folder':path.relative_to(r).as_posix(),'result':result}
for path in [*(r/'Prototype/Assets/Limpool/Runtime').glob('*.cs'),*(r/'Prototype/Assets/Limpool/Runtime').glob('*.shader'),*(r/'Prototype/Assets/Limpool/Editor').glob('*.cs')]:
 previous=subprocess.check_output(['git','show','421df85:'+path.relative_to(r).as_posix()],cwd=r);assert path.read_bytes().replace(b'\r\n',b'\n')==previous.replace(b'\r\n',b'\n')
report['limits']=['Central near-water ROI coverage and whole-image coplanar stripe checks do not replace visual review.','Some static near-surface reflection-camera far-corner diagnostics remain, also present in the old-clipping control. Raw logs are retained; this is not a clean-log certification.','Approximate planar reflection/refraction and homogeneous absorption; not a full physical Snell window.','D3D11 and simulated eyes only; Unity 6 integration not adopted.','Standalone recording/performance build is from 421df85. Unified desktop was rebuilt after merging unrelated native-window fixes; Limpool sources match exactly.']
for path in [out/'waterline-regression.json',r/'docs/reference/evidence/limpool/waterline-regression.json']:path.write_text(json.dumps(report,ensure_ascii=False,indent=2)+'\n')
contact=Image.new('RGB',(1600,490),(18,25,30));draw=ImageDraw.Draw(contact)
for x,path,label in [(0,out/'waterline-before-default/eye-060.png','Before: eye 0.5mm ABOVE water'),(800,out/'waterline-final-default/eye-068.png','After: same eye, continuous water surface')]:
 image=Image.open(path).convert('RGB');image.thumbnail((800,450));contact.paste(image,(x,40));draw.text((x+12,12),label,fill='white')
contact.save(out/'waterline-comparison.jpg',quality=95)
print('Validated',len(report['runs']),'runs; coverage failures',report['coverage_control_failures'],'->',report['coverage_fixed_failures'])
