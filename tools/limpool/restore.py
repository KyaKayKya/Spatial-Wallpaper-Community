"""Restore missing local payload; never overwrite differing files or extract arbitrary paths."""
from pathlib import Path,PurePosixPath
import subprocess,sys,zipfile,hashlib,json
r=Path(__file__).resolve().parents[2]
common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=r,text=True).strip())
source=Path(sys.argv[1]) if len(sys.argv)>1 else common.parent/'.local-assets/limpool/limpool-assets.zip'
with zipfile.ZipFile(source) as z:
    manifest=json.loads(z.read('limpool-manifest.json'))
    for name,record in manifest.items():
        path=PurePosixPath(name)
        if path.is_absolute() or '..' in path.parts or not name.startswith(('Prototype/Assets/Limpool/','art/limpool/')):raise ValueError(name)
        data=z.read(name)
        if hashlib.sha256(data).hexdigest()!=record['sha256']:raise ValueError('Hash mismatch: '+name)
        out=r/path
        if out.exists() and out.read_bytes()!=data:raise ValueError('Refusing overwrite: '+name)
    for name in manifest:
        out=r/PurePosixPath(name);out.parent.mkdir(parents=True,exist_ok=True)
        if not out.exists():out.write_bytes(z.read(name))
print('Verified/restored',len(manifest),'local files')
