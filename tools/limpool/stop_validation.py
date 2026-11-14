from pathlib import Path
import psutil
r=Path(__file__).resolve().parents[2]
for p in psutil.process_iter(['pid','name','cmdline']):
 try:
  if p.info['name'].lower().startswith('python') and any(x.replace('\\','/').endswith('tools/limpool/acceptance.py') for x in p.info['cmdline']) and Path(p.cwd()).resolve()==r:
   children=p.children(recursive=True);p.terminate()
   for child in children:child.terminate()
   psutil.wait_procs([p]+children,timeout=10);print('Stopped task validation process',p.pid)
 except (psutil.NoSuchProcess,psutil.AccessDenied):pass
