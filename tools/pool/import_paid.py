"""Restore purchased packages locally, preserving GUIDs without committing vendor files."""
from pathlib import Path
import tarfile,hashlib,json,gzip
root=Path(__file__).resolve().parents[2]
project=root/'Prototype'
records=[]
for name in ['URP Water 1.2.2.unitypackage','Bakery - GPU Lightmapper v1.98.unitypackage']:
 source=Path('D:/Unity/Plugins')/name
 with gzip.open(source,'rb') as stream,tarfile.open(fileobj=stream,mode='r|') as bundle:
  members={m.name:bundle.extractfile(m).read() for m in bundle if m.isfile()}
  count=0
  for key,member in members.items():
   if not key.endswith('/pathname'):continue
   path=member.decode('utf-8-sig').splitlines()[0].rstrip('\x00')
   rel=Path(path)
   if rel.is_absolute() or '..' in rel.parts or rel.parts[0]!='Assets':raise ValueError(path)
   dest=project/rel;prefix=key.rsplit('/',1)[0]
   asset=members.get(prefix+'/asset');meta=members.get(prefix+'/asset.meta')
   if asset is not None:
    dest.parent.mkdir(parents=True,exist_ok=True);data=asset
    if dest.exists() and dest.read_bytes()!=data:raise ValueError('Refusing overwrite: '+str(dest))
    dest.write_bytes(data);count+=1
   else:dest.mkdir(parents=True,exist_ok=True)
   if meta:Path(str(dest)+'.meta').write_bytes(meta)
 records.append({'package':name,'sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'assets':count})
(root/'Validation/Pool/paid-import.json').write_text(json.dumps(records,indent=2))
print(json.dumps(records,indent=2))
