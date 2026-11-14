"""Run final captures or matched performance cases sequentially, without GPU contention."""
import subprocess,sys
from pathlib import Path
r=Path(__file__).resolve().parents[2]
cases=['stills','stills-wide','stills-tall','stills-no-shadows','stills-legacy-rain','hold','video','encode'] if sys.argv[1]=='captures' else ['bench-full','bench-static','bench-no-shadows']
for case in cases:
 print('START',case,flush=True)
 subprocess.run([sys.executable,str(r/'tools/rainycafe/run.py'),case],cwd=r,check=True)
 print('DONE',case,flush=True)
for script in (['verify_capture.py','check_visual_evidence.py'] if sys.argv[1]=='captures' else ['summarize_performance.py']):
 subprocess.run([sys.executable,str(r/'tools/rainycafe'/script)],cwd=r,check=True)
if sys.argv[1]=='captures':
 for name,args in [('capture_light_audit.py',[]),('check_light_audit.py',['after']),('check_candle_energy.py',[]),('check_candle_stability.py',['after']),('inspect_candle_flicker.py',['hold']),('check_chair_shadow.py',[]),('edge_evidence.py',['after'])]:
  print('CHECK',name,flush=True)
  subprocess.run([sys.executable,str(r/'tools/rainycafe'/name),*args],cwd=r,check=True)
