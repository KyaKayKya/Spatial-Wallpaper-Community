"""Render the actual manager's sound controls without desktop input."""
from pathlib import Path
import argparse,ctypes,json,os,subprocess,sys,time,uuid
from ctypes import wintypes as w
ROOT=Path(__file__).resolve().parents[2];sys.path[:0]=[str(ROOT/'tools/sdk'),str(ROOT/'tools/content')]
from run_e2e import Client
from toolchain_process import run_toolchain
p=argparse.ArgumentParser(description=__doc__);p.add_argument('--delivery',type=Path,required=True);p.add_argument('--data',type=Path,required=True);p.add_argument('--child-output',type=Path);a=p.parse_args()
if not a.child_output:
 out=ROOT/'Validation/Content/sdk-runtime/G/sound'/('manager-'+uuid.uuid4().hex);out.mkdir(parents=True);receipt={};print(out,flush=True)
 with (out/'toolchain.log').open('w') as log:run_toolchain([sys.executable,str(Path(__file__).resolve()),'--delivery',str(a.delivery.resolve()),'--data',str(a.data.resolve()),'--child-output',str(out)],ROOT,log,180,receipt,env=os.environ.copy())
 (out/'process.json').write_text(json.dumps(receipt,indent=2));print(out);raise SystemExit()
out=a.child_output;player=None;manager=None;client=None
u=ctypes.WinDLL('user32');u.GetWindowThreadProcessId.argtypes=[w.HWND,ctypes.POINTER(w.DWORD)];u.PostMessageW.argtypes=[w.HWND,w.UINT,w.WPARAM,w.LPARAM]
def quit(process):
 if process is None or process.poll() is not None:return
 callback=ctypes.WINFUNCTYPE(w.BOOL,w.HWND,w.LPARAM)
 @callback
 def visit(hwnd,_):
  pid=w.DWORD();u.GetWindowThreadProcessId(hwnd,ctypes.byref(pid))
  if pid.value==process.pid:u.PostMessageW(hwnd,0x10,0,0)
  return True
 u.EnumWindows(visit,0);assert process.wait(timeout=45)==0
try:
 runtime=a.delivery/'runtime'
 if not runtime.is_dir():runtime=a.delivery/'client/player/sdk-runtime'
 player=subprocess.Popen([str(a.delivery/'client/player/ContentPlayer.exe'),'--content-data-root',str(a.data),'--sdk-runtime',str(runtime),'--sdk-attempts',str(ROOT/'Validation/Sdk/aview/a'),'-screen-fullscreen','0','-screen-width','960','-screen-height','540','-logFile',str(out/'player.log')])
 deadline=time.monotonic()+60;descriptor=a.data/'runtime/connection.json'
 while client is None:
  try:
   d=json.loads(descriptor.read_text());client=Client(d) if d['pid']==player.pid else None
  except (FileNotFoundError,PermissionError,ConnectionRefusedError):pass
  assert time.monotonic()<deadline;time.sleep(.1)
 while not client.call('GetContentState',{})['runtime_ready']:assert time.monotonic()<deadline;time.sleep(.1)
 client.close();client=None
 manager=subprocess.Popen([str(a.delivery/'client/manager/ContentManager.exe'),'--content-data-root',str(a.data),'--content-events',str(out/'manager-events.jsonl'),'--content-screenshot',str(out/'manager.png'),'-screen-fullscreen','0','-screen-width','1120','-screen-height','760','-logFile',str(out/'manager.log')])
 while not (out/'manager.png').exists():assert manager.poll() is None and time.monotonic()<deadline;time.sleep(.1)
 events=[json.loads(line) for line in (out/'manager-events.jsonl').read_text(encoding='utf-8-sig').splitlines() if line.strip()]
 rendered=next((event for event in reversed(events) if event.get('event')=='library-rendered' and event.get('data',{}).get('works',0)>0),None)
 assert rendered is not None,'manager did not render any work; screenshot alone is insufficient'
 (out/'rendered.json').write_text(json.dumps(rendered,indent=2))
 time.sleep(.5);quit(manager);manager=None;client=Client(json.loads(descriptor.read_text()));(out/'state.json').write_text(json.dumps(client.call('GetContentState',{}),indent=2));client.close();client=None;quit(player);player=None
 (out/'report.json').write_text(json.dumps({'status':'rendered-actual-manager-sound-controls-not-gesture-acceptance','image':str(out/'manager.png'),'scope':'actual UI rendering only; no mouse or keyboard input'},indent=2))
finally:
 if client:client.close()
 quit(manager);quit(player)
