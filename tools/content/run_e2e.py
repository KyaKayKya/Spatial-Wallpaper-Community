"""Real standalone Runtime IPC checks; UI drag is separately evidenced, never inferred."""
from pathlib import Path
import argparse,json,os,socket,struct,subprocess,time,uuid,shutil,zipfile,hashlib
ROOT=Path(__file__).resolve().parents[2];OUT=ROOT/'Validation/Content/p3'
class Client:
 def __init__(self,descriptor,token=None,versions=None):
  self.runtime=descriptor['runtime_instance_id'];self.instance=str(uuid.uuid4());self.connection=None;self.tx=0;self.rx=0;self.revision=0;self.socket=socket.create_connection(('127.0.0.1',descriptor['port']),5);self.socket.settimeout(40)
  self.send('handshake','Hello',None,{'role':'manager','token':descriptor['token'] if token is None else token,'protocol_versions':[1] if versions is None else versions});welcome=self.read();assert welcome['type']=='Welcome';self.connection=welcome['payload']['connection_id']
 def send(self,kind,name,request,payload):
  self.tx+=1;packet={'protocol_version':1,'kind':kind,'type':name,'runtime_instance_id':self.runtime,'sender_instance_id':self.instance,'connection_id':self.connection,'seq':str(self.tx),'request_id':request,'sent_at_us':str(time.perf_counter_ns()//1000),'state_revision':None,'payload':payload};raw=json.dumps(packet,separators=(',',':')).encode();self.socket.sendall(struct.pack('>I',len(raw))+raw)
 def exact(self,n):
  data=b''
  while len(data)<n:
   block=self.socket.recv(n-len(data))
   if not block:raise EOFError('Runtime rejected/closed connection')
   data+=block
  return data
 def read(self):
  size=struct.unpack('>I',self.exact(4))[0];assert 0<size<=65536;packet=json.loads(self.exact(size));self.rx+=1;assert packet['runtime_instance_id']==self.runtime and packet['seq']==str(self.rx);revision=int(packet['state_revision']);assert revision>self.revision;self.revision=revision;return packet
 def call(self,name,payload):
  request=str(uuid.uuid4());self.send('command',name,request,payload);accepted=self.read();assert accepted['request_id']==request and accepted['payload']['status']=='accepted';completed=self.read();assert completed['request_id']==request;assert completed['payload']['status']=='completed',completed['payload'];return completed['payload']['result']
 def close(self):self.socket.close()
def main():
 from build_apps import validate_receipt
 validate_receipt()
 parser=argparse.ArgumentParser();parser.add_argument('--suite',default='import-apply');parser.add_argument('--scene',default='StaticWindow',choices=['StaticWindow','Parking','Pool','Train']);args=parser.parse_args();assert args.suite=='import-apply','unsupported E2E suite'
 from fingerprint_apps import closure
 install=closure(OUT/'apps/player');report_path=OUT/'reports'/('import-apply-'+args.scene+'.json')
 if report_path.exists():report_path.unlink()
 run=OUT/'runs'/str(uuid.uuid4());run.mkdir(parents=True);descriptor=run/'runtime/connection.json';cases=[];process=None;client=None
 def launch():
  nonlocal process
  startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=1
  process=subprocess.Popen([str(OUT/'apps/player/ContentPlayer.exe'),'--content-data-root',str(run),'--content-frame-output',str(run/'frame.png'),'-screen-fullscreen','0','-screen-width','960','-screen-height','540','-logFile',str(run/'runtime.log')],startupinfo=startup)
  end=time.monotonic()+20
  while time.monotonic()<end:
   if process.poll() is not None:raise RuntimeError('Runtime exited '+str(process.returncode))
   if descriptor.exists():
    try:data=json.loads(descriptor.read_text(encoding='utf-8-sig'))
    except PermissionError:time.sleep(.1);continue # Atomic replacement/ACL transition; bounded by deadline and live PID.
    if data['pid']==process.pid:return data
   time.sleep(.1)
  raise TimeoutError('Runtime discovery timeout')
 def stage(source):
  name=str(uuid.uuid4());shutil.copy2(source,run/'staging'/(name+'.spatial'));return name
 try:
  data=launch()
  try:bad=Client(data,'incorrect');bad.close();raise AssertionError('Wrong token accepted')
  except (EOFError,ConnectionError):cases.append({'name':'reject-wrong-token','passed':True})
  client=Client(data);initial=client.call('GetContentState',{});display=initial['display_id'];assert initial['runtime_ready'];assert not initial['state']['applied'] and not initial['camera_started'];cases.append({'name':'startup-empty-without-camera','passed':True})
  package=OUT/(args.scene+'.spatial') if args.scene!='StaticWindow' else OUT/'static-validation/sample.spatial';assert package.exists(),'Build the current SDK package before E2E';imported=client.call('ImportContent',{'staging_id':stage(package)});state=client.call('GetContentState',{});assert len(state['state']['works'])==1 and not state['state']['applied'];cases.append({'name':'real-import-not-apply','passed':True})
  duplicate=client.call('ImportContent',{'staging_id':stage(package)});assert duplicate['duplicate'] and duplicate['work_id']==imported['work_id'];cases.append({'name':'real-duplicate-import','passed':True})
  time.sleep(3) # Native window initialization; production has its own bounded readiness.
  result=client.call('ApplyContent',{'display_id':display,'work_id':imported['work_id']});assert result['succeeded'],result;state=client.call('GetContentState',{});assert state['state']['applied'][display]['revision_id']==imported['revision_id'] and state['presentation_ready'] and state['render_errors']==0;cases.append({'name':'real-player-first-frame-application','passed':True})
  deadline=time.monotonic()+5
  while not (run/'frame.png').exists() and time.monotonic()<deadline:time.sleep(.1)
  assert (run/'frame.png').exists();shutil.copy2(run/'frame.png',OUT/'reports'/('first-frame-'+args.scene+'.png'))
  files={}
  with zipfile.ZipFile(package) as archive:
   for name in archive.namelist():files[name]=archive.read(name)
  scene=json.loads(files['scene.json']);external=next(m for m in scene['materials'] if m['bundle']);external['shader']='SdkSample/Missing';files['scene.json']=json.dumps(scene,separators=(',',':')).encode();manifest=json.loads(files['manifest.json']);manifest['files']['scene.json']={'size':len(files['scene.json']),'sha256':hashlib.sha256(files['scene.json']).hexdigest()};manifest.pop('build_id');manifest['build_id']=hashlib.sha256(json.dumps(manifest,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()).hexdigest();files['manifest.json']=json.dumps(manifest,separators=(',',':'),ensure_ascii=False).encode();broken=run/'missing-shader.spatial'
  with zipfile.ZipFile(broken,'w') as archive:
   for name,value in files.items():archive.writestr(name,value)
  failed=client.call('ImportContent',{'staging_id':stage(broken)});result=client.call('ApplyContent',{'display_id':display,'work_id':failed['work_id']});assert not result['succeeded'] and result['restored'],result;state=client.call('GetContentState',{});assert state['state']['applied'][display]['revision_id']==imported['revision_id'];cases.append({'name':'real-load-failure-restores-old-exact-revision','passed':True})
  client.close();client=None;process.terminate();process.wait(timeout=15);data2=launch();assert data2['token']!=data['token'];client=Client(data2);deadline=time.monotonic()+35
  while True:
   state=client.call('GetContentState',{})
   if state['presentation_ready']:break
   if time.monotonic()>deadline:raise TimeoutError('Last applied first frame not restored')
   time.sleep(.1)
  assert state['state']['applied'][display]['revision_id']==imported['revision_id'] and not state['camera_started'] and state['render_errors']==0;cases.append({'name':'restart-last-success-with-new-token-no-camera','passed':True})
  assert closure(OUT/'apps/player')==install,'Player installation changed during run'
  report={'passed':True,'scene':args.scene,'scope':'Runtime real IPC and Unity presentation; no UI drag claim','cases':cases,'runtime_pid':process.pid,'player_closure_sha256':install,'platform_source_receipt_sha256':hashlib.sha256((OUT/'reports/platform-source-receipt.json').read_bytes()).hexdigest(),'package_sha256':hashlib.sha256(package.read_bytes()).hexdigest()};report_path.write_text(json.dumps(report,indent=2)+'\n');print('Real Runtime verified',len(cases),'cases',args.scene)
 except Exception as e:
  report_path.write_text(json.dumps({'passed':False,'scene':args.scene,'scope':'Failed actual Runtime run; no stage acceptance','cases':cases,'error_type':type(e).__name__,'error':str(e)[:500],'player_closure_sha256':install,'run_directory':run.relative_to(ROOT).as_posix(),'runtime_exit_code':process.poll() if process else None},indent=2)+'\n');raise
 finally:
  if client:client.close()
  if process and process.poll() is None:process.terminate();process.wait(timeout=15)
if __name__=='__main__':main()
