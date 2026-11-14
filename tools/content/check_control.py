"""Negative Manager-control frames against the actual standalone Runtime."""
from pathlib import Path
import json,socket,struct,subprocess,time,uuid
from build_apps import OUT,validate_receipt
from run_e2e import Client
from acl_probe import make_old_descriptor_readable,acl_is_private
def main():
 validate_receipt();run=OUT/'runs'/str(uuid.uuid4());run.mkdir(parents=True);process=None;cases=[];report_path=OUT/'reports/control.json'
 if report_path.exists():report_path.unlink()
 def check(name,action):
  try:action();cases.append({'name':name,'passed':True})
  except Exception as e:cases.append({'name':name,'passed':False,'error':type(e).__name__+': '+str(e)})
 try:
  startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=1
  make_old_descriptor_readable(run/'runtime/connection.json')
  process=subprocess.Popen([str(OUT/'apps/player/ContentPlayer.exe'),'--content-data-root',str(run),'-logFile',str(run/'runtime.log')],startupinfo=startup)
  deadline=time.monotonic()+20;path=run/'runtime/connection.json'
  data={}
  while time.monotonic()<deadline:
   if process.poll() is not None:raise RuntimeError('Runtime failed startup')
   try:data=json.loads(path.read_text(encoding='utf-8-sig'))
   except PermissionError:time.sleep(.1);continue
   if data.get('pid')==process.pid:break
   time.sleep(.1)
  assert data.get('pid')==process.pid,'Runtime discovery timeout'
  check('restrict-directory-and-stale-explicit-descriptor-acl',lambda: (_ for _ in ()).throw(AssertionError('Connection descriptor ACL is not private')) if not (acl_is_private(path.parent) and acl_is_private(path)) else None)
  def reject_version():
   try:client=Client(data,versions=[2]);client.close()
   except (EOFError,ConnectionError):return
   raise AssertionError('Incompatible advertised protocol version accepted')
  check('reject-incompatible-advertised-protocol',reject_version)
  def reject_frame():
   with socket.create_connection(('127.0.0.1',data['port']),5) as connection:
    connection.settimeout(5);connection.sendall(struct.pack('>I',65537));assert connection.recv(4)==b''
  check('reject-frame-length-before-payload',reject_frame)
  from build_apps import ROOT
  import shutil
  client=Client(data);time.sleep(3);initial=client.call('GetContentState',{});staging=str(uuid.uuid4());shutil.copy2(OUT/'static-validation/sample.spatial',run/'staging'/(staging+'.spatial'));imported=client.call('ImportContent',{'staging_id':staging})
  def reject_display():
   request=str(uuid.uuid4());client.send('command','ApplyContent',request,{'display_id':'unregistered-display','work_id':imported['work_id']});assert client.read()['payload']['status']=='accepted';result=client.read();assert result['payload']['status']=='failed','Unregistered display accepted as real target'
  check('reject-unregistered-display-target',reject_display)
  def stable_display():
   result=client.call('ApplyContent',{'display_id':initial['display_id'],'work_id':imported['work_id']});assert result['succeeded'];after=client.call('GetContentState',{});assert initial['display_id']==after['display_id'],'Display identity changed after first apply'
  check('display-identity-stable-before-and-after-first-apply',stable_display);client.close()
  report={'passed':all(c['passed'] for c in cases),'cases':cases,'scope':'Actual Runtime TCP boundary; no camera or UI drag'};report_path.write_text(json.dumps(report,indent=2)+'\n');print(json.dumps(report,indent=2));raise SystemExit(0 if report['passed'] else 1)
 except Exception as e:
  report_path.write_text(json.dumps({'passed':False,'cases':cases,'error_type':type(e).__name__,'error':str(e)[:500]},indent=2)+'\n');raise
 finally:
  if process and process.poll() is None:process.terminate();process.wait(timeout=15)
if __name__=='__main__':main()
