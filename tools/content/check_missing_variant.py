"""A missing Lit variant must fail actual application, never silently show fallback pixels."""
import argparse,hashlib,json,shutil,subprocess,time,uuid,zipfile
from pathlib import Path
from run_e2e import Client,ROOT,OUT
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--player-root',type=Path,default=OUT/'apps/player');args=parser.parse_args();player=args.player_root.resolve();run=OUT/'runs'/str(uuid.uuid4());run.mkdir(parents=True)
 from fingerprint_apps import closure
 from build_apps import validate_receipt
 if player==(OUT/'apps/player').resolve():validate_receipt()
 player_closure=closure(player)
 files={}
 with zipfile.ZipFile(OUT/'static-validation/sample.spatial') as z:
  for name in z.namelist():files[name]=z.read(name)
 scene=json.loads(files['scene.json']);material=next(m for m in scene['materials'] if m['shader']=='Universal Render Pipeline/Lit' and m['bundle']=='');material['keywords'].append('_DETAIL_MULX2');scene['effects']=[{'kind':'background','node':0,'color':[.2,.4,.7,1]},{'kind':'clip-depth','node':0,'depth':9},{'kind':'camera','node':0,'hdr':False,'msaa':2,'opaque':True,'depth':True,'post':False,'renderer':'forward'}];files['scene.json']=json.dumps(scene,separators=(',',':')).encode()
 manifest=json.loads(files['manifest.json']);manifest['capabilities'].append('controlled-scene-v1');manifest['files']['scene.json']={'size':len(files['scene.json']),'sha256':hashlib.sha256(files['scene.json']).hexdigest()};manifest.pop('build_id');manifest['build_id']=hashlib.sha256(json.dumps(manifest,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()).hexdigest();files['manifest.json']=json.dumps(manifest,separators=(',',':'),ensure_ascii=False).encode();package=run/'missing-variant.spatial'
 with zipfile.ZipFile(package,'w') as z:
  for name,data in files.items():z.writestr(name,data)
 data_root=run/'data';descriptor=data_root/'runtime/connection.json';log=run/'runtime.log';client=None;process=subprocess.Popen([str(player/'ContentPlayer.exe'),'--content-data-root',str(data_root),'-screen-fullscreen','0','-screen-width','960','-screen-height','540','-logFile',str(log)])
 try:
  deadline=time.monotonic()+30
  while not descriptor.exists():
   if process.poll() is not None:raise RuntimeError('Player exited')
   if time.monotonic()>deadline:raise TimeoutError('discovery')
   time.sleep(.1)
  client=Client(json.loads(descriptor.read_text(encoding='utf-8-sig')));state=client.call('GetContentState',{})
  while not state['runtime_ready']:
   if time.monotonic()>deadline:raise TimeoutError('Runtime readiness: '+json.dumps(state))
   time.sleep(.1);state=client.call('GetContentState',{})
  staging=str(uuid.uuid4());shutil.copy2(package,data_root/'staging'/(staging+'.spatial'));imported=client.call('ImportContent',{'staging_id':staging});time.sleep(3);result=client.call('ApplyContent',{'display_id':state['display_id'],'work_id':imported['work_id']});time.sleep(1);state=client.call('GetContentState',{});messages=sorted(set(line for line in log.read_text(encoding='utf-8-sig',errors='replace').splitlines() if 'variant ' in line and 'not found' in line));missing=any('_DETAIL_MULX2' in line for line in messages)
  empty_rejected=missing and not result['succeeded'] and not state['presentation_ready'] and not state['state']['applied'] and not state['camera_started']
  good_stage=str(uuid.uuid4());shutil.copy2(OUT/'static-validation/sample.spatial',data_root/'staging'/(good_stage+'.spatial'));good=client.call('ImportContent',{'staging_id':good_stage});good_result=client.call('ApplyContent',{'display_id':state['display_id'],'work_id':good['work_id']});assert good_result['succeeded'],good_result;before=client.call('GetContentState',{});background=before['presentation_metrics']['background'];assert background==[0,0,0,1]
  retry=client.call('ApplyContent',{'display_id':state['display_id'],'work_id':imported['work_id']});after=client.call('GetContentState',{});restored=not retry['succeeded'] and retry['restored'] and after['presentation_ready'] and after['state']['applied'][state['display_id']]['revision_id']==good['revision_id'] and after['presentation_metrics']['background']==background and after['presentation_metrics']['content_depth']==before['presentation_metrics']['content_depth']
  camera_restored=after['presentation_metrics']['camera_settings']==before['presentation_metrics']['camera_settings'];passed=empty_rejected and restored and camera_restored
  report={'passed':passed,'scope':'Actual standalone missing variant: reject from empty; repeat failure restores old revision, background, clipping and camera settings after bad content changed them','player_root':str(player.relative_to(ROOT)),'player_closure_sha256':player_closure,'platform_source_receipt_sha256':hashlib.sha256((OUT/'reports/platform-source-receipt.json').read_bytes()).hexdigest(),'application':result,'retry_application':retry,'empty_rejected':empty_rejected,'old_revision_and_background_restored':restored,'camera_settings_restored':camera_restored,'camera_before':before['presentation_metrics']['camera_settings'],'camera_after':after['presentation_metrics']['camera_settings'],'render_errors':after['render_errors'],'missing_variants':messages,'run_directory':run.relative_to(ROOT).as_posix()};assert closure(player)==player_closure,'Player changed during negative check';(OUT/'reports/missing-variant.json').write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));assert passed,'Missing shader variant was not safely rejected or restored'
 finally:
  if client:client.close()
  if process.poll() is None:
   process.terminate()
   try:process.wait(timeout=15)
   except subprocess.TimeoutExpired:process.kill();process.wait(timeout=15)
if __name__=='__main__':main()
