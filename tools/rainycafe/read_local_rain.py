"""Inspect only the locally owned rain prefab/material dependency chain."""
from pathlib import Path
import tarfile,re,json
r=Path(__file__).resolve().parents[2]
dest=r/'art/RainyCafe/local-weather-review';dest.mkdir(parents=True,exist_ok=True)
with tarfile.open('D:/Unity/Plugins/COZY Stylized Weather 3 v3.6.12.unitypackage') as archive:
 lookup={}
 for member in archive:
  if member.isfile() and member.name.endswith('/pathname'):
   name=archive.extractfile(member).read().decode('utf-8-sig').splitlines()[0].rstrip('\x00')
   lookup[member.name.rsplit('/',1)[0]]=name
 pending=[key for key,name in lookup.items() if name.endswith(('/Rain.mat','/Rain Impact.mat','/Rain Mist.mat','/Light Rain.prefab','/Heavy Rain.prefab','/LICENSE.md','/package.json'))]
 visited=set()
 while pending:
  key=pending.pop()
  if key in visited:continue
  visited.add(key);name=lookup[key]
  if Path(name).suffix.lower() not in ('.png','.jpg','.tga','.mat','.prefab','.shader','.md','.json'):continue
  member=archive.getmember(key+'/asset');data=archive.extractfile(member).read()
  target=dest/name;target.parent.mkdir(parents=True,exist_ok=True);target.write_bytes(data)
  print(name,len(data))
  if target.suffix.lower() in ('.mat','.prefab','.md','.json'):
   text=data.decode('utf-8-sig',errors='replace')
   if target.suffix.lower() in ('.md','.json','.mat'):print(text[:6000])
   for guid in re.findall(r'guid:\s*([a-f0-9]{32})',text):
    if guid in lookup and guid not in visited:pending.append(guid)
