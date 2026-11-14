"""One actual authenticated Player share transaction; no input automation or RC."""
from pathlib import Path
import argparse,ctypes,hashlib,json,shutil,subprocess,sys,time,uuid
from check_contracts import ROOT
from toolchain_process import run_toolchain
sys.path.insert(0,str(ROOT/'tools/content'))
from run_e2e import Client

def child(player,out,fixtures):
 data=out/'data';data.mkdir();descriptor=data/'runtime/connection.json';process=None;client=None
 result={'status':'failed','scope':__doc__}
 def until(read,reason):
  end=time.monotonic()+30
  while time.monotonic()<end:
   assert process.poll() is None,'Player exited early'
   try:
    value=read()
    if value:return value
   except (FileNotFoundError,PermissionError):pass
   time.sleep(.1)
  raise TimeoutError(reason)
 def stage(path):
  identifier=str(uuid.uuid4());target=data/'staging'/(identifier+'.spatial');target.parent.mkdir(exist_ok=True);shutil.copyfile(path,target);return identifier
 try:
  startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=4
  process=subprocess.Popen([str(player),'--content-data-root',str(data),'-screen-fullscreen','0','-screen-width','960','-screen-height','540','-logFile',str(out/'player.log')],startupinfo=startup)
  observed=until(lambda:json.loads(descriptor.read_text(encoding='utf-8-sig')),'runtime discovery');assert observed['pid']==process.pid
  client=Client(observed);until(lambda:client.call('GetContentState',{})['runtime_ready'],'runtime ready')
  allowed=client.call('ImportContent',{'staging_id':stage(fixtures/'allowed.spatial')});denied=client.call('ImportContent',{'staging_id':stage(fixtures/'denied.spatial')})
  before=client.call('GetContentState',{})['state']
  exported=client.call('ExportContent',{'work_id':allowed['work_id'],'revision_id':allowed['revision_id']})
  path=data/'exports'/(str(uuid.UUID(exported['export_id']))+'.spatial');installed=data/'library/builds'/(allowed['build_id']+'.spatial')
  digest=lambda p:hashlib.sha256(p.read_bytes()).hexdigest()
  assert digest(path)==digest(installed),'actual export bytes mismatch'
  def refuse(payload):
   try:client.call('ExportContent',payload)
   except AssertionError as e:
    assert e.args and isinstance(e.args[0],dict) and e.args[0].get('status')=='failed',str(e)
   else:raise AssertionError('invalid export accepted')
  refuse({'work_id':denied['work_id'],'revision_id':denied['revision_id']})
  refuse({'work_id':allowed['work_id'],'revision_id':denied['revision_id']})
  refuse({'work_id':allowed['work_id'],'revision_id':allowed['revision_id'],'output':'../outside'})
  assert client.call('GetContentState',{})['state']==before,'share mutated library'
  reimport=client.call('ImportContent',{'staging_id':stage(path)});assert reimport['duplicate'] and reimport['build_id']==allowed['build_id']
  assert len(list((data/'exports').iterdir()))==1,'rejected export left files'
  result.update(status='passed-actual-player-share-not-RC',exact_installed_bytes=True,share_permissions_refused=True,cross_work_and_extra_path_refused=True,reimport_same_build=True,library_unchanged=True)
 finally:
  if client:client.close()
  if process and process.poll() is None:
   # Close only windows belonging to this exact test child; no foreground,
   # click, keyboard input or user-owned application is involved.
   from ctypes import wintypes
   callback=ctypes.WINFUNCTYPE(wintypes.BOOL,wintypes.HWND,wintypes.LPARAM)
   user=ctypes.windll.user32;user.GetWindowThreadProcessId.argtypes=[wintypes.HWND,ctypes.POINTER(wintypes.DWORD)];user.PostMessageW.argtypes=[wintypes.HWND,wintypes.UINT,wintypes.WPARAM,wintypes.LPARAM]
   @callback
   def close(window,unused):
    pid=wintypes.DWORD();user.GetWindowThreadProcessId(window,ctypes.byref(pid))
    if pid.value==process.pid:user.PostMessageW(window,0x10,0,0)
    return True
   user.EnumWindows(close,0)
   try:result['player_exit']=process.wait(timeout=30)
   except subprocess.TimeoutExpired:
    result['forced_cleanup']=True;process.kill();process.wait(timeout=10);result['status']='failed-cleanup';raise
  elif process:result['player_exit']=process.returncode
  (out/'result.json').write_text(json.dumps(result,indent=2))
 assert result['player_exit']==0,result

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--player',type=Path,required=True);p.add_argument('--fixtures',type=Path,required=True);p.add_argument('--child-output',type=Path);a=p.parse_args()
 if a.child_output:child(a.player.resolve(),a.child_output.resolve(),a.fixtures.resolve());return
 out=ROOT/'Validation/Content/sdk-runtime/G/content-share-client'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
 report={'status':'failed','scope':__doc__,'process':{},'player':str(a.player.resolve()),'player_sha256':hashlib.sha256(a.player.read_bytes()).hexdigest()}
 try:
  with (out/'toolchain.log').open('w') as log:run_toolchain([sys.executable,Path(__file__), '--player',a.player.resolve(),'--fixtures',a.fixtures.resolve(),'--child-output',out],ROOT,log,120,report['process'])
  report['result']=json.loads((out/'result.json').read_text());report['status']=report['result']['status']
 finally:(out/'report.json').write_text(json.dumps(report,indent=2))
if __name__=='__main__':main()
