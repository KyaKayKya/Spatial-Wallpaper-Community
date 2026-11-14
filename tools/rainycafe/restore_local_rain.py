"""Restore the locally owned COZY particle prefabs without its weather controller."""
from pathlib import Path
import tarfile,re,json,hashlib
r=Path(__file__).resolve().parents[2]
source=Path('D:/Unity/Plugins/COZY Stylized Weather 3 v3.6.12.unitypackage')
root=r/'Prototype/Assets/RainyCafe/LocalWeather'
with tarfile.open(source) as archive:
 lookup={}
 for member in archive:
  if member.isfile() and member.name.endswith('/pathname'):
   name=archive.extractfile(member).read().decode('utf-8-sig').splitlines()[0].rstrip('\x00')
   lookup[member.name.rsplit('/',1)[0]]=name
 pending=[key for key,name in lookup.items() if name.endswith(('/Rain.mat','/Rain Impact.mat','/Light Rain.prefab','/LICENSE.md'))]
 visited=set();record={}
 while pending:
  key=pending.pop()
  if key in visited:continue
  visited.add(key);name=lookup[key]
  if Path(name).suffix.lower() not in ('.png','.jpg','.tga','.mat','.prefab','.md'):continue
  data=archive.extractfile(key+'/asset').read()
  rel=Path(name).relative_to('Packages/com.distantlands.cozy.core')
  assert '..' not in rel.parts and not rel.is_absolute()
  target=root/rel;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
  meta=archive.extractfile(key+'/asset.meta').read();Path(str(target)+'.meta').write_bytes(meta)
  record[target.relative_to(r).as_posix()]=hashlib.sha256(data).hexdigest()
  if target.suffix.lower() in ('.mat','.prefab'):
   for guid in re.findall(r'guid:\s*([a-f0-9]{32})',data.decode('utf-8-sig',errors='replace')):
    if guid in lookup and guid not in visited:pending.append(guid)
(r/'Validation/RainyCafe/local-rain-source.json').write_text(json.dumps({'source':str(source),'sha256':hashlib.sha256(source.read_bytes()).hexdigest(),'license':'Unity Asset Store EULA; local user-provided package','files':record},indent=2))
print('Restored',len(record),'licensed files; no weather controller or project settings imported')
