from pathlib import Path
import hashlib,json,re,subprocess
import numpy as np
from PIL import Image
r=Path(__file__).resolve().parents[2];out=r/'Validation/Limpool'
def sha(p):
    h=hashlib.sha256()
    with p.open('rb') as f:
        for b in iter(lambda:f.read(4*1024*1024),b''):h.update(b)
    return h.hexdigest()
build={str(p.relative_to(r/'Prototype/Builds/Limpool')):sha(p) for p in (r/'Prototype/Builds/Limpool').rglob('*') if p.is_file()}
(out/'build-files.json').write_text(json.dumps(build,indent=2))
fingerprint=hashlib.sha256(json.dumps(build,sort_keys=True).encode()).hexdigest()
stability=json.loads((out/'reflection-stability.json').read_text())
assert stability.get('pass') and stability.get('build_fingerprint')==fingerprint,'Reflection regression is missing, failed, or belongs to another build'
final_runs=['stills-default','stills-low','hold-default','video-default']+['bench-'+q for q in ['default','no-volume','no-waterlight','reflection-low','no-reflection','low']]
for run in final_runs:
    p=out/run/'run.json';d=json.loads(p.read_text())
    assert d.get('build_fingerprint')==fingerprint,'Different build: '+run
    d['unchanged_build_post_run_fingerprint']=fingerprint;p.write_text(json.dumps(d,indent=2))
export=json.loads((out/'export.json').read_text());original=r/'art/limpool/source/Scene/Liminal_Pool.blend'
assert sha(original)==export['source_sha256'],'Original Blender changed'
logs=[out/'build.log']+[out/run/'player.log' for run in final_runs]
errors={str(p.relative_to(out)):re.findall(r'^.*(?:Shader error|ERROR:|NullReferenceException|MissingReferenceException|InvalidOperationException).*$' ,p.read_text(errors='replace'),re.M) for p in logs}
assert not any(errors.values()),errors
motion={}
a=np.asarray(Image.open(out/'hold-default/0000.png')).astype(float)
b=np.asarray(Image.open(out/'hold-default/0600.png')).astype(float)
for name,(x0,y0,x1,y1) in {'water':(200,650,1450,880),'wall':(1250,400,1550,640),'beam':(750,230,1100,410),'architecture_control':(50,60,350,150)}.items():
    delta=np.abs(a[y0:y1,x0:x1]-b[y0:y1,x0:x1]);motion[name]={'mean_abs_rgb_255':float(delta.mean()),'p95_abs_rgb_255':float(np.quantile(delta,.95))}
videos={}
for p in out.glob('*.mp4'):
    info=json.loads(subprocess.check_output(['ffprobe','-v','error','-show_entries','stream=width,height,r_frame_rate,nb_frames:format=duration','-of','json',str(p)],text=True));videos[p.name]=info
plugins={p.name:sha(p) for p in Path('D:/Unity/Plugins').glob('*.unitypackage') if p.name in ['URP Water 1.2.2.unitypackage','Volumetric Lights 2 v20.2.1.unitypackage','Bakery - GPU Lightmapper v1.98.unitypackage']}
report={'source_unchanged':True,'closed_structures':len(export['objects']),'build_fingerprint':fingerprint,'runtime_errors':errors,'fixed_eye_motion_20_seconds':motion,'videos':videos,'plugins':plugins,'notes':['Pixel differences show animation, not physical accuracy.','Build files hashed after runs; no rebuild occurred during the runs.']}
(out/'audit.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2))
