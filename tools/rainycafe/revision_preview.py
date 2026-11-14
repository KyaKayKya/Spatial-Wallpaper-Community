"""Rebuild revised source and capture a preview for visual review."""
import subprocess,sys
from pathlib import Path
r=Path(__file__).resolve().parents[2]
for action in ('export','build','stills'):
 print('START',action,flush=True)
 subprocess.run([sys.executable,str(r/'tools/rainycafe/run.py'),action],cwd=r,check=True)
 if action=='export':
  subprocess.run(['D:/Blender/blender.exe','-b','--python-exit-code','1','--python',str(r/'tools/rainycafe/verify_revision_blender.py')],cwd=r,check=True)
 print('DONE',action,flush=True)
