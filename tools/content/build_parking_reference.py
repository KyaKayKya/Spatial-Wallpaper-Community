"""Build original Parking as a standalone visual reference, never a content package."""
from pathlib import Path
import os,shutil,subprocess,hashlib,json,argparse
from build_apps import ROOT,OUT
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--reference-worktree',type=Path,default=ROOT.parent/'no-wallpaper');args=parser.parse_args()
 project=OUT/'parking-author'
 reference=args.reference_worktree.resolve()
 test_sources={name:hashlib.sha256((ROOT/'tools/content'/name).read_bytes()).hexdigest() for name in ['ParkingReferenceBuild.cs','ParkingReferenceCapture.cs','ParkingLightingStateProbe.cs','NaturalSeriesCapture.cs']}
 source_files={p.relative_to(reference).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (reference/'Prototype/Assets/Parking').rglob('*') if p.is_file()}
 shutil.copytree(reference/'Prototype/Assets/Parking',project/'Assets/Parking',dirs_exist_ok=True,ignore=shutil.ignore_patterns('Editor'))
 author_editor=(project/'Assets/Parking/Editor').resolve();assert author_editor.is_relative_to(project.resolve())
 if author_editor.exists():shutil.rmtree(author_editor)
 for name in ['AdaptiveRenderCadence','DisplaySceneVolume','PreviewEyeDriver','ShowcaseBenchmark','NativeProcessCounters']:
  for suffix in ['.cs','.cs.meta']:
   target=project/'Assets/ReferenceShared'/(name+suffix);target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(reference/'Prototype/Assets/Showcase/Runtime'/(name+suffix),target)
 scene=reference/'Prototype/Assets/Library/Scenes/parking.unity'
 target=project/'Assets/Library/Scenes/parking.unity';target.parent.mkdir(parents=True,exist_ok=True)
 shutil.copy2(scene,target);shutil.copy2(Path(str(scene)+'.meta'),Path(str(target)+'.meta'))
 receipt={'source_worktree':str(reference),'scene':'Prototype/Assets/Library/Scenes/parking.unity','sha256':hashlib.sha256(scene.read_bytes()).hexdigest(),'git_head':subprocess.check_output(['git','rev-parse','HEAD'],cwd=reference,text=True).strip(),'source_files':source_files}
 shutil.copytree(ROOT/'Prototype/Assets/Tracking/Core',project/'Assets/Tracking/Core',dirs_exist_ok=True)
 for name,folder in [('ParkingReferenceCapture.cs','Assets/ContentProbe'),('ParkingLightingStateProbe.cs','Assets/ContentProbe'),('ParkingReferenceBuild.cs','Assets/Editor'),('NaturalSeriesCapture.cs','Assets/ContentProbe')]:
  target=project/folder/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/'tools/content'/name,target)
 for name in ['Calibration/Core/ViewportGeometry.cs','Calibration/Runtime/DisplayViewport.cs']:
  target=project/'Assets'/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/'Prototype/Assets'/name,target)
 env=os.environ.copy();env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));env['CONTENT_REFERENCE_BUILD']=str(OUT/'reference-player/ParkingReference.exe')
 result=subprocess.run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(project),'-executeMethod','ParkingReferenceBuild.Run','-logFile',str(OUT/'reference-build.log')],env=env,timeout=600)
 assert source_files=={p.relative_to(reference).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (reference/'Prototype/Assets/Parking').rglob('*') if p.is_file()},'Reference inputs changed during build'
 assert hashlib.sha256(scene.read_bytes()).hexdigest()==receipt['sha256'],'Reference scene changed during build'
 if result.returncode==0:
  from fingerprint_apps import closure
  receipt['player_closure_sha256']=closure(OUT/'reference-player')
  assert test_sources=={name:hashlib.sha256((ROOT/'tools/content'/name).read_bytes()).hexdigest() for name in test_sources},'Reference harness changed during build'
  receipt['compiled_test_sources']=test_sources
  (OUT/'reports/parking-reference-source.json').write_text(json.dumps(receipt,indent=2)+'\n')
 print('Reference build exit',result.returncode);raise SystemExit(result.returncode)
if __name__=='__main__':main()
