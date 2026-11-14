"""Sequential Player captures and measurements; never benchmark beside another render."""
from pathlib import Path
import subprocess,sys,json
r=Path(__file__).resolve().parents[2]
for mode,quality in [('hold','default'),('video','default'),('stills','low')]+[('bench',q) for q in ['default','no-volume','no-waterlight','reflection-low','no-reflection','low']]:
    print('BEGIN',mode,quality,flush=True)
    subprocess.run([sys.executable,str(r/'tools/limpool/player.py'),mode,quality],cwd=r,check=True)
subprocess.run([sys.executable,str(r/'tools/limpool/summarize.py')],cwd=r,check=True)
