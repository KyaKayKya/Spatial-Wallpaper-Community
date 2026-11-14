"""Bounded ETW sample of formal Runtime's eight actual native child PIDs.

Reuses existing PresentMon/QPC analysis. Does not call their screenshot, focus,
window-hiding or input helpers. Desktop occlusion is uncontrolled; no image,
moving-eye, bridge comparison, full task3 acceptance or RC claim.
"""
from pathlib import Path
import argparse,hashlib,json,subprocess,sys,time,uuid
ROOT=Path(__file__).resolve().parents[2]
from check_native_runtime import read,until,Client,psutil
from check_native_baseline_elevated import ElevatedCapture,qpc_ms
from check_native_baseline import summarize,require_complete_timing
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def main():
 originals=['showcase','pool','train','parking','minishotgame','limpool','rainycafe','mita']
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--launcher',type=Path,required=True);p.add_argument('--profile',type=Path,required=True);p.add_argument('--artwork',choices=originals,action='append');p.add_argument('--capture-seconds',type=int,choices=[60,240],default=240);a=p.parse_args();artworks=a.artwork or originals
 assert len(set(artworks))==len(artworks) and a.capture_seconds>=len(artworks)*20+15,'capture bound too short for selected samples'
 launcher=a.launcher.resolve();out=ROOT/'Validation/NB'/uuid.uuid4().hex[:12];out.mkdir(parents=True);print(out,flush=True)
 desktop=out/'desktop';calibration=out/'calibration';(calibration/'presets').mkdir(parents=True);profile=read(a.profile);(calibration/'presets'/(profile['id']+'.json')).write_bytes(a.profile.read_bytes())
 executable=ROOT/'Validation/Dependencies/PresentMon/2.6.0/PresentMon-2.6.0-x64.exe';assert digest(executable)=='b2a706bc6ad475749e3b7e3409263aa1e6906d45bdcf993f6dbc0f660188f1af'
 report={'status':'failed','scope':__doc__,'presentmon_sha256':digest(executable),'assembly_sha256':digest(launcher.parent/'SpatialWallpaper_Data/Managed/Assembly-CSharp.dll'),'source_sha256':digest(Path(__file__)),'capture_limit_seconds':a.capture_seconds,'sample_seconds_per_work':15,'cases':[],'occlusion':'uncontrolled; computer-use unavailable; no exposed-desktop/image claim','not_verified':['bridge/no-bridge/script-fault comparison','moving eyes','visible image quality','stable60/RC','physical multi-target']}
 process=None;client=None;monitor=None;children=[]
 def state():return client.call('GetDesktopState',{})
 try:
  process=subprocess.Popen([str(launcher),'--desktop-runtime','true','--desktop-data-root',str(desktop),'--data-root',str(calibration),'--tracking-scenario','fixed','--native-presentation-diagnostics','true','-screen-fullscreen','0','-screen-width','800','-screen-height','450','-logFile',str(out/'runtime.log')])
  def discovery():
   if process.poll() is not None:raise RuntimeError('formal Launch exited')
   try:
    d=read(desktop/'runtime/connection.json');return d if psutil.pid_exists(d['pid']) else None
   except (OSError,ValueError):return None
  client=Client(until(discovery,'Runtime discovery'));until(lambda:state()['ready'],'Runtime ready',70)
  client.call('DesktopCalibrationSelection',{'selected':profile['id'],'applied':profile,'pause_when_invisible':False});client.call('ConfigureDesktopCamera',{'camera_id':'simulation','camera_name':'Native ETW fixed derived pose','mode_id':None,'simulate':True})
  for artwork in artworks:
   result=client.call('ApplyDesktop',{'artwork_id':artwork,'display_id':profile['manual']['display_id'],'preset_id':profile['id'],'preset_revision':profile['revision']});assert result['succeeded'],result
   def native():
    s=state();n=s['native_targets'][0]['state'];return s if n['frame']>30 and n.get('tracking_fresh') and not n['paused'] else None
   before=until(native,'actual unpaused tracked renderer');n=before['native_targets'][0]['state'];child=psutil.Process(n['pid']);children.append(child)
   assert child.ppid()==before['pid'] and Path(child.exe()).resolve()==(launcher.parent/'SpatialWallpaper.exe').resolve() and n['render_errors']==0
   if monitor is None:
    monitor=ElevatedCapture(executable.resolve(),['--process_name','SpatialWallpaper.exe','--output_file',str(out/'presents.csv'),'--qpc_time_ms','--session_name','SpatialNativeChildren-'+uuid.uuid4().hex,'--timed',str(a.capture_seconds),'--terminate_after_timed','--no_console_stats'])
    until(lambda:(out/'presents.csv').exists() if monitor.poll() is None else False,'official ETW capture starts',30)
   assert monitor.poll() is None,'ETW ended before sample';time.sleep(2)
   start=qpc_ms();time.sleep(15);end=qpc_ms();after=state();assert after['native_targets'][0]['state']['pid']==n['pid'] and after['native_targets'][0]['state']['render_errors']==0
   report['cases'].append({'artwork':artwork,'child_pid':n['pid'],'child_creation_time':child.create_time(),'parent_pid':before['pid'],'start_qpc_ms':start,'end_qpc_ms':end,'before':before,'after':after});(out/'progress.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print('sampled',artwork,n['pid'],flush=True)
  assert client.call('QuitDesktop',{})['succeeded'];client.close();client=None;assert process.wait(timeout=40)==0
  for child in children:child.wait(timeout=10)
  report['presentmon_exit_code']=monitor.wait(timeout=150);assert report['presentmon_exit_code']==0
  for case in report['cases']:
   case['timings']=summarize(out/'presents.csv',case['child_pid'],case['start_qpc_ms'],case['end_qpc_ms'],60)
   case['complete_timing']=require_complete_timing(case['timings'],case['end_qpc_ms']-case['start_qpc_ms'],60)
  report['csv_sha256']=digest(out/'presents.csv');report['status']='captured-native-child-ETW-development-only-uncontrolled-occlusion'
 except Exception as failure:report['error']=repr(failure);raise
 finally:
  if client:
   try:client.call('QuitDesktop',{});client.close()
   except Exception as failure:report['cleanup_error']=repr(failure)
  if process and process.poll() is None:
   try:process.wait(timeout=40)
   except subprocess.TimeoutExpired:report['cleanup_unproven']=True
  report['live_renderer_pins']=[]
  for child in children:
   try:child.wait(timeout=5)
   except psutil.TimeoutExpired:report['live_renderer_pins'].append(child.pid)
  if monitor:
   try:
    if monitor.poll() is None:monitor.terminate();report['capture_terminated_after_failure']=True
   finally:monitor.close()
  if report['status'].startswith('captured-') and (not report['cases'] or not all(case.get('complete_timing') for case in report['cases'])):report['status']='failed-incomplete-native-child-timing'
  if report.get('cleanup_error') or report.get('cleanup_unproven') or report['live_renderer_pins']:report['status']='failed-cleanup-unproven'
  (out/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding='utf-8');print(report['status'],out,flush=True)
  if sys.exc_info()[0] is None and not report['status'].startswith('captured-'):raise RuntimeError(report['status'])
if __name__=='__main__':main()
