"""Stop only this task's background Blender export before changing its geometry."""
import psutil
from pathlib import Path
root=Path(__file__).resolve().parents[2]
for p in psutil.process_iter(['name','cmdline','cwd']):
    if (p.info['name'] or '').lower()=='blender.exe' and any('export_scene.py' in a for a in (p.info['cmdline'] or [])) and Path(p.info['cwd']).resolve()==root:
        print('Stopping task export PID',p.pid)
        p.terminate();p.wait(timeout=15)
