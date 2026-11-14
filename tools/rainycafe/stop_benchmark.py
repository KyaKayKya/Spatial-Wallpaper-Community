import psutil
from pathlib import Path
r=Path(__file__).resolve().parents[2]
exe=(r/'Prototype/Builds/RainyCafe/RainyCafe.exe').resolve()
for p in psutil.process_iter(['name','exe','cmdline']):
 try:
  if p.info['name'].lower()=='rainycafe.exe' and Path(p.info['exe']).resolve()==exe and '--benchmark' in p.info['cmdline']:
   p.terminate();p.wait(15);print('Stopped own benchmark',p.pid)
 except (psutil.NoSuchProcess,psutil.AccessDenied):pass
