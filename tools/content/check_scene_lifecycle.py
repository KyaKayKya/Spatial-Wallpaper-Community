"""Real IPC switches in one separate, read-only-observed validation Player."""
import hashlib,json,shutil,subprocess,time,uuid,zipfile
from build_apps import ROOT,OUT,validate_receipt
from run_e2e import Client
from fingerprint_apps import closure

def broken(source,path,kind):
 with zipfile.ZipFile(source) as archive:files={n:archive.read(n) for n in archive.namelist()}
 scene=json.loads(files['scene.json'])
 if kind=='load':next(m for m in scene['materials'] if m['bundle'])['shader']='Validation/MissingShader'
 else:next(m for m in scene['materials'] if m['shader']=='Universal Render Pipeline/Lit')['keywords'].append('_DETAIL_MULX2')
 files['scene.json']=json.dumps(scene,separators=(',',':')).encode();manifest=json.loads(files['manifest.json']);manifest['files']['scene.json']={'size':len(files['scene.json']),'sha256':hashlib.sha256(files['scene.json']).hexdigest()};manifest.pop('build_id');manifest['build_id']=hashlib.sha256(json.dumps(manifest,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()).hexdigest();files['manifest.json']=json.dumps(manifest,separators=(',',':'),ensure_ascii=False).encode()
 with zipfile.ZipFile(path,'w') as archive:
  for name,data in files.items():archive.writestr(name,data)

def main():
 validate_receipt();receipt=OUT/'reports/series-player-source.json';compiled=json.loads(receipt.read_text());platform=hashlib.sha256((OUT/'reports/platform-source-receipt.json').read_bytes()).hexdigest();assert compiled['platform_source_receipt_sha256']==platform
 player=OUT/'series-player';install=closure(player);assert install==compiled['player_closure_sha256'];run=OUT/'runs'/('lc-'+uuid.uuid4().hex[:12]);run.mkdir();data=run/'data';snapshot=run/'state.json';report_path=OUT/'reports/scene-lifecycle.json';cases=[];client=None;process=None
 def observed(tick=-1):
  deadline=time.monotonic()+20
  while time.monotonic()<deadline:
   candidates=sorted(snapshot.parent.glob(snapshot.name+'.????????'))
   if candidates:
    try:
     value=json.loads(candidates[-1].read_text())
     if value['tick']>tick:return value
    except (PermissionError,FileNotFoundError):pass # Windows atomic replacement may briefly hold the destination.
   assert process.poll() is None,'Observer exited';time.sleep(.1)
  raise TimeoutError('Lifecycle snapshot')
 try:
  process=subprocess.Popen([str(player/'ContentPlayer.exe'),'--content-data-root',str(data),'--lifecycle-output',str(snapshot),'-screen-fullscreen','1','-window-mode','borderless','-logFile',str(run/'runtime.log')]);descriptor=data/'runtime/connection.json';deadline=time.monotonic()+30
  while not descriptor.exists():
   assert process.poll() is None
   if time.monotonic()>deadline:raise TimeoutError('Runtime discovery')
   time.sleep(.1)
  client=Client(json.loads(descriptor.read_text(encoding='utf-8-sig')));state=client.call('GetContentState',{});display=state['display_id'];assert state['runtime_ready'] and not state['camera_started'];baseline=observed(3);assert baseline['roots']==baseline['targets']==baseline['planar']==baseline['probes']==0
  packages={name:OUT/(name+'.spatial') for name in ['Pool','Train','Parking']};packages['StaticWindow']=OUT/'static-validation/sample.spatial';works={};scenes={};digests={}
  def imported(path):
   staging=str(uuid.uuid4());shutil.copy2(path,data/'staging'/(staging+'.spatial'));return client.call('ImportContent',{'staging_id':staging})
  for name,path in packages.items():
   works[name]=imported(path);digests[name]=hashlib.sha256(path.read_bytes()).hexdigest()
   with zipfile.ZipFile(path) as archive:scenes[name]=json.loads(archive.read('scene.json'))
  errors=0
  def verify(name):
   first=observed();actual=observed(first['tick']+3);state=client.call('GetContentState',{});assert state['presentation_ready'] and not state['camera_started'] and state['render_errors']==errors,(name,state)
   assert actual['ready'] and actual['roots']==1 and not actual['invert_culling'] and actual['hidden_renderers']==0,actual
   pool=name=='Pool'
   for key in ['planar','probes','reflection_cameras','targets']:assert actual[key]==int(pool),(name,key,actual)
   assert actual['global_reflection'] in actual['target_ids'] if pool else actual['global_reflection']==baseline['global_reflection'],actual
   assert actual['lightmaps']==len(scenes[name]['lightmaps'])
   camera=next((e for e in scenes[name].get('effects',[]) if e['kind']=='camera'),baseline)
   for key in ['hdr','msaa','opaque','depth','post','renderer']:assert actual[key]==camera[key],(name,key,actual[key],camera[key])
   gradient=next((e for e in scenes[name].get('effects',[]) if e['kind']=='ambient-gradient'),None);uniform=any(e['kind']=='ambient' for e in scenes[name].get('effects',[]))
   assert actual['ambient_mode']==(1 if gradient else 4 if uniform else 3),(name,'ambient-mode',actual)
   expected=[gradient[key] for key in ['sky','equator','ground']] if gradient else [scenes[name]['environment']['ambient'],*baseline['ambient_colors'][1:]]
   assert actual['ambient_colors']==expected,(name,'ambient-colors',actual['ambient_colors'],expected)
   return actual
  for cycle in range(2):
   for name in ['Pool','Train','Parking','Pool','StaticWindow']:
    result=client.call('ApplyContent',{'display_id':display,'work_id':works[name]['work_id']});assert result['succeeded'],result;actual=verify(name);cases.append({'name':'switch-'+str(cycle)+'-'+name,'passed':True,'observed':actual});print('PASS switch',cycle,name,flush=True)
  result=client.call('ApplyContent',{'display_id':display,'work_id':works['Pool']['work_id']});assert result['succeeded'];verify('Pool');old=client.call('GetContentState',{})['state']['applied'][display]
  for kind in ['load','variant']:
   path=run/(kind+'.spatial');broken(packages['Train'] if kind=='load' else packages['StaticWindow'],path,kind);bad=imported(path);before=errors;result=client.call('ApplyContent',{'display_id':display,'work_id':bad['work_id']});assert not result['succeeded'] and result['restored'],result;state=client.call('GetContentState',{});errors=state['render_errors'];assert errors==before if kind=='load' else errors>before
   assert state['state']['applied'][display]==old;actual=verify('Pool');cases.append({'name':kind+'-failure-restores-pool-resources-and-camera','passed':True,'application':result,'observed':actual});print('PASS failure restore',kind,flush=True)
  result=client.call('ApplyContent',{'display_id':display,'work_id':works['StaticWindow']['work_id']});assert result['succeeded'];verify('StaticWindow');assert closure(player)==install
  report={'passed':True,'scope':'Actual IPC in one Player with read-only validation observer; two scene-switch cycles and load/first-frame rollback from Pool; not performance or human acceptance','cases':cases,'run_directory':run.relative_to(ROOT).as_posix(),'observer_player_closure_sha256':install,'platform_source_receipt_sha256':platform,'observer_receipt_sha256':hashlib.sha256(receipt.read_bytes()).hexdigest(),'package_sha256':digests};report_path.write_text(json.dumps(report,indent=2)+'\n');print('PASS lifecycle',len(cases),flush=True)
 except Exception as error:
  report_path.write_text(json.dumps({'passed':False,'error':str(error),'cases':cases,'run_directory':run.relative_to(ROOT).as_posix()},indent=2)+'\n');raise
 finally:
  if client:client.close()
  if process and process.poll() is None:process.terminate();process.wait(timeout=15)
if __name__=='__main__':main()
