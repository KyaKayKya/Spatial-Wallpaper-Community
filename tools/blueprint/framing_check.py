"""Exercise numeric composition controls in the actual standalone Player."""
import argparse,json,os,subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[2]
p=argparse.ArgumentParser(description=__doc__)
p.add_argument('--manifest',type=Path,default=root/'Validation/Blueprint/framing-inputs/cases.json')
p.add_argument('--output',type=Path,required=True)
p.add_argument('--data-root',type=Path,required=True)
a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=True)
initial=next(row['path'] for row in json.loads(a.manifest.read_text(encoding='utf8')) if row['expected']=='supported')
command=[str(root/'Prototype/Builds/Blueprint/Blueprint.exe'),'-screen-fullscreen','0','-screen-width','1280','-screen-height','720','-logFile',str(out/'player.log'),'--blueprint',initial,'--blueprint-data-root',str(a.data_root.resolve()),'--blueprint-look','natural','--blueprint-hour','12','--blueprint-terrain','none','--blueprint-framing-check',str(out),'--blueprint-framing-manifest',str(a.manifest.resolve())]
raise SystemExit(subprocess.run(command,cwd=root/'Prototype',env=dict(os.environ,ALLUSERSPROFILE=os.environ.get('PROGRAMDATA','C:/ProgramData'))).returncode)
