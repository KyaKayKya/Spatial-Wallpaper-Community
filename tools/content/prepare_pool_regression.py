"""Copy the original Pool dependency closure into an owned author project."""
import argparse,hashlib,json,os,re,shutil,subprocess
from pathlib import Path
from build_apps import ROOT,OUT

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--reference',action='store_true');args=parser.parse_args()
 reference=ROOT.parent/'no-wallpaper';assets=reference/'Prototype/Assets';project=OUT/'pool-author'
 for folder in ['Assets/Editor','Packages','ProjectSettings']:(project/folder).mkdir(parents=True,exist_ok=True)
 inputs={};pending=[];seen=set();index={}
 for meta in assets.rglob('*.meta'):
  match=re.search(r'^guid: ([a-f0-9]{32})',meta.read_text(encoding='utf-8-sig',errors='ignore'),re.M)
  if match:index[match.group(1)]=Path(str(meta)[:-5])
 def copy(source,target):
  target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,target)
  if source.is_relative_to(reference):inputs[source.relative_to(reference).as_posix()]=hashlib.sha256(source.read_bytes()).hexdigest()
 def enqueue(source):
  if source.is_file() and source not in seen:seen.add(source);pending.append(source)
 for folder in ['Pool','Bakery']:
  for source in (assets/folder).rglob('*'):
   if source.is_file() and (folder=='Bakery' or 'Editor' not in source.relative_to(assets/folder).parts):enqueue(source)
 for name in ['AdaptiveRenderCadence','DisplaySceneVolume','PreviewEyeDriver','ShowcaseBenchmark','NativeProcessCounters']:
  for suffix in ['.cs','.cs.meta']:enqueue(assets/'Showcase/Runtime'/(name+suffix))
 for suffix in ['', '.meta']:enqueue(assets/('Library/Scenes/pool.unity'+suffix))
 while pending:
  source=pending.pop();copy(source,project/'Assets'/source.relative_to(assets))
  if source.suffix!='.meta':enqueue(Path(str(source)+'.meta'))
  if source.suffix not in ['.meta','.unity','.prefab','.mat','.asset','.shader','.shadergraph','.shadersubgraph','.cginc','.hlsl']:continue
  text=source.read_text(encoding='utf-8-sig',errors='ignore')
  for guid in re.findall(r'guid[\s\"\:]+([a-f0-9]{32})',text):
   target=index.get(guid)
   if target:enqueue(target)
  for relative in re.findall(r'#include\s+"([^\"]+)"',text):
   if relative.startswith('Assets/'):enqueue(reference/'Prototype'/relative)
   elif not relative.startswith('Packages/'):enqueue(source.parent/relative)
 for source in (ROOT/'Packages/com.spatialwallpaper.sdk').rglob('*'):
  if source.is_file():copy(source,project/'Packages/com.spatialwallpaper.sdk'/source.relative_to(ROOT/'Packages/com.spatialwallpaper.sdk'))
 for source in (ROOT/'Prototype/Assets/Tracking/Core').rglob('*'):
  if source.is_file():copy(source,project/'Assets/Tracking/Core'/source.relative_to(ROOT/'Prototype/Assets/Tracking/Core'))
 for name in ['Calibration/Core/ViewportGeometry.cs','Calibration/Runtime/DisplayViewport.cs']:copy(ROOT/'Prototype/Assets'/name,project/'Assets'/name)
 for name in ['ProjectVersion.txt','ProjectSettings.asset','GraphicsSettings.asset','QualitySettings.asset']:copy(reference/'Prototype/ProjectSettings'/name,project/'ProjectSettings'/name)
 copy(ROOT/'tools/content/PoolContentExport.cs',project/'Assets/Editor/PoolContentExport.cs')
 harness=['PoolReferenceBuild.cs','PoolReferenceCapture.cs','NaturalSeriesCapture.cs'] if args.reference else ['PoolContentExport.cs']
 test_sources={name:hashlib.sha256((ROOT/'tools/content'/name).read_bytes()).hexdigest() for name in harness}
 if args.reference:
  for name,folder in [('PoolReferenceBuild.cs','Editor'),('PoolReferenceCapture.cs','ContentProbe'),('NaturalSeriesCapture.cs','ContentProbe')]:copy(ROOT/'tools/content'/name,project/'Assets'/folder/name)
 manifest=json.loads((ROOT/'tools/content/probe-manifest.json').read_text());manifest['dependencies']['com.spatialwallpaper.sdk']='file:com.spatialwallpaper.sdk';(project/'Packages/manifest.json').write_text(json.dumps(manifest,indent=2))
 env=os.environ.copy();env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'));env['CONTENT_BUILD_OUTPUT']=str(OUT)
 env['CONTENT_REFERENCE_BUILD']=str(OUT/'pool-reference-player/PoolReference.exe')
 result=subprocess.run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(project),'-executeMethod','PoolReferenceBuild.Run' if args.reference else 'PoolContentExport.Run','-logFile',str(OUT/('pool-reference-build.log' if args.reference else 'pool-export.log'))],env=env,timeout=1200)
 assert all(hashlib.sha256((reference/name).read_bytes()).hexdigest()==digest for name,digest in inputs.items()),'Original Pool inputs changed'
 assert test_sources=={name:hashlib.sha256((ROOT/'tools/content'/name).read_bytes()).hexdigest() for name in harness},'Compiled harness changed during build'
 receipt={'source_worktree':str(reference),'inputs':inputs,'unity_exit':result.returncode,'compiled_test_sources':test_sources}
 if args.reference and result.returncode==0:
  from fingerprint_apps import closure
  receipt['player_closure_sha256']=closure(OUT/'pool-reference-player')
 (OUT/('reports/pool-reference-source.json' if args.reference and result.returncode==0 else 'reports/pool-author-attempt.json')).write_text(json.dumps(receipt,indent=2)+'\n')
 print('Pool author exit',result.returncode,flush=True);raise SystemExit(result.returncode)
if __name__=='__main__':main()
