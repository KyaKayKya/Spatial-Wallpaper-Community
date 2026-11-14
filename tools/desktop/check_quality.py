"""Bounded original Runtime regression and frame-sequence sampling, not RC."""
from pathlib import Path
import argparse,hashlib,json,os,shutil,subprocess,sys,time,uuid
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'Validation/Dependencies/python'));import psutil
sys.path.insert(0,str(ROOT/'tools/content'));from run_e2e import Client
def read(p):return json.loads(p.read_text(encoding='utf-8-sig'))
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--exe',type=Path,required=True);p.add_argument('--seconds',type=int,default=10);p.add_argument('--runtime',type=Path);p.add_argument('--cafe',type=Path);p.add_argument('--package-update',nargs=2,action='append',default=[]);p.add_argument('--scenes',nargs='+',default=['minishotgame','train','rainycafe']);p.add_argument('--skip-builtin',action='store_true')
 p.add_argument('--seed-content',type=Path)
 # Accept the seed option without changing existing invocations.
 args=p.parse_args()
 out=ROOT/'Validation/Content/sdk-runtime/quality-repair'/uuid.uuid4().hex[:12];out.mkdir(parents=True);print(out,flush=True)
 common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent
 exe=args.exe.resolve();desktop=out/'desktop';calibration=out/'calibration';report={'status':'failed','scope':__doc__,'exe_sha256':hashlib.sha256(exe.read_bytes()).hexdigest(),'cases':[]};proc=None;client=None
 seed=None
 if args.seed_content:
  seed=read(args.seed_content/'library/index.json');seed_aliases=read(args.seed_content/'bundled-catalog.json');destination=desktop/'content';destination.mkdir(parents=True)
  shutil.copytree(args.seed_content/'library',destination/'library');shutil.copy2(args.seed_content/'bundled-catalog.json',destination/'bundled-catalog.json')
  seed_mixes={work:dict(music=.31,effects=.62,music_muted=True,effects_muted=False) for work in seed['works']};(destination/'sound-mixes.json').write_text(json.dumps(seed_mixes))
 try:
  proc=subprocess.Popen([str(exe),'--desktop-runtime','true','--desktop-data-root',str(desktop),'--data-root',str(calibration),'--tracking-python',str(common/'tracker/.venv/Scripts/python.exe'),'--tracking-root',str(ROOT/'tracker'),'-screen-fullscreen','0','-logFile',str(out/'runtime.log')]+(['--sdk-runtime',str(args.runtime.resolve())] if args.runtime else []))
  end=time.monotonic()+50;descriptor=desktop/'runtime/connection.json'
  while not descriptor.exists():
   assert proc.poll() is None and time.monotonic()<end,'Runtime discovery failed';time.sleep(.1)
  client=Client(read(descriptor));end=time.monotonic()+90
  while True:
   state=client.call('GetDesktopState',{})
   if state.get('ready'):break
   assert time.monotonic()<end,('Runtime initialization timeout',state);time.sleep(.1)
  display=state['displays'][0]['id']
  client.call('ConfigureDesktopCamera',{'camera_id':'simulation','camera_name':'合成追踪','simulate':True,'mode_id':None})
  end=time.monotonic()+30
  while True:
   sample=client.call('GetDesktopTracking',{'preview':False,'consume':True})
   if sample.get('tracking'):break
   assert time.monotonic()<end,'synthetic Tracker unavailable';time.sleep(.05)
  desc=sample['tracking']['descriptor'];pid=uuid.uuid4().hex
  saved={'schema_version':2,'mapping_type':'five_parameter_v1','id':pid,'revision':1,'name':'Quality synthetic','manual':dict(offsetX=0,offsetY=0,scaleX=4.38,scaleY=4.38,scaleZ=6.7,input_type='normalized_unmirrored_iris_v1',source={k:desc[k] for k in ['camera_id','head_model_id','estimator_id','intrinsics_id','geometry_id','actual_mode']},display_id=display,display_width=.53),'reference_ready':True,'display_units':'nominal_m'}
  presets=calibration/'presets';presets.mkdir(parents=True,exist_ok=True);(presets/(pid+'.json')).write_text(json.dumps(saved))
  client.call('DesktopCalibrationSelection',{'selected':pid,'applied':saved,'pause_when_invisible':False})
  content=client.call('GetContentState',{});aliases=content['bundled_catalog']
  if seed:
   actual=content['state'];assert actual['applied']==seed['applied'],'bundle update changed explicit applied revision'
   assert set(actual['works'])==set(seed['works']),'bundle update changed installation identities'
   for work in seed['works']:
    assert actual['works'][work]['name']==seed['works'][work]['name'] and content['sound_mixes'][work]==seed_mixes[work]
   for scene,alias in aliases.items():assert alias['work_id']==seed_aliases[scene]['work_id']
   stored=read(desktop/'content/library/index.json')
   assert all(stored['revisions'][key]==value for key,value in seed['revisions'].items()),'bundle update removed history'
   catalog=read(exe.parent/'SpatialWallpaper_Data/StreamingAssets/sdk-content/catalog.json')
   assert all(aliases[item['original_id']]['build_id']==item['build_id'] for item in catalog),'bundle did not advance to current build'
   report['bundle_upgrade']={'preserved_work_ids':list(actual['works']),'applied_unchanged':True,'old_revisions_unchanged':True,'mixes_unchanged':True,'new_aliases':aliases}
  updates=args.package_update+([['rainycafe',str(args.cafe)]] if args.cafe else []);report['package_updates']=[]
  for scene,package in updates:
   work=aliases[scene]['work_id'];revision=content['state']['works'][work]['latest_revision'];stage=uuid.uuid4();destination=desktop/'content/staging'/(str(stage)+'.spatial');shutil.copyfile(Path(package).resolve(),destination)
   report['package_updates'].append(client.call('UpdateContent',{'staging_id':str(stage),'work_id':work,'expected_revision':revision}))
  for artwork in ([] if args.skip_builtin else ['mita','pool'])+[aliases[n]['work_id'] for n in args.scenes]:
   row={'artwork':artwork};report['cases'].append(row)
   try:
    result=client.call('ApplyDesktop',{'artwork_id':artwork,'display_id':display,'preset_id':pid,'preset_revision':1});row['apply']={k:result.get(k) for k in ['succeeded','message','active_artwork','attached','content_ready']};print('APPLY',artwork,row['apply'],flush=True)
    if not result.get('succeeded'):raise AssertionError('apply failed: '+str(result))
    if artwork in ['mita','pool']:
     row['healthy_state']=client.call('GetDesktopState',{})['active_artwork']==artwork;assert row['healthy_state'],'builtin state differs from successful apply';continue
    time.sleep(2);start=time.monotonic();rows=[]
    while time.monotonic()-start<args.seconds:
     client.call('GetContentState',{});state=client.call('GetDesktopState',{});metrics=state.get('content_metrics',{})
     assert state.get('content_ready') and state.get('active_artwork')==artwork,'actual SDK session stopped during sampling: '+str(state)
     rows.append({'t':time.monotonic()-start,'metrics':metrics});time.sleep(.4)
    row['samples']=rows
   except Exception as error:row['error']=repr(error);print('ERROR',artwork,repr(error),flush=True)
  assert all('error' not in row and row.get('apply',{}).get('succeeded') for row in report['cases']),'quality regression case failed'
  for row in report['cases']:
   samples=row.get('samples',[])
   if samples:
    first=samples[0]['metrics']['frame_statistics'];last=samples[-1]['metrics']['frame_statistics'];assert last['presented_count']>first['presented_count'],'no actual presentation progress'
    row['actual_present_fps']=(last['presented_count']-first['presented_count'])*1000/(last['elapsed_ms']-first['elapsed_ms'])
  report['status']='passed-development-quality-regressions-not-RC'
 finally:
  if client:
   try:client.call('QuitDesktop',{})
   except Exception as e:report['quit_error']=repr(e)
   client.close()
  if proc:
   try:proc.wait(timeout=50)
   except subprocess.TimeoutExpired:report['cleanup_error']='normal Runtime exit timed out'
  report['runtime_exited']=proc is None or proc.poll() is not None
  if not report['runtime_exited'] or report.get('quit_error') or report.get('cleanup_error'):report['status']='failed-cleanup-unproven'
  (out/'report.json').write_text(json.dumps(report,indent=2,ensure_ascii=False));print('REPORT',out/'report.json',flush=True)
 assert report['status']=='passed-development-quality-regressions-not-RC',report['status']
if __name__=='__main__':main()
