"""Compare natural playback against two independent original-scene runs."""
import argparse,hashlib,json,math,shutil,statistics,subprocess,time,uuid
from pathlib import Path
from PIL import Image,ImageChops,ImageStat
from run_e2e import ROOT,OUT,Client
from build_apps import validate_receipt
from fingerprint_apps import closure

def features(folder):
 data=json.loads((folder/'series.json').read_text());assert data['render_errors']==0,data;frames=data['frames'];assert len(frames)==32
 vectors=[];motion=[];previous=None
 for frame in frames:
  with Image.open(folder/frame['file']) as original:
   image=original.convert('RGB');width,height=image.size;assert frame['pixels']==[width,height]
   vector=[]
   for y in range(8):
    for x in range(12):vector.extend(v/255 for v in ImageStat.Stat(image.crop((x*width//12,y*height//8,(x+1)*width//12,(y+1)*height//8))).mean)
   vectors.append(vector)
   if previous is not None:motion.append(sum(ImageStat.Stat(ImageChops.difference(previous,image)).mean)/765)
   previous=image
 return data,vectors,statistics.mean(motion)

def assess(scene,controls,imported):
 a,av,am=controls[0];b,bv,bm=controls[1];actual,iv,im=imported;reference=a['frames'][0];current=actual['frames'][0]
 for key in ['pixels','pixel_rect','content_depth','eye','camera_settings','probe_count','reflection_cameras']:
  for data in [a,b,actual]:
   assert all(frame[key]==reference[key] for frame in data['frames']),('context-or-resource-mismatch',key,reference[key],current[key])
 assert all(not frame['invert_culling'] for data in [a,b,actual] for frame in data['frames']),'Global culling leaked'
 # Material shaders and bound reflection sizes must remain present independently
 # of frame timing. Train's lossless mesh partition may add renderer instances.
 shaders=lambda frame:sorted(set(material['shader'] for renderer in frame['renderers'] for material in renderer['materials']))
 assert shaders(reference)==shaders(current),('shader-inventory',shaders(reference),shaders(current))
 if scene=='pool':
  for key in ['ambient_mode','ambient_colors','lightprobe_count']:
   assert all(frame[key]==reference[key] for data in [a,b,actual] for frame in data['frames']),('ambient-input-mismatch',key,reference[key],current[key])
  for key in ['ambient_probe','ring_probe']:
   assert all(len(frame[key])==27 and max(abs(x-y) for x,y in zip(frame[key],reference[key]))<=1e-5 for data in [a,b,actual] for frame in data['frames']),('ambient-sh-input-mismatch',key)
  for data in [a,b,actual]:
   for frame in data['frames']:
    outputs=[material['reflection'] for renderer in frame['renderers'] for material in renderer['materials'] if material['reflection']]
    assert outputs==[[reference['pixels'][0]//2,reference['pixels'][1]//2]],('reflection-output-missing-or-size',outputs)
  names={r['name'] for r in reference['renderers']};assert names=={r['name'] for r in current['renderers']},'Pool geometry missing'
  ring=next(name for name in names if name.startswith('Floating ring'))
  for name in names-{ring}:
   original=next(r for r in reference['renderers'] if r['name']==name);candidate=next(r for r in current['renderers'] if r['name']==name)
   for field in ['position','bounds_center','bounds_size']:assert max(abs(x-y) for x,y in zip(original[field],candidate[field]))<=.00001,('static-geometry',name,field)
  def excursion(data):
   positions=[next(r['position'] for r in f['renderers'] if r['name']==ring) for f in data['frames']]
   return math.sqrt(sum((max(p[c] for p in positions)-min(p[c] for p in positions))**2 for c in range(3)))
  original_motion=(excursion(a)+excursion(b))/2;ring_motion=excursion(actual);assert original_motion>1e-5 and .25*original_motion<=ring_motion<=4*original_motion,('ring-motion',original_motion,ring_motion)
 deviations=[];absolute=[]
 for index in range(len(av[0])):
  first=[v[index] for v in av];second=[v[index] for v in bv];candidate=[v[index] for v in iv];mean=statistics.mean(first+second);error=abs(statistics.mean(candidate)-mean)
  # The observed original-to-original drift and temporal spread define the
  # allowance. It is not a per-pixel equality test of unrelated animation phases.
  tolerance=max(1/255,3*abs(statistics.mean(first)-statistics.mean(second))+2*statistics.pstdev(first+second))
  deviations.append(error/tolerance);absolute.append(error)
 reference_motion=(am+bm)/2;motion_ok=reference_motion>1e-6 and .25*reference_motion<=im<=4*reference_motion
 failed_fraction=sum(v>1 for v in deviations)/len(deviations);mean_error=statistics.mean(absolute)
 passed=failed_fraction<=.05 and max(deviations)<=3 and mean_error<=.005 and motion_ok
 return {'passed':passed,'tile_channels_outside_reference_envelope_fraction':failed_fraction,'maximum_envelope_ratio':max(deviations),'mean_tile_rgb_error':mean_error,'reference_motion_score':[am,bm],'imported_motion_score':im,'motion_present_and_comparable':motion_ok,'thresholds':{'outside_fraction':.05,'maximum_envelope_ratio':3,'mean_tile_rgb_error':.005,'motion_ratio':[.25,4]},'scope':'32 naturally running frames per run; two independent original runs establish temporal allowance; no clock/seed/pose/material override; fixed frames and scene-specific lifecycle remain separate checks'}

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--scene',choices=['parking','train','pool'],required=True);parser.add_argument('--package',type=Path);parser.add_argument('--report-prefix');args=parser.parse_args();prefix=args.report_prefix or args.scene+'-natural';assert not args.package or prefix!=args.scene+'-natural'
 validate_receipt();player_receipt=json.loads((OUT/'reports/series-player-source.json').read_text());assert player_receipt['platform_source_receipt_sha256']==hashlib.sha256((OUT/'reports/platform-source-receipt.json').read_bytes()).hexdigest(),'Observer compiled against stale platform'
 player_root=OUT/'series-player';player_closure=closure(player_root);assert player_closure==player_receipt['player_closure_sha256']
 reference_root=OUT/('reference-player' if args.scene=='parking' else args.scene+'-reference-player');receipt=OUT/'reports'/(args.scene+'-reference-source.json');reference_receipt=json.loads(receipt.read_text());reference_closure=closure(reference_root);assert reference_closure==reference_receipt['player_closure_sha256']
 run=OUT/'runs'/('series-'+str(uuid.uuid4()));run.mkdir(parents=True);report_path=OUT/'reports'/(prefix+'.json');common=['-screen-fullscreen','1','-window-mode','borderless'];package=args.package or OUT/(args.scene.title()+'.spatial')
 try:
  for name in ['reference-a','reference-b']:
   process=subprocess.Popen([str(reference_root/(args.scene.title()+'Reference.exe')),'--series-output',str(run/name),'--series-warmup','3' if name=='reference-a' else '20',*common,'-logFile',str(run/(name+'.log'))])
   try:assert process.wait(timeout=90)==0,('Original natural playback failed',name)
   finally:
    if process.poll() is None:process.terminate();process.wait(timeout=15)
  root=run/'data';descriptor=root/'runtime/connection.json';client=None;process=subprocess.Popen([str(player_root/'ContentPlayer.exe'),'--content-data-root',str(root),'--series-output',str(run/'imported'),*common,'-logFile',str(run/'imported.log')])
  try:
   deadline=time.monotonic()+30
   while not descriptor.exists():
    assert process.poll() is None
    if time.monotonic()>deadline:raise TimeoutError('Runtime discovery')
    time.sleep(.1)
   client=Client(json.loads(descriptor.read_text(encoding='utf-8-sig')));state=client.call('GetContentState',{});assert state['runtime_ready'] and not state['camera_started'];staging=str(uuid.uuid4());shutil.copy2(package,root/'staging'/(staging+'.spatial'));imported=client.call('ImportContent',{'staging_id':staging});time.sleep(3);result=client.call('ApplyContent',{'display_id':state['display_id'],'work_id':imported['work_id']});assert result['succeeded'],result;state=client.call('GetContentState',{});assert state['render_errors']==0 and not state['camera_started'];client.close();client=None;assert process.wait(timeout=90)==0,'Imported natural playback failed'
  finally:
   if client:client.close()
   if process.poll() is None:process.terminate();process.wait(timeout=15)
  report=assess(args.scene,[features(run/'reference-a'),features(run/'reference-b')],features(run/'imported'));report.update({'run_directory':run.relative_to(ROOT).as_posix(),'package_sha256':hashlib.sha256(package.read_bytes()).hexdigest(),'observer_player_closure_sha256':player_closure,'reference_player_closure_sha256':reference_closure,'platform_source_receipt_sha256':player_receipt['platform_source_receipt_sha256'],'reference_receipt_sha256':hashlib.sha256(receipt.read_bytes()).hexdigest()});assert closure(player_root)==player_closure and closure(reference_root)==reference_closure,'Player changed during run';report_path.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));assert report['passed'],'Natural playback differs beyond the original repeat envelope'
 except Exception as error:
  report=locals().get('report',{'scope':'Failed natural playback run; no acceptance','run_directory':run.relative_to(ROOT).as_posix()});report.update({'passed':False,'error':str(error)});report_path.write_text(json.dumps(report,indent=2)+'\n')
  raise
if __name__=='__main__':main()
