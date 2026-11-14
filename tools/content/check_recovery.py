"""Actual Runtime startup damage, display selection and >64KiB library checks."""
from pathlib import Path
import argparse,hashlib,json,shutil,subprocess,time,uuid,zipfile
from run_e2e import Client
from build_apps import OUT,validate_receipt
from fingerprint_apps import closure

def main():
 parser=argparse.ArgumentParser();parser.add_argument('--baseline',action='store_true');args=parser.parse_args()
 if not args.baseline:validate_receipt()
 package=OUT/'static-validation/sample.spatial';install=closure(OUT/'apps/player');cases=[];process=None;client=None
 report=OUT/'reports'/('review-recovery-red.json' if args.baseline else 'review-recovery.json')
 if report.exists():report.unlink()
 def stop():
  nonlocal process,client
  if client:client.close();client=None
  if process and process.poll() is None:process.terminate();process.wait(timeout=15)
 def launch(run):
  nonlocal process,client
  startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=1
  process=subprocess.Popen([str(OUT/'apps/player/ContentPlayer.exe'),'--content-data-root',str(run),'-screen-fullscreen','0','-screen-width','960','-screen-height','540','-logFile',str(run/'runtime.log')],startupinfo=startup)
  deadline=time.monotonic()+20
  while time.monotonic()<deadline:
   if process.poll() is not None:raise RuntimeError('Runtime exited during discovery')
   try:data=json.loads((run/'runtime/connection.json').read_text(encoding='utf-8-sig'))
   except (FileNotFoundError,PermissionError,json.JSONDecodeError):time.sleep(.1);continue
   if data['pid']==process.pid:client=Client(data);return
   time.sleep(.1)
  raise TimeoutError('discovery')
 def ready():
  deadline=time.monotonic()+35
  while time.monotonic()<deadline:
   value=client.call('GetContentState',{})
   if value['runtime_ready']:return value
   time.sleep(.1)
  raise TimeoutError('Runtime never accepts normal commands after recovery failure')
 def empty():
  run=OUT/'runs'/str(uuid.uuid4());run.mkdir(parents=True);launch(run);state=ready();display=state['display_id'];stop();return run,display
 files={}
 with zipfile.ZipFile(package) as archive:
  files={n:archive.read(n) for n in archive.namelist()}
 manifest=json.loads(files['manifest.json']);build=manifest['build_id'];work=str(uuid.uuid4());revision=str(uuid.uuid4())
 def index(run,display,mode):
  directory=run/'library/builds';directory.mkdir(parents=True,exist_ok=True);target=directory/(build+'.spatial');shutil.copy2(package,target)
  state={'schema':1,'builds':{build:{'template_id':manifest['template_id'],'name':manifest['name']}},'works':{work:{'name':manifest['name'],'default_build':build,'latest_revision':revision}},'revisions':{revision:{'work_id':work,'build_id':build,'parameters':{}}},'applied':{display:{'work_id':work,'revision_id':revision}}}
  if mode=='missing':target.unlink()
  elif mode=='corrupt':target.write_bytes(b'broken archive')
  elif mode=='load-failure':
   altered=dict(files);scene=json.loads(altered['scene.json']);next(m for m in scene['materials'] if m['bundle'])['shader']='SdkSample/Missing';altered['scene.json']=json.dumps(scene,separators=(',',':')).encode();m=json.loads(altered['manifest.json']);m['files']['scene.json']={'size':len(altered['scene.json']),'sha256':hashlib.sha256(altered['scene.json']).hexdigest()};m.pop('build_id');b=hashlib.sha256(json.dumps(m,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()).hexdigest();m['build_id']=b;altered['manifest.json']=json.dumps(m,separators=(',',':')).encode()
   with zipfile.ZipFile(directory/(b+'.spatial'),'w') as archive:
    for name,value in altered.items():archive.writestr(name,value)
   state['builds'][b]=state['builds'].pop(build);state['works'][work]['default_build']=b;state['revisions'][revision]['build_id']=b
  elif mode in ['other-first','other-only']:
   wrong=str(uuid.uuid4());state['revisions'][wrong]={'work_id':work,'build_id':'0'*64,'parameters':{}};state['applied']={'different-display':{'work_id':work,'revision_id':wrong},**(state['applied'] if mode=='other-first' else {})}
  elif mode=='large':
   state['applied']={};state['works']={};state['revisions']={}
   for i in range(500):
    w=str(uuid.uuid4());r=str(uuid.uuid4());state['works'][w]={'name':'作品'+str(i)+'测'*120,'default_build':build,'latest_revision':r};state['revisions'][r]={'work_id':w,'build_id':build,'parameters':{}}
  (run/'library/index.json').write_text(json.dumps(state,ensure_ascii=False),encoding='utf-8');return state
 try:
  for mode in ['missing','corrupt','load-failure','other-first','other-only','large']:
   try:
    run,display=empty();expected=index(run,display,mode);launch(run);state=ready()
    if mode in ['missing','corrupt','load-failure']:
     assert state['startup_error'] and not state['presentation_ready'];assert state['state']['applied'][display]==expected['applied'][display]
     stage=str(uuid.uuid4());alternate=dict(files);m=json.loads(alternate['manifest.json']);m['name']='Recovery alternative';m.pop('build_id');m['build_id']=hashlib.sha256(json.dumps(m,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()).hexdigest();alternate['manifest.json']=json.dumps(m,separators=(',',':')).encode()
     with zipfile.ZipFile(run/'staging'/(stage+'.spatial'),'w') as archive:
      for name,value in alternate.items():archive.writestr(name,value)
     imported=client.call('ImportContent',{'staging_id':stage})
     result=client.call('ApplyContent',{'display_id':display,'work_id':imported['work_id']});assert result['succeeded'],result;assert client.call('GetContentState',{})['presentation_ready']
    elif mode=='other-first':assert state['presentation_ready'] and not state['startup_error']
    elif mode=='other-only':assert not state['presentation_ready'] and not state['startup_error'] and not state['state']['applied']
    else:
     assert len(json.dumps(expected).encode())>65536;seen=set();pages=0
     while True:
      pages+=1;assert len(state['state']['works'])<=16
      for w in state['state']['works']:assert w not in seen;seen.add(w)
      cursor=state['page']['next']
      if cursor is None:break
      state=client.call('GetContentState',{'after':cursor,'snapshot':state['page']['snapshot']})
     assert len(seen)==500 and pages==32
     if not args.baseline:
      # The current Runtime intentionally owns one Manager connection. Release
      # the probe client before handing that slot to the actual Manager.
      client.close();client=None
      manager_events=run/'manager-events.jsonl';manager=None
      try:
       manager=subprocess.Popen([str(OUT/'apps/manager/ContentManager.exe'),'--content-data-root',str(run),'--content-events',str(manager_events),'--content-screenshot',str(run/'manager-library.png'),'-screen-fullscreen','0','-logFile',str(run/'manager.log')],startupinfo=subprocess.STARTUPINFO())
       deadline=time.monotonic()+40
       while time.monotonic()<deadline:
        if manager.poll() is not None:raise RuntimeError('Manager exited')
        events=[json.loads(line) for line in manager_events.read_text(encoding='utf-8-sig').splitlines()] if manager_events.exists() else []
        if any(e['event']=='library-rendered' and e['data']['works']==500 for e in events) and (run/'manager-library.png').exists():break
        time.sleep(.2)
       else:raise TimeoutError('Actual Manager did not render all 500 paged works')
       (OUT/'reports/review-manager-library.json').write_text(json.dumps({'passed':True,'works':500,'events':events,'scope':'Actual C# client pagination and Render; no native drag/button input'},ensure_ascii=False,indent=2)+'\n',encoding='utf-8');shutil.copy2(run/'manager-library.png',OUT/'reports/review-manager-library.png')
      finally:
       if manager and manager.poll() is None:manager.terminate();manager.wait(timeout=15)
    cases.append({'name':mode,'passed':True});print('PASS',mode,flush=True)
   except Exception as e:cases.append({'name':mode,'passed':False,'error':type(e).__name__+': '+str(e)[:250]});print('FAIL',mode,type(e).__name__,str(e)[:250],flush=True)
   finally:stop()
  assert closure(OUT/'apps/player')==install
  result={'passed':all(c['passed'] for c in cases),'cases':cases,'player_closure_sha256':install,'scope':'Actual standalone IPC; generated private library damage; no UI interaction claim'};report.write_text(json.dumps(result,indent=2)+'\n');raise SystemExit(0 if result['passed'] else 1)
 finally:stop()
if __name__=='__main__':main()
