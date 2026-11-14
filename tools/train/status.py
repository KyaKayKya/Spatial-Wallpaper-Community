from pathlib import Path
import psutil,time
root=Path(__file__).resolve().parents[2]
for rel in ['train-export.log','Validation/Train/build.log']:
 p=root/rel
 print(rel, time.ctime(p.stat().st_mtime) if p.exists() else 'missing')
 if p.exists():print('\n'.join(p.read_text(errors='replace').splitlines()[-12:]))
for p in psutil.process_iter(['pid','name','cpu_times','memory_info','cmdline']):
 if p.info['name'] and p.info['name'].lower() in ['blender.exe','unity.exe']:
  print(p.info['pid'],p.info['name'],round(p.info['memory_info'].rss/1024**3,2),'GB',p.info['cpu_times'],' '.join(p.info['cmdline']))
