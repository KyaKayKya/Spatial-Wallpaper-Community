"""Render identical frozen-wave left/right eye sweeps, one component at a time."""
from pathlib import Path
import subprocess,sys
r=Path(__file__).resolve().parents[2]
for label,args in [('composite',[]),('reflection',['--water-debug','3']),('transmission',['--water-debug','2']),('specular',['--water-debug','6']),('normal',['--water-debug','7']),('fresnel',['--water-debug','8'])]:
    print('BEGIN',label,flush=True)
    subprocess.run([sys.executable,str(r/'tools/limpool/diagnostic.py'),'isolate-'+label,'--frames','300']+args,cwd=r,check=True)
