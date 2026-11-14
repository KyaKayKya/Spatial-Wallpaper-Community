"""Restore the plugin needed to compile the merged Limpool scene."""
import json,tarfile,subprocess,sys
from pathlib import Path
r=Path(__file__).resolve().parents[2]
out=r/'Validation/Limpool';out.mkdir(parents=True,exist_ok=True)
name='Volumetric Lights 2 v20.2.1'
with tarfile.open(Path('D:/Unity/Plugins')/(name+'.unitypackage')) as t:
 entries={m.name.rsplit('/',1)[0]:t.extractfile(m).read().decode('utf-8-sig').splitlines()[0].rstrip('\x00') for m in t.getmembers() if m.name.endswith('/pathname')}
(out/(name+'-index.json')).write_text(json.dumps(entries),encoding='utf-8')
subprocess.run([sys.executable,str(r/'tools/limpool/read_plugins.py')],cwd=r,check=True,stdout=subprocess.DEVNULL)
print('Restored shared Volumetric Lights URP dependency')
