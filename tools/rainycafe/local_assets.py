"""Preserve and restore licensed RainyCafe payloads with SHA-256 checks."""
import argparse,hashlib,json,shutil,subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('action',choices=['snapshot','restore','check']);p.add_argument('--destination',type=Path)
args=p.parse_args();a=args.action
assert a!='snapshot' or args.destination is None, 'Snapshots always read the current worktree'
destination=(root/args.destination).resolve() if args.destination else root
def digest(path):
    h=hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda:stream.read(8*1024*1024),b''):h.update(block)
    return h.hexdigest()
common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip())
pointer=Path(__file__).with_name('local-assets.json')
if a=='snapshot':
    generated=root/'Prototype/Assets/RainyCafe/Generated'
    export=json.loads((generated/'manifest.json').read_text())
    files=[generated/f for f in export['files']]
    files += [root/'art/RainyCafe/rainycafe_production.blend',root/'art/RainyCafe/rainycafe_bake.blend']
    files += sorted((root/'Prototype/Assets/RainyCafe/Meshes').glob('*LocalShadows.asset'))
    files += sorted((root/'Prototype/Assets/RainyCafe/Meshes').glob('*CandleCopy[0-9].asset'))
    files += sorted(f for f in (root/'Prototype/Assets/RainyCafe/LocalWeather').rglob('*') if f.is_file())
    files += [f for f in (root/'art/RainyCafe/RainyCafe_Complete_Project').rglob('*') if f.is_file()]
    hashes={f.relative_to(root).as_posix():digest(f) for f in files}
    data={'bundle':hashlib.sha256(json.dumps(hashes,sort_keys=True).encode()).hexdigest()[:16],'files':hashes}
else:data=json.loads(pointer.read_text())
store=common.parent/'.local-assets/rainycafe'/data['bundle']
pending=[]
for name,expected in data['files'].items():
    rel=Path(name);assert not rel.is_absolute() and '..' not in rel.parts
    source,target=(root/rel,store/rel) if a=='snapshot' else (store/rel,destination/rel)
    assert source.is_file() and digest(source)==expected,source
    if target.exists():
        assert target.is_file(), target
        assert digest(target)==expected or (target.suffix=='.meta' and
            source.read_bytes().replace(b'\r\n',b'\n')==target.read_bytes().replace(b'\r\n',b'\n')), target
    elif a=='check':raise AssertionError('Not restored: '+str(target))
    else:pending.append((source,target))
for source,target in pending:
    target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
    assert digest(target)==data['files'][source.relative_to(root if a=='snapshot' else store).as_posix()]
if a=='snapshot':pointer.write_text(json.dumps(data,indent=2))
print('PASS:',a,data['bundle'],len(data['files']),'files; source and destination hashes verified')
