"""Compare actual Unity 2022 Player captures at the clipped-water boundary."""
from pathlib import Path
import csv,json,subprocess,sys,shutil
import numpy as np
from PIL import Image,ImageDraw

ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Validation/Limpool'
mode=sys.argv[1]
if mode=='video-review':
    folder=OUT/'video-default';rows=list(csv.DictReader((folder/'capture.csv').read_text().splitlines()));assert len(rows)==900
    assert 'pass=True' in (folder/'checks.txt').read_text()
    water=-.0761875;crossings=[i for i in range(1,len(rows)) if (float(rows[i-1]['eye_y'])-water)*(float(rows[i]['eye_y'])-water)<0]
    means=[]
    for i in range(len(rows)):
        image=Image.open(folder/f'{i:04}.png').convert('RGB');image=image.resize((400,225));array=np.asarray(image).astype(float)
        means.append(array[185:215,100:260].mean(axis=(0,1)))
    means=np.asarray(means);acceleration=np.abs(means[2:]-2*means[1:-1]+means[:-2]).mean(axis=1)
    interesting=set(j for i in crossings for j in [i-1,i,i+1] if 0<=j<len(rows))
    interesting.update((np.argsort(acceleration)[-8:]+1).tolist());selected=sorted(interesting)
    contact=Image.new('RGB',(1600,((len(selected)+5)//6)*170),(24,28,32));draw=ImageDraw.Draw(contact)
    for n,i in enumerate(selected):
        image=Image.open(folder/f'{i:04}.png').convert('RGB');image.thumbnail((266,150));x=n%6*266;y=n//6*170;contact.paste(image,(x,y));draw.text((x+3,y+151),f"{i}: eye {float(rows[i]['eye_y'])-water:+.6f} m",fill='white')
    contact.save(OUT/'waterline-video-contact.jpg',quality=94)
    result={'frames':len(rows),'fps_encoded':30,'duration_s':30,'waterline_crossings':len(crossings),'review_frames':selected,'max_corner_error_px':max(float(x['corner_error_px']) for x in rows),'max_reflection_uv_error':max(float(x['reflection_uv_error']) for x in rows),'mean_color_acceleration_p95':float(np.percentile(acceleration,95)),'mean_color_acceleration_max':float(max(acceleration)),'limits':'Color acceleration includes real waterline transitions and animation; it is not a flicker-free certification.'}
    (OUT/'waterline-video-review.json').write_text(json.dumps(result,indent=2));print(json.dumps(result,indent=2));raise SystemExit(0)
if mode=='prepare-shared':
    import re,hashlib
    main=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent
    deleted=subprocess.check_output(['git','ls-files','--deleted','--','Prototype/Assets'],cwd=ROOT,text=True).splitlines()
    assert all(x.endswith('.meta') for x in deleted),'Unexpected deleted source files; preserve them for inspection'
    if deleted:subprocess.run(['git','restore','--',*deleted],cwd=ROOT,check=True)
    sources=[main,main/'.local-assets/limpool/underwater-fix-20261025/Recovery',main/'.local-assets/limpool/task-archive-1113954',main/'.local-assets/limpool/old-worktree-163b930']
    receipt_path=main/'.local-assets/limpool/underwater-fix-20261025/local-recovery.json'
    receipt=json.loads(receipt_path.read_text())['files'] if receipt_path.exists() else {}
    metas=subprocess.check_output(['git','ls-files','--','Prototype/Assets'],cwd=ROOT,text=True).splitlines();restored=[];missing=[]
    for rel in metas:
        if not rel.endswith('.meta'):continue
        target=ROOT/rel[:-5]
        if target.exists():continue
        guid=re.search(r'^guid: (\w+)',(ROOT/rel).read_text(errors='replace'),re.M)
        entry=receipt.get(rel[:-5])
        if entry:
            source=Path(entry['saved']);prior=subprocess.check_output(['git','show','2f7bc5ac2eb141f67205593be69cf803d75287b5:'+rel],cwd=ROOT,text=True,stderr=subprocess.DEVNULL)
            prior_guid=re.search(r'^guid: (\w+)',prior,re.M)
            if source.is_file() and guid and prior_guid and guid.group(1)==prior_guid.group(1):
                assert hashlib.sha256(source.read_bytes()).hexdigest()==entry['sha256'],source
                target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target);restored.append({'path':rel[:-5],'source':str(source),'sha256':entry['sha256']});continue
        for source_root in sources:
            source=source_root/rel[:-5];source_meta=source_root/rel
            if not source.is_file() or not source_meta.is_file():continue
            other=re.search(r'^guid: (\w+)',source_meta.read_text(errors='replace'),re.M)
            if not guid or not other or guid.group(1)!=other.group(1):continue
            target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target);restored.append({'path':rel[:-5],'source':str(source)});break
        else:
            if Path(rel[:-5]).suffix:missing.append(rel[:-5])
    report={'restored':restored,'missing':missing};(OUT/'shared-recovery.json').write_text(json.dumps(report,indent=2))
    print('Restored GUID-matched missing assets:',len(restored),'unavailable:',len(missing));print('\n'.join(missing[:15]));raise SystemExit(0)
if mode in ['validate','finish']:
    def capture(name,quality,*args):
        subprocess.run([sys.executable,str(ROOT/'tools/limpool/player.py'),name,quality,*args],cwd=ROOT,check=True)
    diagnostic=['--waterline-critical','--freeze-effects','--water-debug','14','--no-volume','--no-waterlight']
    if mode=='validate':
        capture('waterline-coverage-before','default',*diagnostic,'--water-depth-clip')
        baseline=subprocess.run([sys.executable,__file__,'coverage','waterline-coverage-before-default'],cwd=ROOT,stdout=(OUT/'waterline-coverage-before.txt').open('w'))
        assert baseline.returncode==1,'The old depth clipping must fail actual water coverage'
        capture('waterline-coverage-final','default',*diagnostic)
        subprocess.run([sys.executable,__file__,'coverage','waterline-coverage-final-default'],cwd=ROOT,check=True)
    for quality in ['default','low']:
        capture('waterline-final',quality,'--waterline-critical','--freeze-effects')
        subprocess.run([sys.executable,__file__,'review','waterline-final-'+quality],cwd=ROOT,check=True)
    capture('underwater-final','default','--underwater','--freeze-effects')
    subprocess.run([sys.executable,str(ROOT/'tools/limpool/check_underwater.py')],cwd=ROOT,check=True)
    capture('waterline-normal','default','--freeze-effects')
    raise SystemExit(0)
if mode=='prepare':
    main=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent
    count=0
    for folder in ['Train','Pool','ThirdParty']:
        for source in (main/'Prototype/Assets'/folder).rglob('*'):
            if not source.is_file():continue
            target=ROOT/source.relative_to(main)
            if target.exists():continue
            target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target);count+=1
    print('Restored missing local shared assets:',count);raise SystemExit(0)

folder=OUT/sys.argv[2]
rows=list(csv.DictReader((folder/'capture.csv').read_text().splitlines()))
assert len(rows)==144,('Expected 3 distances x 3 horizontal positions x 16 heights',len(rows))
assert max(float(x['corner_error_px']) for x in rows)<.01
assert max(float(x['reflection_uv_error']) for x in rows)<.00001
water=-.0761875
results=[]
for i,row in enumerate(rows):
    data=np.asarray(Image.open(folder/f'eye-{i:03}.png'))[:,:,:3]
    h,w=data.shape[:2]
    # Central near floor, away from side walls, railings and the waterline.
    roi=data[int(h*.88):int(h*.96),int(w*.25):int(w*.65)].astype(float)
    rgb=roi.mean(axis=(0,1));dry=((roi[:,:,0]>70)&(roi[:,:,0]>.8*roi[:,:,2])).mean()
    # ACES maps the debug green to approximately (132,233,72), not (0,255,0).
    green=((roi[:,:,1]>200)&(roi[:,:,1]>roi[:,:,0]*1.5)&(roi[:,:,1]>roi[:,:,2]*2)).mean()
    whole=data.astype(float);whole_green=((whole[:,:,1]>200)&(whole[:,:,1]>whole[:,:,0]*1.5)&(whole[:,:,1]>whole[:,:,2]*2)).mean()
    results.append({'frame':i,'eye_delta_m':float(row['eye_y'])-water,'rgb':rgb.tolist(),'warm_fraction':float(dry),'water_coverage':float(green),'whole_green_fraction':float(whole_green)})
target=[x for x in results if x['eye_delta_m']>1e-6]
coplanar=[x for x in results if abs(x['eye_delta_m'])<=1e-6]
passed=all(x['water_coverage']>.999 for x in target) and all(x['whole_green_fraction']<.0001 for x in coplanar)
selected=[16*4+i for i in range(16)]
contact=Image.new('RGB',(1200,6*255),(24,28,32));draw=ImageDraw.Draw(contact)
for j,i in enumerate(selected):
    image=Image.open(folder/f'eye-{i:03}.png');image.thumbnail((400,225));x=j%3*400;y=j//3*255
    contact.paste(image,(x,y));draw.text((x+8,y+228),f"eye {i}: {results[i]['eye_delta_m']:+.5f}m",fill='white')
contact.save(folder/'contact.png')
report={'passed':passed if mode=='coverage' else None,'scope':'debug mode 14 actual water coverage and coplanar stripe rejection' if mode=='coverage' else 'informational floor color; warm reflections are not dry-floor failures','eyes':len(rows),'max_corner_error_px':max(float(x['corner_error_px']) for x in rows),'max_reflection_uv_error':max(float(x['reflection_uv_error']) for x in rows),'critical_roi_failures':([x for x in target if x['water_coverage']<=.999]+[x for x in coplanar if x['whole_green_fraction']>=.0001]) if mode=='coverage' else [],'samples':results}
(folder/'waterline-review.json').write_text(json.dumps(report,indent=2))
print(json.dumps({**{k:v for k,v in report.items() if k not in ['samples','critical_roi_failures']},'critical_roi_failure_count':len(report['critical_roi_failures']),'first_failures':report['critical_roi_failures'][:3]},indent=2))
if mode=='coverage' and not passed:raise SystemExit(1)
