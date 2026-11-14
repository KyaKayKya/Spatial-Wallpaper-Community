"""Fetch pinned upstream sources into ignored research cache (not game assemblies)."""
from pathlib import Path
import requests,hashlib,zipfile,io,json
root=Path(__file__).resolve().parents[2];cache=root/'.local-assets/blueprint-upstream';cache.mkdir(parents=True,exist_ok=True)
sources=[
 ('fnbt-v1.0.0','https://codeload.github.com/mstefarov/fNbt/zip/578b9b089905927d69c2c8852dbc54806e236f0f','c91788a5962770c6b23afcd882e43b1ed2ffd9a3eb5dbb4236acc887d0b625ed9bcdb2847ab9e6769ece6036a9ae051001c4325882f35720c273044421eb4bab'),
 ('pixel-perfection','https://cdn.modrinth.com/data/6w3F4SEu/versions/8HUXdaIC/Pixel%20Perfection%20Legacy%2013.21.3.zip','013e529249306ce1df8f0b569a07c1391d34cd0b8e03a38c29ed8b01a4ed2704a6a99d188cfd52846cb5e7dd5d6ca6c409c31372d72a8f2010f5627bde33b4a3')]
records=[]
for name,url,expected in sources:
 p=cache/(name+'.zip')
 if not p.exists():r=requests.get(url,timeout=120);r.raise_for_status();p.write_bytes(r.content)
 data=p.read_bytes()
 if expected and hashlib.sha512(data).hexdigest()!=expected:raise ValueError('digest mismatch '+name)
 with zipfile.ZipFile(io.BytesIO(data)) as z:
  names=z.namelist();licenses=[n for n in names if any(k in n.lower() for k in ['license','credits','readme','permission'])]
  print(name,'bytes',len(data),'files',len(names),'license files',licenses[:15])
  for n in licenses[:15]:
   if not n.endswith('/') and z.getinfo(n).file_size<30000:
    text=z.read(n).decode('utf8',errors='replace');(cache/(name+'-'+Path(n).name)).write_text(text,encoding='utf8');print(n,ascii(text[:1000]))
  if name.startswith('fnbt'):
   target=root/'Prototype/Assets/Blueprint/ThirdParty/fNbt';target.mkdir(parents=True,exist_ok=True)
   for old in target.rglob('*.cs'):
    assert root.resolve() in old.resolve().parents
    old.unlink()
   for n in names:
    if '/fNbt/' in n and n.endswith('.cs') and '/Properties/' not in n:
     rel=n.split('/fNbt/',1)[1];dest=target/rel;dest.parent.mkdir(parents=True,exist_ok=True);dest.write_bytes(z.read(n))
   license_file=next(n for n in names if Path(n).name.lower() in ['license.txt','license']);(target/'LICENSE.txt').write_bytes(z.read(license_file))
 records.append({'name':name,'url':url,'bytes':len(data),'sha256':hashlib.sha256(data).hexdigest(),'sha512':hashlib.sha512(data).hexdigest()})
(cache/'sources.json').write_text(json.dumps(records,indent=2),encoding='utf8')
print('sources',records)
