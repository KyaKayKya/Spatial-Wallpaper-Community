"""Compare unaltered standalone frames under the same native fullscreen viewport."""
import json,os,shutil,subprocess,time,uuid,hashlib,argparse
from pathlib import Path
from PIL import Image,ImageChops,ImageStat
from run_e2e import Client,ROOT,OUT
from build_apps import validate_receipt

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--player-root',type=Path,default=OUT/'apps/player');parser.add_argument('--scene',choices=['parking','train'],default='parking');parser.add_argument('--report-prefix');parser.add_argument('--package',type=Path);args=parser.parse_args();args.player_root=args.player_root.resolve();args.report_prefix=args.report_prefix or args.scene+'-player';assert not args.package or args.report_prefix!=args.scene+'-player','Diagnostic package needs a separate report prefix'
 validate_receipt()
 from fingerprint_apps import closure
 reference_root=OUT/('reference-player' if args.scene=='parking' else args.scene+'-reference-player');receipt_path=OUT/'reports'/(args.scene+'-reference-source.json');reference_receipt=json.loads(receipt_path.read_text());reference_closure=closure(reference_root);assert reference_closure==reference_receipt['player_closure_sha256'],'Reference Player identity changed'
 player_closure=closure(args.player_root);run=OUT/'runs'/('visual-'+str(uuid.uuid4()));run.mkdir(parents=True)
 common=['-screen-fullscreen','1','-window-mode','borderless']
 reference=run/'reference.png'
 process=subprocess.Popen([str(reference_root/(args.scene.title()+'Reference.exe')),'--reference-output',str(reference),'--lighting-state-output',str(run/'reference-lighting.json'),*common,'-logFile',str(run/'reference.log')])
 try:
  assert process.wait(timeout=60)==0,'Reference Player failed'
 finally:
  if process.poll() is None:process.terminate();process.wait(timeout=10)
 assert reference.exists(),'Reference frame missing'
 player=run/'player';descriptor=player/'runtime/connection.json';capture=run/'imported.png';client=None
 process=subprocess.Popen([str(args.player_root/'ContentPlayer.exe'),'--content-data-root',str(player),'--content-frame-output',str(capture),'--content-visual-time','2.5','--lighting-state-output',str(run/'imported-lighting.json'),*common,'-logFile',str(run/'imported.log')])
 try:
  deadline=time.monotonic()+30
  while not descriptor.exists():
   assert process.poll() is None,'Content Player exited'
   if time.monotonic()>deadline:raise TimeoutError('discovery')
   time.sleep(.1)
  client=Client(json.loads(descriptor.read_text(encoding='utf-8-sig')))
  state=client.call('GetContentState',{});assert not state['camera_started']
  package_path=args.package or OUT/(args.scene.title()+'.spatial');staging=str(uuid.uuid4());shutil.copy2(package_path,player/'staging'/(staging+'.spatial'))
  imported=client.call('ImportContent',{'staging_id':staging});time.sleep(3)
  applied=client.call('ApplyContent',{'display_id':state['display_id'],'work_id':imported['work_id']});assert applied['succeeded'],applied
  deadline=time.monotonic()+10
  while not capture.exists():
   if time.monotonic()>deadline:raise TimeoutError('frame')
   time.sleep(.1)
  time.sleep(1);state=client.call('GetContentState',{});assert state['render_errors']==0 and not state['camera_started']
  source_metrics=json.loads(reference.with_suffix('.json').read_text());imported_metrics=state['presentation_metrics']
  for key in ['width','height','center_x','center_y','scale','pixels','fixed_time','content_depth']:
   a,b=source_metrics[key],imported_metrics[key]
   assert a==b or isinstance(a,(int,float)) and abs(a-b)<1e-6,(key,a,b)
  assert all(abs(a-b)<1e-6 for a,b in zip(source_metrics['eye'],imported_metrics['eye']))
  a=Image.open(reference).convert('RGB');b=Image.open(capture).convert('RGB');assert a.size==b.size
  difference=ImageChops.difference(a,b);stats=ImageStat.Stat(difference);mean=sum(stats.mean)/3/255;maximum=max(v[1] for v in difference.getextrema())
  sparse=sum(max(pixel)>8 for pixel in difference.getdata())/(a.width*a.height)
  # Allow sparse rasterization edge differences, while retaining a strict
  # global error budget. Animated series use a separate temporal assessment.
  report={'passed':mean<=.0001 and maximum<=32 and sparse<=.00001,'scope':'Real standalone library '+args.scene.title()+' vs imported package, identical physical viewport and fixed time; software visual comparison only','mean_rgb_error':mean,'maximum_channel_error':maximum,'pixels_over_8_fraction':sparse,'thresholds':{'mean':.0001,'maximum':32,'pixels_over_8_fraction':.00001},'reference_metrics':source_metrics,'imported_metrics':imported_metrics,'run_directory':run.relative_to(ROOT).as_posix(),'package_sha256':hashlib.sha256(package_path.read_bytes()).hexdigest()}
  report['player_root']=str(args.player_root.relative_to(ROOT));report['production_player']=args.player_root.resolve()==(OUT/'apps/player').resolve();report['player_closure_sha256']=player_closure;report['reference_player_closure_sha256']=reference_closure;report['platform_source_receipt_sha256']=hashlib.sha256((OUT/'reports/platform-source-receipt.json').read_bytes()).hexdigest();report['reference_source_receipt_sha256']=hashlib.sha256(receipt_path.read_bytes()).hexdigest()
  assert closure(args.player_root)==player_closure and closure(reference_root)==reference_closure,'Player binaries changed during comparison'
  for name,path in [(args.report_prefix+'-reference.png',reference),(args.report_prefix+'-imported.png',capture)]:shutil.copy2(path,OUT/'reports'/name)
  (OUT/'reports'/(args.report_prefix+'-visual.json')).write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2))
  if not report['passed']:raise AssertionError('Standalone appearance mismatch')
 except Exception as error:
  failure={'passed':False,'scope':'Standalone visual validation failed; no stage acceptance','error':str(error),'run_directory':run.relative_to(ROOT).as_posix(),'player_root':str(args.player_root.relative_to(ROOT))}
  if 'source_metrics' in locals():failure['reference_metrics']=source_metrics
  if 'imported_metrics' in locals():failure['imported_metrics']=imported_metrics
  (run/'failure.json').write_text(json.dumps(failure,indent=2)+'\n')
  if 'mean' not in locals():(OUT/'reports'/(args.report_prefix+'-visual.json')).write_text(json.dumps(failure,indent=2)+'\n')
  raise
 finally:
  if client:client.close()
  if process.poll() is None:process.terminate();process.wait(timeout=15)
if __name__=='__main__':main()
