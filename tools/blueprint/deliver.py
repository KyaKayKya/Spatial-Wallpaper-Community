"""Preserve a private local Windows delivery without changing the main checkout."""
from pathlib import Path
import argparse,hashlib,json,shutil,subprocess,uuid,os
from PIL import Image
root=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve()
if out.exists():raise SystemExit('Use a new delivery directory')
if root==out or root in out.parents:raise SystemExit('Delivery must survive worktree removal')
out.mkdir(parents=True)
shutil.copytree(root/'Prototype/Builds/Library',out/'App')
shutil.copytree(root/'tracker',out/'App/Tracker',ignore=shutil.ignore_patterns('.venv','__pycache__','.pytest_cache'))
shutil.copytree(root/'Prototype/Builds/Blueprint',out/'Preview')
data=out/'Data/desktop/blueprints';data.mkdir(parents=True)
source=root/'Validation/Blueprint/local-data';index=json.loads((source/'index.json').read_text(encoding='utf-8-sig'))
original=next(iter(index['revisions'].values()));source_hash=original['source']
(data/'sources').mkdir();shutil.copy2(source/'sources'/(source_hash+'.blueprint'),data/'sources'/(source_hash+'.blueprint'))
state={'schema':1,'works':{},'revisions':{}};ids=[]
for name,look,hour,cover in [('Cherry Cottage - Natural','natural',12,'final-day'),('Cherry Cottage - Warm Dusk','warm',18.2,'final-hero'),('Cherry Cottage - Night','warm',0,'final-night')]:
 work=uuid.uuid4().hex;revision=uuid.uuid4().hex;settings=dict(original['settings']);settings.update(Look=look,Hour=hour,TimeMode='fixed',CycleMinutes=20,Terrain='none',Yaw=20,Scale=1,OffsetZ=0,WarmWindows=True,Fireflies=False)
 state['works'][work]={'name':name,'latest':revision};state['revisions'][revision]={'work':work,'source':source_hash,'settings':settings};ids.append('blueprint-'+work+'-'+revision)
 (data/'covers').mkdir(exist_ok=True)
 image=Image.open(root/'Validation/Blueprint'/cover/'front.png');image.resize((512,288)).save(data/'covers'/(work+'-'+revision+'.png'))
(data/'index.json').write_text(json.dumps(state,ensure_ascii=False),encoding='utf8')
wrapper=out/'Start.cs'
wrapper.write_text('''using System;using System.Diagnostics;using System.IO;using System.Text;using System.Windows.Forms;
class Start { [STAThread]static void Main(){string d=AppDomain.CurrentDomain.BaseDirectory;try{var p=new ProcessStartInfo(Path.Combine(d,"App/Launch.exe"));p.UseShellExecute=false;p.WorkingDirectory=Path.Combine(d,"App");p.Arguments="--desktop-data-root "+Q(Path.Combine(d,"Data/desktop"))+" --data-root "+Q(Path.Combine(d,"Data/calibration"));Process.Start(p);}catch(Exception e){MessageBox.Show(e.ToString());}}static string Q(string s){return "\\\""+s.Replace("\\\"","\\\\\\\"")+"\\\"";}}
''',encoding='utf8')
compiler=Path(os.environ['WINDIR'])/'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
subprocess.run([str(compiler),'/nologo','/target:winexe','/reference:System.Windows.Forms.dll','/out:'+str(out/'Start.exe'),str(wrapper)],check=True)
evidence=out/'Evidence';evidence.mkdir()
for name in ['final-hero','final-day','final-night','daynight','baseline-daynight','final-review-check','review-final-geometry','lights-balanced','lights-q0','lights-q2','final-video','final-desktop','weather-clear','weather-cloudy','weather-rain','weather-snow']:
 target=evidence/name;target.mkdir()
 for f in (root/'Validation/Blueprint'/name).iterdir():
  if f.is_file() and not(name=='final-video' and f.suffix=='.png'):shutil.copy2(f,target/f.name)
shutil.copytree(root/'Validation/Blueprint/performance',evidence/'performance',ignore=shutil.ignore_patterns('*.png'))
shutil.copy2(root/'Validation/Desktop/build-receipt.json',evidence/'build-receipt.json')
shutil.copy2(root/'Validation/Blueprint/weather-checks.json',evidence/'weather-checks.json')
licenses=out/'Licenses';licenses.mkdir();assets=root/'Prototype/Assets/Blueprint/Resources/Blueprint'
for name in ['LICENSE.txt','SOURCES.json']:shutil.copy2(assets/name,licenses/name)
shutil.copy2(assets/'Sky/SOURCES.json',licenses/'Sky-SOURCES.json')
shutil.copy2(root/'Prototype/Assets/Blueprint/LocalSky/source.json',licenses/'Sky-import.json')
shutil.copytree(root/'Prototype/Assets/Blueprint/ThirdParty',licenses/'ThirdParty')
shutil.copy2(root/'galacticlunairy-cherryblossomcottage.zip',out/'Private-source-Cherry-Cottage.zip')
(out/'README.txt').write_text('Private local Minecraft blueprint wallpaper delivery.\nStart.exe opens the existing wallpaper library with three local cottage presets. Import other supported blueprints from the library.\nData is isolated in this directory. Tracking uses the existing main checkout tracker virtual environment configured in App/Launch.exe.config.\nNo real camera was connected during verification. Camera calibration remains required for real tracked desktop use.\nCommunity cottage source and its screenshots are private: do not redistribute this directory. Texture attribution and licenses are in Licenses.\nPreview/Blueprint.exe is an optional standalone viewer; use --blueprint with a supported local input.\n',encoding='utf8')
manifest={str(f.relative_to(out)):hashlib.sha256(f.read_bytes()).hexdigest() for f in out.rglob('*') if f.is_file()}
(out/'manifest.json').write_text(json.dumps({'commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=root,text=True).strip(),'artwork_ids':ids,'bytes':sum(f.stat().st_size for f in out.rglob('*') if f.is_file()),'files':manifest},indent=2),encoding='utf8')
print(out);print('MiB',round(sum(f.stat().st_size for f in out.rglob('*') if f.is_file())/1048576,2));print(ids)
