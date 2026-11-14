"""Hash-verified local Train payload preservation and restoration."""
import argparse,hashlib,json,shutil,subprocess
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
GEN=ROOT/'Prototype/Assets/Train/Generated'
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
p=argparse.ArgumentParser(description=__doc__);p.add_argument('action',choices=['snapshot','restore','check']);a=p.parse_args().action
common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip())
pointer=Path(__file__).with_name('local-assets.json')
if a=='snapshot':
 manifest=json.loads((GEN/'manifest.json').read_text())
 data={'bundle':digest(GEN/'manifest.json')[:16],'files':{f:digest(GEN/f) for f in manifest['files']}}
 authoring=ROOT/'art/Train/train_compartment_complete.blend'
 if authoring.exists():data['authoring']={'name':authoring.name,'sha256':digest(authoring)}
else:data=json.loads(pointer.read_text())
store=common.parent/'.local-assets/train'/data['bundle']
if a=='snapshot':store.mkdir(parents=True,exist_ok=True)
for name,expected in data['files'].items():
 assert Path(name).name==name
 source,target=(GEN/name,store/name) if a=='snapshot' else (store/name,GEN/name)
 assert source.exists() and digest(source)==expected,source
 if not target.exists() and a in ['snapshot','restore']:
  target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
 assert target.exists() and digest(target)==expected,target
if 'authoring' in data:
 item=data['authoring'];assert Path(item['name']).name==item['name']
 authored=ROOT/'art/Train'/item['name'];saved=store/item['name']
 source,target=(authored,saved) if a=='snapshot' else (saved,authored)
 assert source.exists() and digest(source)==item['sha256'],source
 if not target.exists() and a in ['snapshot','restore']:
  target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
 assert target.exists() and digest(target)==item['sha256'],target
if a=='snapshot':pointer.write_text(json.dumps(data,indent=2));shutil.copy2(GEN/'manifest.json',store/'manifest.json')
print('PASS: Train payload',data['bundle'],len(data['files']),'files')
