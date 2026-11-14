"""Run actual D3D11 frames; these observations do not prove desktop input isolation."""
from pathlib import Path
import subprocess, shutil, uuid, json, hashlib, platform, sys, struct
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from evidence import load_current_build,require_render
from windows_sandbox import launch
ROOT=Path(__file__).resolve().parents[3]
BASE=ROOT/'Validation/Content/sdk-runtime/A'
OUT=BASE/('render-'+uuid.uuid4().hex)
OUT.mkdir(parents=True)
build=load_current_build(ROOT)
player=build/'player'
report={'platform':platform.platform(),'cases':{},'source_receipt_sha256':hashlib.sha256((build/'source-receipt.json').read_bytes()).hexdigest()}
def arguments(output):
 return ['-batchmode','-force-d3d11','-screen-width','640','-screen-height','360','-screen-fullscreen','0','-logFile',str(output/'player.log'),'--stage-a-output',str(output)]
try:
 for mode in ['full-trust','appcontainer','private-station']:
  root=OUT/mode
  shutil.copytree(player,root)
  output=root/'output';output.mkdir()
  exe=root/'StageA.exe';args=arguments(output)
  try:
   if mode=='full-trust':
    result=subprocess.run([str(exe),*args],timeout=45);code=result.returncode
   else:code,_=launch(exe,args,root,isolated_desktop=mode=='private-station',timeout=45,lpac=False)
   case={'exit_code':code}
   if (output/'render.json').exists():case.update(json.loads((output/'render.json').read_text()))
   if (output/'frame.png').exists():
    pixels=(output/'frame.png').read_bytes();case['frame_sha256']=hashlib.sha256(pixels).hexdigest()
    case['png_valid']=len(pixels)>24 and pixels[:8]==b'\x89PNG\r\n\x1a\n' and struct.unpack('>II',pixels[16:24])==(640,360)
   report['cases'][mode]=case
  except Exception as error:report['cases'][mode]={'error':repr(error)}
 require_render(report['cases'])
 report['status']='observations-only-not-stage-A-acceptance'
except Exception as error:
 report['status']='failed';report['error']=repr(error);raise
finally:
 path=OUT/'render-report.json';path.write_text(json.dumps(report,indent=2),encoding='utf-8');print(json.dumps(report,indent=2));print(path)
