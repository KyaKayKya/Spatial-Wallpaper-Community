"""Fetch the selected CC0 Poly Haven concrete maps and record provenance."""
import hashlib,json,urllib.request
from pathlib import Path
from PIL import Image,ImageOps
root=Path(__file__).resolve().parents[2]
out=root/'Prototype/Assets/Parking/Generated'
def get(url):
    request=urllib.request.Request(url,headers={'User-Agent':'SpatialWallpaper-local-art-import/1.0'})
    with urllib.request.urlopen(request,timeout=90) as response:return response.read()
asset='concrete_wall_004'
cached=Path(__file__).parent/'concrete-source.json'
if cached.exists() and (out/'PHConcrete_UnityMetalSmooth.png').exists():
    record=json.loads(cached.read_text())
    if all((out/m['file']).exists() and hashlib.sha256((out/m['file']).read_bytes()).hexdigest()==m['sha256'] for m in record['maps']):
        print('Verified cached CC0 concrete maps');raise SystemExit(0)
files=json.loads(get('https://api.polyhaven.com/files/'+asset))
print('Available map keys:',list(files))
manifest={'asset':asset,'source':'https://polyhaven.com/a/'+asset,'license':'CC0-1.0','license_url':'https://polyhaven.com/license','authors':['Charlotte Baglioni','Dario Barresi'],'physical_width_m':2,'maps':[]}
for kind,name in [('Diffuse','PHConcrete_diff.png'),('nor_gl','PHConcrete_nor_gl.png'),('Rough','PHConcrete_rough.png')]:
    item=files[kind]['1k']['png'];data=get(item['url'])
    if item.get('md5'):assert hashlib.md5(data).hexdigest()==item['md5']
    target=out/name;target.write_bytes(data)
    with Image.open(target) as im:im.verify()
    manifest['maps'].append({'file':name,'url':item['url'],'sha256':hashlib.sha256(data).hexdigest(),'size':len(data)})
r=Image.open(out/'PHConcrete_rough.png').convert('L');zero=Image.new('L',r.size,0)
Image.merge('RGBA',(zero,zero,zero,ImageOps.invert(r))).save(out/'PHConcrete_UnityMetalSmooth.png')
(Path(__file__).parent/'concrete-source.json').write_text(json.dumps(manifest,indent=2))
print('Verified CC0 concrete maps:',[m['file'] for m in manifest['maps']])
