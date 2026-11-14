"""Restore only known unrelated Unity import side effects in this task worktree."""
from pathlib import Path
import subprocess
r=Path(__file__).resolve().parents[2]
deleted=subprocess.check_output(['git','ls-files','--deleted','--','Prototype/Assets/MiniShotGame'],cwd=r,text=True).splitlines()
paths=[x for x in deleted if x.endswith('.meta')]
paths+=['Prototype/Assets/Showcase/Settings/ShowcaseURP.asset','Prototype/ProjectSettings/GraphicsSettings.asset','Prototype/ProjectSettings/QualitySettings.asset']
subprocess.run(['git','restore','--']+paths,cwd=r,check=True)
for rel in ['Prototype/Assets/Limpool/Scenes/Limpool.unity','Prototype/Assets/Limpool/Settings/Appearance.asset']:
    p=r/rel;s=p.read_text();p.write_text('\n'.join(x.rstrip() for x in s.splitlines())+'\n')
