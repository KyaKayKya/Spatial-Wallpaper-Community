"""Download a pinned, MIT-licensed URP rain sample from its author's repository."""
from pathlib import Path
import json,hashlib,subprocess
r=Path(__file__).resolve().parents[2]
dest=r/'art/RainyCafe/free-rain-urp'
revision='3006185c2f697a33be4ffe2d909e4140afbeae3c'
if not (dest/'.git').exists():
 subprocess.run(['git','clone','--depth','1','--filter=blob:none','--sparse','https://github.com/daniel-ilett/rain-urp.git',str(dest)],check=True,cwd=r)
if subprocess.check_output(['git','rev-parse','HEAD'],cwd=dest,text=True).strip()!=revision:
 subprocess.run(['git','fetch','--depth','1','origin',revision],check=True,cwd=dest)
 subprocess.run(['git','checkout','--detach',revision],check=True,cwd=dest)
subprocess.run(['git','sparse-checkout','set','Assets/VFX','Assets/Textures','Assets/Shaders','Assets/Materials'],check=True,cwd=dest)
files={}
for target in sorted(dest.rglob('*')):
 name=target.relative_to(dest).as_posix()
 if not target.is_file() or not (name in ('LICENSE','README.md') or name.startswith(('Assets/VFX/','Assets/Textures/','Assets/Shaders/','Assets/Materials/'))):continue
 if name.endswith('.afdesign') or name.endswith('.afdesign.meta'):continue
 data=target.read_bytes()
 files[name]=hashlib.sha256(data).hexdigest()
 print(name,len(data),flush=True)
record={'url':'https://github.com/daniel-ilett/rain-urp','revision':revision,'license':'MIT','files':files}
(r/'Validation/RainyCafe/free-rain-source.json').write_text(json.dumps(record,indent=2))
print('Pinned revision',revision,'files',len(files))
