"""Prepare an isolated author from the read-only library worktree and export Train."""
import argparse,hashlib,json,os,shutil,subprocess
from pathlib import Path
from build_apps import ROOT,OUT

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--reference',action='store_true');args=parser.parse_args()
 reference=ROOT.parent/'no-wallpaper';project=OUT/'train-author'
 for folder in ['Assets/Editor','Packages','ProjectSettings']:(project/folder).mkdir(parents=True,exist_ok=True)
 inputs={}
 def copy_file(source,target):
  target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
  if source.is_relative_to(reference):inputs[source.relative_to(reference).as_posix()]=hashlib.sha256(source.read_bytes()).hexdigest()
 for folder in ['Train','Pool/Settings']:
  for source in (reference/'Prototype/Assets'/folder).rglob('*'):
   if source.is_file() and 'Editor' not in source.relative_to(reference/'Prototype/Assets'/folder).parts:
    copy_file(source,project/'Assets'/source.relative_to(reference/'Prototype/Assets'))
 for name in ['AdaptiveRenderCadence','DisplaySceneVolume','PreviewEyeDriver','ShowcaseBenchmark','NativeProcessCounters']:
  for suffix in ['.cs','.cs.meta']:copy_file(reference/'Prototype/Assets/Showcase/Runtime'/(name+suffix),project/'Assets/ReferenceShared'/(name+suffix))
 for source in (ROOT/'Packages/com.spatialwallpaper.sdk').rglob('*'):
  if source.is_file():copy_file(source,project/'Packages/com.spatialwallpaper.sdk'/source.relative_to(ROOT/'Packages/com.spatialwallpaper.sdk'))
 for source in (ROOT/'Prototype/Assets/Tracking/Core').rglob('*'):
  if source.is_file():copy_file(source,project/'Assets/Tracking/Core'/source.relative_to(ROOT/'Prototype/Assets/Tracking/Core'))
 for name in ['Calibration/Core/ViewportGeometry.cs','Calibration/Runtime/DisplayViewport.cs']:copy_file(ROOT/'Prototype/Assets'/name,project/'Assets'/name)
 for name in ['ProjectVersion.txt','ProjectSettings.asset','GraphicsSettings.asset','QualitySettings.asset']:copy_file(reference/'Prototype/ProjectSettings'/name,project/'ProjectSettings'/name)
 scene=reference/'Prototype/Assets/Library/Scenes/train.unity'
 for source in [scene,Path(str(scene)+'.meta')]:copy_file(source,project/'Assets/Library/Scenes'/source.name)
 copy_file(ROOT/'tools/content/TrainContentExport.cs',project/'Assets/Editor/TrainContentExport.cs')
 harness=['TrainReferenceBuild.cs','TrainReferenceCapture.cs','NaturalSeriesCapture.cs'] if args.reference else ['TrainContentExport.cs']
 test_sources={name:hashlib.sha256((ROOT/'tools/content'/name).read_bytes()).hexdigest() for name in harness}
 if args.reference:
  for name,folder in [('TrainReferenceBuild.cs','Editor'),('TrainReferenceCapture.cs','ContentProbe'),('NaturalSeriesCapture.cs','ContentProbe')]:copy_file(ROOT/'tools/content'/name,project/'Assets'/folder/name)
 manifest=json.loads((ROOT/'tools/content/probe-manifest.json').read_text());manifest['dependencies']['com.spatialwallpaper.sdk']='file:com.spatialwallpaper.sdk'
 (project/'Packages/manifest.json').write_text(json.dumps(manifest,indent=2))
 env=os.environ.copy();env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));env['CONTENT_BUILD_OUTPUT']=str(OUT)
 env['CONTENT_REFERENCE_BUILD']=str(OUT/'train-reference-player/TrainReference.exe')
 result=subprocess.run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(project),'-executeMethod','TrainReferenceBuild.Run' if args.reference else 'TrainContentExport.Run','-logFile',str(OUT/('train-reference-build.log' if args.reference else 'train-export.log'))],env=env,timeout=1200)
 assert all(hashlib.sha256((reference/name).read_bytes()).hexdigest()==digest for name,digest in inputs.items()),'Original source changed during export'
 assert test_sources=={name:hashlib.sha256((ROOT/'tools/content'/name).read_bytes()).hexdigest() for name in harness},'Compiled harness changed during build'
 receipt={'source_worktree':str(reference),'inputs':inputs,'unity_exit':result.returncode,'compiled_test_sources':test_sources}
 if args.reference and result.returncode==0:
  from fingerprint_apps import closure
  receipt['player_closure_sha256']=closure(OUT/'train-reference-player')
 if result.returncode==0:(OUT/('reports/train-reference-source.json' if args.reference else 'reports/train-author-source.json')).write_text(json.dumps(receipt,indent=2)+'\n')
 print('Train author exit',result.returncode,flush=True);raise SystemExit(result.returncode)
if __name__=='__main__':main()
