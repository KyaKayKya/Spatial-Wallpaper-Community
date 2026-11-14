"""Restore only known unrelated files changed by this isolated worktree's Unity import."""
import subprocess
from pathlib import Path
r=Path(__file__).resolve().parents[2]
deleted=subprocess.check_output(['git','diff','--name-only','--diff-filter=D','-z'],cwd=r).decode().split('\0')
allowed=('Prototype/Assets/MiniShotGame/','Prototype/Assets/Parking/','Prototype/Assets/Train/','Prototype/Assets/Limpool/')
paths=[p for p in deleted if p.endswith('.meta') and p.startswith(allowed)]
paths.append('Prototype/Packages/packages-lock.json')
subprocess.run(['git','restore','--']+paths,cwd=r,check=True)
temporary=r/'inspect_materials_tmp.py'
if temporary.is_file():temporary.unlink()
print('Restored',len(paths),'known import side effects; removed own temporary inspection script')
