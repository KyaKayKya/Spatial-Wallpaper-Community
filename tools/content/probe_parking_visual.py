"""Compare original Unity content with SDK reconstruction in an isolated author."""
from pathlib import Path
import os,shutil,subprocess,json,argparse,re
from build_apps import ROOT,OUT,PROJECT
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--verify',action='store_true');args=parser.parse_args()
 project=OUT/'parking-author';evidence=OUT/'visual-probe';(project/'Assets/Editor').mkdir(parents=True,exist_ok=True)
 shutil.copy2(ROOT/'tools/content/ParkingVisualProbe.cs',project/'Assets/Editor/ParkingVisualProbe.cs')
 for name in ['ContentLoader.cs','ControlledEffects.cs']:
  target=project/'Assets/ContentProbe'/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/'Prototype/Assets/ContentRuntime/Runtime'/name,target)
 for source,name in [('Pipeline.asset','HostPipeline.asset'),('Renderer.asset','HostRenderer.asset')]:
  for suffix in ['', '.meta']:
   target=project/'Assets/Diagnostics'/(name+suffix);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(PROJECT/'Assets/Generated'/(source+suffix),target)
 env=os.environ.copy();env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));env['CONTENT_VISUAL_OUTPUT']=str(evidence);env['CONTENT_VISUAL_PACKAGE']=str(OUT/'Parking.spatial')
 env['CONTENT_VISUAL_VERIFY']='true' if args.verify else 'false'
 scene=(PROJECT/'Assets/Generated/Runtime.unity').read_text();match=re.search(r'm_BackGroundColor: \{r: ([^,]+), g: ([^,]+), b: ([^,]+), a: ([^}]+)\}',scene);assert match,'Actual host camera background missing';env['CONTENT_VISUAL_HOST_BACKGROUND']=','.join(match.groups())
 result=subprocess.run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-projectPath',str(project),'-executeMethod','ParkingVisualProbe.Run','-logFile',str(OUT/'visual-probe.log')],env=env,timeout=300)
 report=evidence/'visual-probe.json'
 print(json.dumps({k:v for k,v in json.loads(report.read_text(encoding='utf-8-sig')).items() if k not in ['source_materials','loaded_materials','source_lights','loaded_lights']},indent=2) if report.exists() else (OUT/'visual-probe.log').read_text(encoding='utf-8-sig',errors='replace')[-10000:])
 raise SystemExit(result.returncode)
if __name__=='__main__':main()
