from pathlib import Path
import sys,json,psutil
r=Path(__file__).resolve().parents[2]
for arg in sys.argv[1:]:
 p=r/arg
 if p.is_dir():
  frames=sorted(p.glob('*.png'));print(arg,'frames',len(frames),'latest',frames[-1].name if frames else '-',flush=True)
 if p.is_file():
  text=p.read_text(encoding='utf-8',errors='replace')
  print('\nFILE',arg,'\n',text[-16000:])
for p in psutil.process_iter(['name','cmdline','memory_info']):
 try:
  if p.info['name'].lower() in ('blender.exe','rainycafe.exe'):
   print(p.pid,p.info['name'],round(p.info['memory_info'].rss/1048576),'MiB')
 except (psutil.AccessDenied,psutil.NoSuchProcess):pass
