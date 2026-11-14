"""Run the production Supervisor runtime and author-free Worker through real LPAC pipes."""
from pathlib import Path
import argparse,subprocess,sys,uuid,json,hashlib,shutil,platform
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
sys.path.insert(0,str(ROOT/'tools/sdk'))
from fixtures import sample,archive,component,canonical,ins
from provenance import sources
from windows_framework import references,identity
def require_production_build(receipt):
 if receipt.get('status')!='production-Worker-built-not-acceptance' or receipt.get('release_candidate') is False:
  raise ValueError('production-Worker-required-diagnostic-build-rejected')
def packages(out,frame=None,input_feedback=False):
 p,c=sample();c['frame']=frame or c['frame'];cls=p['classes'][0];cls['fields'] += [{'id':'mesh','type':'mesh'},{'id':'vertices','type':'vector3[]'},{'id':'triangles','type':'i32[]'}]
 start=[ins('ldarg',0),ins('call','SpatialWallpaper.Sdk::Resources::CreateMesh()->Mesh'),ins('stfield','mesh')]
 for field,member in [('vertices','UnityEngine.CoreModule::UnityEngine.Mesh::set_vertices(UnityEngine.Vector3[])->System.Void'),('triangles','UnityEngine.CoreModule::UnityEngine.Mesh::set_triangles(System.Int32[])->System.Void')]:
  start += [ins('ldarg',0),ins('ldfield','mesh'),ins('ldarg',0),ins('ldfield',field),ins('call',member)]
 start += [ins('ldarg',0),ins('ldfield','target'),ins('ldarg',0),ins('ldfield','mesh'),ins('call','SpatialWallpaper.Sdk::Resources::BindMesh(Node,Mesh)->System.Void'),ins('ret')]
 cls['methods'].append({'id':'Start','returns':'void','parameters':[],'locals':[],'body':start})
 script=c['nodes'][0]['components'][1];script['fields'].update(mesh=None,vertices=[[-.1,-.1,.1],[0,.1,.1],[.1,-.1,.1]],triangles=[0,1,2]);script['callbacks'].append('Start')
 material=canonical({'shader':'Universal Render Pipeline/Unlit','bundle':'','properties':{'_BaseColor':{'type':'color','value':[.8,.02,.02,1]}}});name='resources/'+hashlib.sha256(material).hexdigest()+'.json'
 c['resources'].append({'id':'red','kind':'material','file':name});c['nodes'][0]['components'].append(component('mesh-renderer',{'mesh':None,'material':'red'}))
 extras={name:material};identities=[]
 wave=[ins('ldarg',0),ins('ldfield','vertices'),ins('ldc.i4',1),ins('ldc.r4',0.),ins('ldarg',0),ins('ldfield','counter'),ins('ldc.i4',20),ins('rem'),ins('conv.r4'),ins('ldc.r4',.003),ins('mul'),ins('ldc.r4',.04),ins('add'),ins('ldc.r4',.1),ins('newvector3'),ins('stelem','vector3'),ins('ldarg',0),ins('ldfield','mesh'),ins('ldarg',0),ins('ldfield','vertices'),ins('call','UnityEngine.CoreModule::UnityEngine.Mesh::set_vertices(UnityEngine.Vector3[])->System.Void'),ins('ret')]
 if input_feedback:
  # Only ordinary author input API drives the top vertex; no trusted geometry.
  at=wave.index(ins('ldc.r4',0.));wave[at:at+1]=[ins('call','SpatialWallpaper.Sdk::Input::GetPointerX()->System.Single'),ins('ldc.r4',.3),ins('mul')]
 cls['methods'][0]['body']=cls['methods'][0]['body'][:-1]+wave
 for filename,bad in [('healthy.spatial',False),('bad.spatial',True)]:
  if bad:cls['methods'][0]['body']=[ins('br',0)]
  data=archive(p,c,extras);(out/filename).write_bytes(data)
  import zipfile,io
  with zipfile.ZipFile(io.BytesIO(data)) as z:identities.append(json.loads(z.read('manifest.json'))['build_id'])
 return identities
def main():
 parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--build',type=Path,required=True);parser.add_argument('--width',type=int,default=640);parser.add_argument('--height',type=int,default=360);parser.add_argument('--steady-ms',type=int,default=0);parser.add_argument('--negative-stop-grace',action='store_true');args=parser.parse_args();build=args.build.resolve()
 if not 1<=args.width<=8192 or not 1<=args.height<=8192 or args.width*args.height*4>64*1024*1024 or not 0<=args.steady_ms<=60000:raise ValueError('measurement bounds')
 receipt=json.loads((build/'report.json').read_text());require_production_build(receipt);actual={p.relative_to(build/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (build/'player').rglob('*') if p.is_file()};assert actual==receipt['runtime_files'],'Worker image changed'
 out=ROOT/'Validation/Content/sdk-runtime/D/production'/uuid.uuid4().hex;out.mkdir(parents=True);ids=packages(out);source=sources()
 from failure_report import failure_receipt
 with failure_receipt(out/'report.json',{'worker_build':receipt,'supervisor_sources':source,'package_ids':ids,'platform':platform.platform(),'command':sys.argv}):
  subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_validator.py'),'--output',str(out/'validator')],check=True)
  shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
  cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),*references(),'-out:'+str(out/'Checks.exe'),str(ROOT/'Supervisor/Tests/ProductionChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs'),*map(str,(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities').glob('*.cs')),*map(str,(ROOT/'Supervisor/Broker').glob('*.cs')),*map(str,(ROOT/'Supervisor/Core').glob('*.cs')),*map(str,(ROOT/'Supervisor/Windows').glob('*.cs'))]
  cmd += list(map(str,(ROOT/'Supervisor/Hosting').glob('*.cs')))
  cmd.append(str(ROOT/'Supervisor/Tests/OwnedHelperPause.cs'))
  if args.negative_stop_grace:
   original=ROOT/'Supervisor/Core/WorkerWatchdog.cs';text=original.read_text(encoding='utf-8-sig');needle='now-stopAt>=policy.StopMilliseconds';assert text.count(needle)==1
   copy=out/'WorkerWatchdog.cs';copy.write_text(text.replace(needle,'now-stopAt>=0'),encoding='utf-8');cmd=[str(copy) if value==str(original) else value for value in cmd]
  subprocess.run(cmd,check=True);shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config');print(out,flush=True)
  attempts=ROOT/'Validation/Sdk'/('r'+out.name[:10])
  with (out/'stdout.txt').open('w') as stdout,(out/'stderr.txt').open('w') as stderr:
   completed=subprocess.run([str(out/'Checks.exe'),str(out),str(build/'report.json'),str(build/'player'),str(out/'healthy.spatial'),ids[0],str(out/'bad.spatial'),ids[1],str(attempts),str(args.width),str(args.height),str(args.steady_ms)],timeout=120,stdout=stdout,stderr=stderr)
  assert sources()==source,'Supervisor verification source changed'
  if args.negative_stop_grace:
   evidence=json.loads((out/'unresponsive-stop-evidence.json').read_text())
   assert completed.returncode==1 and 'System.Exception: unresponsive-Worker-lost-full-stop-grace' in (out/'stderr.txt').read_text(),'wrong-stop-grace-negative'
   assert evidence['paused_kernel_exit_before_fallback'] is True and evidence['forced_stop'] is True and evidence['forced_started_at_ms']-evidence['stop_requested_at_ms']<500,'negative did not reach real premature termination'
   report={'status':'expected-premature-Unity-stop-rejected','supervisor_sources':source,'worker_build':receipt,'results':evidence,'supervisor_binary_sha256':hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest()}
   (out/'report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report['results'],indent=2));return
  completed.check_returncode()
  result=json.loads((out/'results.json').read_text());report={'status':'production-runtime-observations-not-D-acceptance','platform':platform.platform(),'worker_build':receipt,'supervisor_sources':source,'package_ids':ids,'results':result,'supervisor_binary_sha256':hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest()}
  from render_profiles import require_runtime_receipt
  observed=require_runtime_receipt((out/'healthy.log').read_text(encoding='utf-8-sig'),result['render_expected_identity'],args.width,args.height)
  if receipt['build'].get('render_profile',{}).get('identity')!=observed['profile']:raise ValueError('Worker-build-render-profile-mismatch')
  report['render_observation']=observed
  report['render_observation_scope']='Worker-reported-configuration-not-visual-fidelity-or-presented-frame'
  report['framework_references']=identity()
  (out/'report.json').write_text(json.dumps(report,indent=2));print(json.dumps({k:v for k,v in result.items() if not k.endswith('_samples')},indent=2));print(out)
if __name__=='__main__':main()
