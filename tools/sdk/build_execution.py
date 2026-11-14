"""Fresh Editor conversion, ordinary Play and author-free Windows VM Player proof."""
from pathlib import Path
import subprocess,json,os,shutil,uuid,hashlib,sys,platform,argparse
from provenance import sources
ROOT=Path(__file__).resolve().parents[2]
UNITY='D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe'
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def create(project,files):
 for d in ['Assets/Editor','Packages','ProjectSettings']:(project/d).mkdir(parents=True,exist_ok=True)
 manifest={'dependencies':{'com.unity.render-pipelines.universal':'14.0.12','com.unity.nuget.newtonsoft-json':'3.2.1','com.unity.modules.physics':'1.0.0','com.unity.modules.audio':'1.0.0','com.spatialwallpaper.sdk':'file:'+(ROOT/'Packages/com.spatialwallpaper.sdk').as_posix()}}
 (project/'Packages/manifest.json').write_text(json.dumps(manifest),encoding='utf-8')
 shutil.copyfile(ROOT/'Prototype/ProjectSettings/ProjectVersion.txt',project/'ProjectSettings/ProjectVersion.txt')
 for name,editor in files:shutil.copyfile(ROOT/'tools/sdk/experiments'/name,project/('Assets/Editor' if editor else 'Assets')/name)
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument("--editor-only",action="store_true",help="Partial conversion probe only; never stage C acceptance");options=parser.parse_args()
 run=uuid.uuid4().hex;project=ROOT/'Validation/Sdk'/('c'+run[:10]);worker=ROOT/'Validation/Sdk'/('w'+run[:10]);output=ROOT/'Validation/Content/sdk-runtime/C'/run
 output.mkdir(parents=True);receipt=sources();report={'run_id':run,'sources':receipt,'platform':platform.platform(),'status':'failed'}
 author_files=[('StageCAuthor.cs',False),('StageCPeer.cs',False),('StageCSpawn.cs',False),('StageCBoundaryAuthor.cs',False),('StageCNullAuthor.cs',False),('StageCChecks.cs',True),('StageCPlay.cs',True),('StageCPlayObserver.cs',False),('StageCPipeline.cs',True),('StageCVisuals.cs',False)]
 create(project,author_files)
 negative=project/'Assets/CallbackNegative';negative.mkdir()
 (negative/'NegativeAuthor.cs').write_text('using UnityEngine;using SpatialWallpaper.Sdk;[WallpaperScript]public sealed class NegativeAuthor:MonoBehaviour{void Awake(){}}',encoding='utf-8')
 (negative/'SdkCallbackNegative.asmdef').write_text(json.dumps({'name':'SdkCallbackNegative','references':['SpatialWallpaper.Sdk'],'autoReferenced':False}),encoding='utf-8')
 env=os.environ.copy();env.update(SDK_C_OUTPUT=str(output),SDK_C_RUN=run);env.setdefault('ALLUSERSPROFILE',env.get('ProgramData','C:/ProgramData'))
 def unity(path,method,log,quit=True):
  command=[UNITY,'-batchmode','-projectPath',str(path),'-executeMethod',method,'-logFile',str(output/log)]
  if quit:command.insert(2,'-quit')
  subprocess.run(command,env=env,timeout=900,check=True)
 def result(name):
  value=json.loads((output/name).read_text(encoding='utf-8-sig'))
  if value.get('run_id')!=run:raise RuntimeError('stale result:'+name)
  return value
 try:
  unity(project,'StageCChecks.Run','conversion.log');report['conversion']=result('compiler-result.json')
  report['package_sha256']=digest(output/'scene.spatial')
  if options.editor_only:
   if sources()!=receipt:raise RuntimeError('sources changed during editor-only probe')
   report['status']='editor-only-not-stage-C-acceptance';return 0
  unity(project,'StageCPlay.Run','play.log',False);report['play']=result('play-result.json')
  create(worker,[('StageCWorker.cs',False),('StageCVisuals.cs',False),('StageCPipeline.cs',True),('StageCBuild.cs',True)])
  unity(worker,'StageCBuild.Run','worker-build.log');report['build']=result('worker-build.json')
  if report['build']['result']!='Succeeded' or report['build']['errors']!=0:raise RuntimeError('worker build failed')
  managed=output/'player/StageC_Data/Managed'
  forbidden=[p.name for p in managed.glob('*.dll') if 'Cilbox' in p.name or p.name in ['SdkCallbackNegative.dll']]
  # Assembly-CSharp contains only trusted Worker/Visuals. Source inventory below proves no author inputs.
  if forbidden:raise RuntimeError('unexpected author/editor DLL:'+repr(forbidden))
  report['worker_sources']={p.relative_to(worker).as_posix():digest(p) for p in (worker/'Assets').rglob('*.cs')}
  from assembly_inventory import inspect
  report['worker_types']=inspect(managed)
  report['worker_assemblies']={p.name:digest(p) for p in managed.glob('*.dll')}
  sys.path.insert(0,str(ROOT/'tools/sdk/experiments'));from windows_sandbox import launch
  report['workers']={}
  for mode in ['full-trust','private-station']:
   root=output/mode;shutil.copytree(output/'player',root);out=root/'output';out.mkdir()
   for name in ['scene.spatial','fields.json']:shutil.copyfile(output/name,root/name)
   args=['-batchmode','-force-d3d11','-screen-width','640','-screen-height','360','-screen-fullscreen','0','-logFile',str(out/'worker.log'),'--sdk-package',str(root/'scene.spatial'),'--sdk-fields',str(root/'fields.json'),'--sdk-output',str(out),'--sdk-run',run]
   if mode=='full-trust':code=subprocess.run([str(root/'StageC.exe'),*args],timeout=60).returncode
   else:code,_=launch(root/'StageC.exe',args,root,isolated_desktop=True,timeout=60,lpac=False)
   value=json.loads((out/'worker-result.json').read_text(encoding='utf-8-sig'))
   if code or value.get('run_id')!=run or value.get('graphics')!='Direct3D11':raise RuntimeError('worker execution failed:'+mode)
   value['exit_code']=code;value['frame_sha256']=digest(out/'frame.png');report['workers'][mode]=value
  for key in ['counter','peer','active','items','frames']:
   if len({json.dumps(x[key]) for x in [report['play'],*report['workers'].values()]})!=1:raise RuntimeError('ordinary Play/VM mismatch:'+key)
  if len({x['frame_sha256'] for x in report['workers'].values()})!=1:raise RuntimeError('sandbox render drift')
  report['play_frame_sha256']=digest(output/'play.png')
  if report['play_frame_sha256']!=report['workers']['private-station']['frame_sha256']:raise RuntimeError('ordinary Play/render mismatch')
  from execution_evidence import require_execution
  require_execution(report,sources(),run)
  report['status']='C-mechanism-observations'
 finally:
  (output/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(output,flush=True)
 if report['status']!='C-mechanism-observations':return 1
 (ROOT/'Validation/Content/sdk-runtime/reports/stage-C-execution.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 return 0
if __name__=='__main__':raise SystemExit(main())
