"""Save/restore the machine-local Parking texture bundle, with SHA-256 verification."""
import argparse, hashlib, json, shutil, subprocess, sys
from pathlib import Path

ROOT=Path(__file__).resolve().parents[2]
common=Path(subprocess.check_output(['git','-C',str(ROOT),'rev-parse','--path-format=absolute','--git-common-dir'],text=True).strip())
STORE=common.parent/'.local-assets/parking/20261018-pbr-v2'
MANIFEST=Path(__file__).with_name('local-assets.json')
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
    p=argparse.ArgumentParser(description=__doc__);p.add_argument('action',choices=['snapshot','restore','check']);args=p.parse_args()
    if args.action=='snapshot':
        files=sorted((ROOT/'Prototype/Assets/Parking/Generated').glob('*.png'))+[ROOT/'Prototype/Assets/Parking/Generated/scene.json']
        data={'schema':1,'bundle':'20261018-pbr-v2','files':[{'path':f.relative_to(ROOT).as_posix(),'size':f.stat().st_size,'sha256':digest(f)} for f in files]}
        saved=STORE/'manifest.json'
        if saved.exists() and json.loads(saved.read_text())!=data:raise ValueError('Existing bundle differs; create a new bundle version')
        for f in files:
            target=STORE/'payload'/f.relative_to(ROOT)
            if target.exists() and digest(target)!=digest(f):raise ValueError('Bundle payload differs: '+str(target))
            target.parent.mkdir(parents=True,exist_ok=True)
            if not target.exists():shutil.copy2(f,target)
        text=json.dumps(data,indent=2);saved.write_text(text);MANIFEST.write_text(text)
    data=json.loads(MANIFEST.read_text());assert data==json.loads((STORE/'manifest.json').read_text())
    count=0
    for item in data['files']:
        rel=Path(item['path']);assert not rel.is_absolute() and '..' not in rel.parts
        source=STORE/'payload'/rel;target=ROOT/rel
        assert source.stat().st_size==item['size'] and digest(source)==item['sha256'],str(source)
        if not target.exists() and args.action=='restore':target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
        assert target.exists() and digest(target)==item['sha256'],str(target)
        count+=1
    print(f'PASS: {count} Parking assets verified; bundle={STORE}')
if __name__=='__main__':main()
