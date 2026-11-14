"""Actual formal Runtime native/unchanged saved-blueprint switching; no GUI claim."""
from pathlib import Path
import argparse,hashlib,json,shutil,subprocess,sys,uuid
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/desktop'))
from check_native_runtime import read,until,psutil,Client
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def inventory(p):return {x.relative_to(p).as_posix():digest(x) for x in p.rglob('*') if x.is_file()}
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--launcher',type=Path,required=True);p.add_argument('--profile',type=Path,required=True);p.add_argument('--blueprints',type=Path,required=True);a=p.parse_args()
 launcher=a.launcher.resolve();source=a.blueprints.resolve();original=inventory(source)
 # Keep the existing importer fixture below Win32 MAX_PATH, as the original
 # baseline does; its temporary source name includes both digest and GUID.
 out=ROOT/'Validation/NB'/uuid.uuid4().hex[:12];out.mkdir(parents=True);print(out,flush=True)
 desktop=out/'desktop';desktop.mkdir();shutil.copytree(source,desktop/'blueprints');profile=read(a.profile);calibration=out/'calibration';(calibration/'presets').mkdir(parents=True);shutil.copy2(a.profile,calibration/'presets'/(profile['id']+'.json'))
 index=read(source/'index.json');work,entry=next(iter(index['works'].items()));artwork='blueprint-'+work+'-'+entry['latest'];display=profile['manual']['display_id'];request={'artwork_id':artwork,'display_id':display,'preset_id':profile['id'],'preset_revision':profile['revision']}
 report={'status':'failed','checks':[],'scope':__doc__,'blueprint_input_sha256':original,'assembly_sha256':digest(launcher.parent/'SpatialWallpaper_Data/Managed/Assembly-CSharp.dll'),'source_sha256':digest(Path(__file__))};process=None;client=None;children=[]
 def state():return client.call('GetDesktopState',{})
 def launch(stage):
  nonlocal process,client
  process=subprocess.Popen([str(launcher),'--desktop-runtime','true','--desktop-data-root',str(desktop),'--data-root',str(calibration),'--native-presentation-diagnostics','true','-screen-fullscreen','0','-screen-width','800','-screen-height','450','-logFile',str(out/(stage+'.log'))])
  def discovery():
   if process.poll() is not None:raise RuntimeError('formal Launch exited')
   try:
    d=read(desktop/'runtime/connection.json');return d if psutil.pid_exists(d['pid']) else None
   except (OSError,ValueError):return None
  client=Client(until(discovery,'formal Runtime discovery'));until(lambda:state()['ready'],'formal ready',70)
 def quit():
  nonlocal client
  assert client.call('QuitDesktop',{})['succeeded'];client.close();client=None;assert process.wait(timeout=40)==0
 try:
  launch('first');client.call('DesktopCalibrationSelection',{'selected':profile['id'],'applied':profile,'pause_when_invisible':False})
  assert client.call('ApplyDesktop',dict(request,artwork_id='pool'))['succeeded'];s=state();child=psutil.Process(s['native_targets'][0]['state']['pid']);children.append(child)
  result=client.call('ApplyDesktop',request);report['blueprint_apply']=result;assert result['succeeded'],result;child.wait(timeout=10);s=state();assert s['active_artwork']==artwork and s['scene_loaded'] and s['attached'] and not s['native_targets'] and s['render_errors']==0
  assert s['applied_state']['targets'][0]['artwork_id']==artwork and not s['camera_started'];expected=read(desktop/'applied.json');report['blueprint_state']=s;report['checks'].append('full saved blueprint replaces native owner using unchanged original blueprint scene')
  quit();launch('restart');s=state();assert s['active_artwork']==artwork and s['scene_loaded'] and s['attached'] and s['applied_state']==expected and s['render_errors']==0;report['checks'].append('formal Launch restores exact saved blueprint revision and profile')
  assert client.call('ApplyDesktop',dict(request,artwork_id='showcase'))['succeeded'];s=state();assert s['active_artwork']=='showcase' and len(s['native_targets'])==1 and s['native_targets'][0]['attached'];child=psutil.Process(s['native_targets'][0]['state']['pid']);children.append(child);report['checks'].append('blueprint to full native Showcase transfers target ownership')
  quit();child.wait(timeout=10);assert inventory(source)==original;report['checks'].append('normal quit releases renderer and original blueprint fixture stays byte-identical');report['status']='passed-native-blueprint-control-development-only'
 except Exception as failure:report['error']=repr(failure);raise
 finally:
  if client:
   try:quit()
   except Exception as failure:report['cleanup_error']=repr(failure)
  if process and process.poll() is None:
   try:process.wait(timeout=40)
   except subprocess.TimeoutExpired:report['cleanup_unproven']=True
  report['live_renderer_pins']=[]
  for child in children:
   try:child.wait(timeout=5)
   except psutil.TimeoutExpired:report['live_renderer_pins'].append(child.pid)
  if report['live_renderer_pins']:report['status']='failed-cleanup-unproven'
  report['original_fixture_unchanged']=inventory(source)==original
  if not report['original_fixture_unchanged']:report['status']='failed-original-fixture-changed'
  if report.get('cleanup_error') or report.get('cleanup_unproven'):report['status']='failed-cleanup-unproven'
  (out/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(report['status'],flush=True)
  if sys.exc_info()[0] is None and report['status']!='passed-native-blueprint-control-development-only':raise RuntimeError('native blueprint final verification failed: '+report['status'])
if __name__=='__main__':main()
