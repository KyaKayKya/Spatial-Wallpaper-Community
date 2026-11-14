"""Compare real Player day/night sky and a fixed shadow-facing cottage wall."""
from pathlib import Path
import argparse,subprocess,json,os
from PIL import Image,ImageStat
root=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser();p.add_argument('--exe',type=Path,default=root/'Prototype/Builds/Blueprint/Blueprint.exe');p.add_argument('--name',default='daynight');p.add_argument('--full',action='store_true');a=p.parse_args()
out=root/'Validation/Blueprint'/a.name;out.mkdir(parents=True,exist_ok=True)
def luminance(image,box):
 rgb=ImageStat.Stat(image.crop(box)).mean
 return sum(x*w for x,w in zip(rgb,[.2126,.7152,.0722]))
results={}
samples=[('day',12),('dusk',18.5),('night',0)]
if a.full:samples += [('horizon-'+str(hour),hour) for hour in [5.99,6,6.01,17.99,18,18.01]]
for name,hour in samples:
 folder=out/name;folder.mkdir(exist_ok=True)
 command=[str(a.exe),'-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',str(folder/'player.log'),'--blueprint',str(root/'galacticlunairy-cherryblossomcottage.zip'),'--blueprint-data-root',str(out/'data'),'--blueprint-look','natural','--blueprint-quality','1','--blueprint-hour',str(hour),'--blueprint-yaw','20','--blueprint-terrain','none','--blueprint-weather','clear','--blueprint-shot',str(folder)]
 subprocess.run(command,cwd=a.exe.parent,env=dict(os.environ,ALLUSERSPROFILE=os.environ.get('PROGRAMDATA','C:/ProgramData')),check=True)
 image=Image.open(folder/'front.png').convert('RGB')
 lighting=json.loads((folder/'lighting.json').read_text(encoding='utf-8-sig'))
 sun=next(l for l in lighting['lights'] if l['name']=='Template sun and moon')
 bright=sum(1 for rgb in image.crop((0,0,180,160)).getdata() if sum(x*w for x,w in zip(rgb,[.2126,.7152,.0722]))>30)
 results[name]={'hour':hour,'sky_luminance':luminance(image,(0,0,180,160)),'shadow_wall_luminance':luminance(image,(315,310,405,470)),'main_light_intensity':sun['intensity'],'bright_sky_pixels':bright}
cases={'night_sky_darkens':results['night']['sky_luminance']<results['day']['sky_luminance']*.35,'day_shadow_detail_readable':results['day']['shadow_wall_luminance']>=55,'night_wall_darker_than_day':results['night']['shadow_wall_luminance']<results['day']['shadow_wall_luminance']*.7}
cases.update(dusk_sky_dimmed=results['dusk']['sky_luminance']<results['day']['sky_luminance']*.65,night_stars_visible=results['night']['bright_sky_pixels']>=3,night_main_light_dimmed=results['night']['main_light_intensity']<.05)
if a.full:cases['day_moon_horizon_has_no_intensity_spike']=all(v['main_light_intensity']<.001 for k,v in results.items() if k.startswith('horizon-'))
report={'pass':all(cases.values()),'standalone':True,'simulated_eye_only':True,'resolution':'1280x720','cases':cases,'measurements':results}
(out/'checks.json').write_text(json.dumps(report,indent=2),encoding='utf8');print(json.dumps(report,indent=2));raise SystemExit(0 if report['pass'] else 2)
