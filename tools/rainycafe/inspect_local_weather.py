"""Read the local weather package index without importing its project settings."""
from pathlib import Path
import tarfile,json
r=Path(__file__).resolve().parents[2]
source=Path('D:/Unity/Plugins/COZY Stylized Weather 3 v3.6.12.unitypackage')
entries={}
with tarfile.open(source) as archive:
 for member in archive:
  if member.isfile() and member.name.endswith('/pathname'):
   name=archive.extractfile(member).read().decode('utf-8-sig').splitlines()[0].rstrip('\x00')
   if any(s in name.lower() for s in ('rain','splash','puddle','license','readme')):
    entries[member.name.rsplit('/',1)[0]]=name
for k,v in entries.items():print(k,v)
(r/'Validation/RainyCafe/local-weather-index.json').write_text(json.dumps(entries,indent=2))
