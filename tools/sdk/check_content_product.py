"""Build and run original ContentPlayer ownership and modern/legacy transactions."""
from pathlib import Path
import argparse,os,uuid,shutil,subprocess,json,hashlib,sys
from build_execution import ROOT,UNITY,create
from check_contracts import DATA
from provenance import sources
from toolchain_process import run_toolchain
from windows_framework import references
sys.path.insert(0,str(ROOT/'Supervisor/Tests'));from run_production import packages
sys.path.insert(0,str(ROOT/'tools/content'));from store_fixture import create as legacy_fixture
p=argparse.ArgumentParser();p.add_argument('--runtime',type=Path,required=True);p.add_argument('--reuse-project',type=Path);p.add_argument('--case',choices=['normal','native-desktop','native-desktop-empty','native-desktop-failed-apply','native-desktop-input','native-desktop-controls','native-desktop-controls-stop-failure','native-exit','native-reapply','native-reapply-unproven','idle-native-import','projection','projection-stall','projection-load-unload','native-resize-reapply','native-resize-auto','native-resize-auto-unproven','native-resize-quit','native-crash-reapply','tracking-simulate','tracking-move','tracking-disconnect','tracking-reset-negative','tracking-quit-unproven','native-occlusion','native-unload-recovery-ownership','native-first-frame-unload','legacy-first-frame-destroy'],default='normal');args=p.parse_args();run=uuid.uuid4().hex;out=ROOT/'Validation/Content/sdk-runtime/F/content-product'/run;out.mkdir(parents=True);print(out,flush=True);project=args.reuse_project.resolve() if args.reuse_project else ROOT/'Validation/Sdk'/('c'+run[:10]);assert project.is_relative_to(ROOT/'Validation/Sdk')
if args.reuse_project:assert (project/'content-product-fixture.json').is_file(),'owned product fixture project required'
else:create(project,[('StageFContentClientBuild.cs',True),('StageFContentClientObserver.cs',False)])
for name in ['Tracking/TrackingEyeDriver.cs','Tracking/TrackingEyeDriver.cs.meta','Showcase/Runtime/AdaptiveRenderCadence.cs','Showcase/Runtime/AdaptiveRenderCadence.cs.meta','Showcase/Runtime/PreviewEyeDriver.cs','Showcase/Runtime/PreviewEyeDriver.cs.meta','StreamingAssets/tracking-calibration-synthetic.json','StreamingAssets/tracking-calibration-synthetic.json.meta']:
 source=ROOT/'Prototype/Assets'/name;target=project/'Assets'/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,target)
manifest_path=project/'Packages/manifest.json';manifest=json.loads(manifest_path.read_text());manifest['dependencies']['com.unity.modules.screencapture']='1.0.0';manifest_path.write_text(json.dumps(manifest,indent=2))
for folder in ['ContentRuntime','Tracking/Core']:
 target=project/'Assets'/folder
 if target.exists():
  # Update only this fixture's previously copied source files; do not delete project data.
  for f in (ROOT/'Prototype/Assets'/folder).rglob('*'):
   if f.is_file():q=target/f.relative_to(ROOT/'Prototype/Assets'/folder);q.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(f,q)
 else:shutil.copytree(ROOT/'Prototype/Assets'/folder,target)
for name in ['RuntimeContentHost.cs','ContentPlayerBootstrap.cs','ContentDesktopRuntime.cs']:
 source=ROOT/'tools/sdk/tests/legacy-client'/name;target=project/'Assets/LegacyClientFixtures'/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(source,target)
for name in ['Desktop/Windows/WindowsDesktopWindow.cs','Desktop/Windows/WindowsDesktopShell.cs','Desktop/Core/DesktopDisplay.cs','Desktop/Core/DesktopPolicy.cs','Desktop/Core/IDesktopShellState.cs','ContentRuntime/Runtime/ContentHostClient.cs','Calibration/Core/ViewportGeometry.cs','Calibration/Runtime/DisplayViewport.cs']:
 target=project/'Assets'/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(ROOT/'Prototype/Assets'/name,target)
if args.reuse_project:
 for f in (ROOT/'Packages/com.spatialwallpaper.sdk').rglob('*'):
  if f.is_file():q=project/'Packages/com.spatialwallpaper.sdk'/f.relative_to(ROOT/'Packages/com.spatialwallpaper.sdk');q.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(f,q)
 for name,editor in [('StageFContentClientBuild.cs',True),('StageFContentClientObserver.cs',False)]:shutil.copyfile(ROOT/'tools/sdk/experiments'/name,project/('Assets/Editor' if editor else 'Assets')/name)
product_mutations={}
if args.case=='tracking-quit-unproven':
 target=project/'Assets/Tracking/Core/RuntimeTrackingHost.cs';target.write_text('#define SDK_TRACKING_TESTS\n'+target.read_text());product_mutations['tracking_wait_proof_hook']=hashlib.sha256(target.read_bytes()).hexdigest()
if args.case=='tracking-reset-negative':
 target=project/'Assets/ContentRuntime/Runtime/RuntimeContentPresentation.cs';source=target.read_text();needle='var tracked=tracker&&tracker.OwnsEye?(Vector3?)tracker.EyeInView:null;';assert source.count(needle)==1;target.write_text(source.replace(needle,'Vector3? tracked=null;'));product_mutations['controlled_legacy_default_eye_reset']=hashlib.sha256(target.read_bytes()).hexdigest()
(project/'content-product-fixture.json').write_text(json.dumps({'kind':'owned-content-product-fixture','created_by':str(Path(__file__))}))
def product_sources():
 result={f.relative_to(ROOT).as_posix():hashlib.sha256(f.read_bytes()).hexdigest() for folder in ['Prototype/Assets/ContentRuntime','Prototype/Assets/ContentPlayer','Prototype/Assets/Tracking/Core','Prototype/Assets/ContentManager','Prototype/Assets/Desktop/Windows','Prototype/Assets/Desktop/Core'] for f in (ROOT/folder).rglob('*') if f.is_file() and f.suffix in ['.cs','.asmdef']}
 for name in ['Tracking/TrackingEyeDriver.cs','Showcase/Runtime/AdaptiveRenderCadence.cs','Showcase/Runtime/PreviewEyeDriver.cs','StreamingAssets/tracking-calibration-synthetic.json']:
  f=ROOT/'Prototype/Assets'/name;result[f.relative_to(ROOT).as_posix()]=hashlib.sha256(f.read_bytes()).hexdigest()
 for f in [*(ROOT/'tracker/src').rglob('*.py'),ROOT/'tracker/pyproject.toml',ROOT/'tracker/uv.lock']:
  result[f.relative_to(ROOT).as_posix()]=hashlib.sha256(f.read_bytes()).hexdigest()
 return result
report={'status':'failed','case':args.case,'sources':sources(),'product_sources':product_sources(),'runtime_manifest_sha256':hashlib.sha256((args.runtime.resolve()/'runtime.json').read_bytes()).hexdigest(),'mutated_copied_product_sources':product_mutations,'tracking_scope':{'fixture_visibility':'owned topmost-noactivate window; strict actual screen verification unchanged','source':'actual TCP synthetic producer; no physical camera','camera_mode_classification_injected':args.case=='tracking-disconnect'} if args.case.startswith('tracking-') else None,'project':str(project),'scope':__doc__};env=os.environ.copy();env['SDK_F_CONTENT_OUTPUT']=str(out);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
try:
 ids=packages(out,input_feedback=args.case.startswith('native-desktop'));legacy_fixture(out/'legacy.spatial');report['build']={}
 with (out/'toolchain.log').open('w') as log:run_toolchain([UNITY,'-batchmode','-quit','-projectPath',str(project),'-executeMethod','StageFContentClientBuild.Run','-logFile',str(out/'build.log')],ROOT,log,600,report['build'],env=env)
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
 from trusted_client_sources import client_sources
 files=[ROOT/'Supervisor/Tests/ContentProductChecks.cs',ROOT/'Supervisor/Tests/ContentDesktopInputChecks.cs']+client_sources(ROOT)
 subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-main:ContentProductChecks',*references(),'-r:System.Drawing.dll','-r:System.Windows.Forms.dll','-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,dict.fromkeys(files))],check=True)
 report['process']={}
 with (out/'checks.log').open('w') as log:run_toolchain([str(out/'Checks.exe'),str(out),str(args.runtime.resolve()),str(ROOT/'Validation/Sdk'/('p'+run[:10])),str(ROOT/'Validation/Sdk'/('a'+run[:10])),args.case,str(Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent/'tracker/.venv/Scripts/python.exe'),str(ROOT/'tracker')],ROOT,log,180,report['process'])
 report['result']=json.loads((out/'result.json').read_text());assert sources()==report['sources'] and product_sources()==report['product_sources'];report['status']='passed-development-original-content-product'
finally:
 report['sources_unchanged']=sources()==report['sources'] and product_sources()==report['product_sources'];(out/'report.json').write_text(json.dumps(report,indent=2));print(out)
