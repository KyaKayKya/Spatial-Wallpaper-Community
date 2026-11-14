"""Extract only the licensed Cloudy Crown sky dependencies from a local official package."""
from pathlib import Path
import argparse,tarfile,json,hashlib
root=Path(__file__).resolve().parents[2]
default=Path.home()/'AppData/Roaming/Unity/Asset Store-5.x/Borodar/Textures MaterialsSkies/Farland Skies - Cloudy Crown Pro.unitypackage'
p=argparse.ArgumentParser();p.add_argument('--package',type=Path,default=default);a=p.parse_args()
if not a.package.is_file():raise SystemExit('Download Cloudy Crown Pro with your Unity account, then pass --package FILE')
selected={'b39eba844529e304596997d76a5bab7e':'CloudyCrownSkybox.shader','eef65d496c7133349b3223df0850594e':'Clouds.png','8872a522d7da63c4095c44ca9bf0964b':'Stars.png','77f8724492f53bc4886576ec4f84921f':'Moon.png','74e412b1b4d56534baeb0961bac91684':'Sun.png'}
out=root/'Prototype/Assets/Blueprint/LocalSky';out.mkdir(exist_ok=True)
records=[]
with tarfile.open(a.package,'r:gz') as package:
 for guid,name in selected.items():
  source=package.extractfile(guid+'/asset').read();meta=package.extractfile(guid+'/asset.meta').read()
  # Use the ordinary material inspector; the vendor editor requires unrelated editor helpers.
  content=source.replace(b'    CustomEditor "CloudyCrownShaderGUI"',b'') if name.endswith('.shader') else source
  (out/name).write_bytes(content);(out/(name+'.meta')).write_bytes(meta)
  records.append({'file':name,'source_guid':guid,'source_sha256':hashlib.sha256(source).hexdigest(),'bytes':len(content)})
(out/'source.json').write_text(json.dumps({'package_sha256':hashlib.sha256(a.package.read_bytes()).hexdigest(),'files':records},indent=2),encoding='utf8')
print('Sky runtime source bytes',sum(f['bytes'] for f in records),'(before Unity texture compression)')
