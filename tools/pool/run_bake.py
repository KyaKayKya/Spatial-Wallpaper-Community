import os,subprocess,zipfile,shutil
from pathlib import Path
root=Path(__file__).resolve().parents[2]
common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip()).parent
dest=root/'.local-assets/pool';dest.mkdir(parents=True,exist_ok=True)
with zipfile.ZipFile(next((common/'art/Pool').glob('*.zip'))) as z:
 (dest/'approved_pool.blend').write_bytes(z.read('Poolcore_Static_v2/approved_pool.blend'))
out=root/'Validation/Pool';out.mkdir(parents=True,exist_ok=True)
env=os.environ.copy();env['ALLUSERSPROFILE']='C:/ProgramData'
with (out/'cycles-bake.log').open('w') as log:
 result=subprocess.run(['D:/Blender/blender.exe','-b','--python',str(root/'tools/pool/bake.py')],stdout=log,stderr=subprocess.STDOUT,env=env)
print('Blender exit',result.returncode)
if result.returncode==0:
 raw=out/'bake-raw';raw.mkdir(exist_ok=True)
 for path in (root/'Prototype/Assets/Pool/Baked').glob('*.exr'):
  if not path.name.startswith('Optional'):shutil.copy2(path,raw/path.name)
 with (out/'denoise.log').open('w') as log:
  result=subprocess.run(['D:/Blender/blender.exe','-b','--python',str(root/'tools/pool/denoise.py')],stdout=log,stderr=subprocess.STDOUT,env=env)
 print('Denoise exit',result.returncode)
raise SystemExit(result.returncode)
