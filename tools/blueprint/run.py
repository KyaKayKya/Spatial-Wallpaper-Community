"""Build and inspect the actual fixed-window blueprint Player."""
from pathlib import Path
import argparse,subprocess,os,sys
root=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser();p.add_argument('mode',choices=['build','preview','shot','capture','benchmark','video','editor-check','lighting-check','status']);p.add_argument('--input');p.add_argument('--look',default='warm');p.add_argument('--quality',default='1');p.add_argument('--hour',default='17.5');p.add_argument('--yaw',default='0');p.add_argument('--weather',default='clear');p.add_argument('--terrain',default='none');p.add_argument('--edit',action='store_true');p.add_argument('--name');p.add_argument('--width',default='1280');p.add_argument('--height',default='720');a=p.parse_args()
out=root/'Validation/Blueprint';out.mkdir(parents=True,exist_ok=True)
env=dict(os.environ,ALLUSERSPROFILE=os.environ.get('PROGRAMDATA','C:/ProgramData'))
if a.mode=='status':print('\n'.join((out/'build.log').read_text(encoding='utf8',errors='replace').splitlines()[-35:]));raise SystemExit()
if a.mode=='build':
 if not (root/'Prototype/Assets/Blueprint/LocalSky/CloudyCrownSkybox.shader').exists():subprocess.run([sys.executable,str(root/'tools/blueprint/prepare_sky.py')],check=True)
 subprocess.run([sys.executable,str(root/'tools/blueprint/build_importer.py')],check=True)
 subprocess.run([sys.executable,str(root/'tools/blueprint/prepare_meta.py')],check=True)
 command=['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(root/'Prototype'),'-executeMethod','BlueprintBuild.Build','-logFile',str(out/'build.log')]
else:
 folder=out/(a.name or a.mode);folder.mkdir(exist_ok=True)
 source=Path(a.input).resolve() if a.input else root/'galacticlunairy-cherryblossomcottage.zip'
 command=[str(root/'Prototype/Builds/Blueprint/Blueprint.exe'),'-screen-fullscreen','0','-screen-width',a.width,'-screen-height',a.height,'-logFile',str(folder/'player.log'),'--blueprint',str(source),'--blueprint-data-root',str(out/'local-data'),'--blueprint-look',a.look,'--blueprint-quality',a.quality,'--blueprint-hour',a.hour]
 command+=['--blueprint-yaw',a.yaw,'--blueprint-weather',a.weather,'--blueprint-terrain',a.terrain]
 if a.edit:command+=['--blueprint-edit','true']
 if a.mode!='preview':command+=['--blueprint-'+a.mode,str(folder)]
subprocess.run(command,cwd=root/'Prototype',env=env,check=True)
if a.mode=='build':
 log=(out/'build.log').read_text(encoding='utf8',errors='replace')
 errors=[line for line in log.splitlines() if 'error CS' in line or 'Exception:' in line]
 if errors:print('\n'.join(dict.fromkeys(errors)));raise SystemExit(1)
 print((out/'build-result.txt').read_text())
