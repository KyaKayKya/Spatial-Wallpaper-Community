"""Read back actual desktop pixels; IPC success is not a visible frame."""
from pathlib import Path
import argparse,ctypes as c,hashlib,json,subprocess,sys,time,uuid,shutil,struct
from ctypes import wintypes as w
from PIL import Image
import psutil
from verify_receipt import validate
root=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(root/'tools/content'));from run_e2e import Client
parser=argparse.ArgumentParser();parser.add_argument('--launcher',type=Path,default=root/'Prototype/Builds/Library/Launch.exe');parser.add_argument('--settings-source',type=Path,required=True);args=parser.parse_args()
launcher=args.launcher.resolve();out=root/'Validation/Desktop'/('visibility-'+uuid.uuid4().hex[:8]);out.mkdir(parents=True)
shutil.copytree(args.settings_source,out/'calibration')
report={'passed':False,'launcher':str(launcher),'player_sha256':hashlib.sha256((launcher.parent/'SpatialWallpaper.exe').read_bytes()).hexdigest(),'frames':[],'cases':[]}
if launcher==(root/'Prototype/Builds/Library/Launch.exe').resolve():report['build_fingerprint']=hashlib.sha256(json.dumps(validate(),sort_keys=True).encode()).hexdigest()
u=c.WinDLL('user32');u.GetParent.argtypes=[w.HWND];u.GetParent.restype=w.HWND;u.IsChild.argtypes=[w.HWND,w.HWND];u.IsWindowVisible.argtypes=[w.HWND]
u.GetClassNameW.argtypes=[w.HWND,w.LPWSTR,c.c_int];u.GetWindowRect.argtypes=[w.HWND,c.POINTER(w.RECT)]
u.SetThreadDpiAwarenessContext.argtypes=[c.c_void_p];u.SetThreadDpiAwarenessContext(c.c_void_p(-4))
g=c.WinDLL('gdi32');u.GetDC.argtypes=[w.HWND];u.GetDC.restype=w.HDC;u.ReleaseDC.argtypes=[w.HWND,w.HDC];u.PrintWindow.argtypes=[w.HWND,w.HDC,w.UINT]
g.CreateCompatibleDC.argtypes=[w.HDC];g.CreateCompatibleDC.restype=w.HDC;g.CreateCompatibleBitmap.argtypes=[w.HDC,c.c_int,c.c_int];g.CreateCompatibleBitmap.restype=w.HBITMAP
g.SelectObject.argtypes=[w.HDC,w.HGDIOBJ];g.SelectObject.restype=w.HGDIOBJ;g.DeleteObject.argtypes=[w.HGDIOBJ];g.DeleteDC.argtypes=[w.HDC]
g.GetDIBits.argtypes=[w.HDC,w.HBITMAP,w.UINT,w.UINT,c.c_void_p,c.c_void_p,w.UINT]
def frame(state):
 h=state['window'];width=state['render_width'];height=state['render_height'];dc=u.GetDC(h);assert dc
 mem=g.CreateCompatibleDC(dc);assert mem
 bitmap=g.CreateCompatibleBitmap(dc,width,height);assert bitmap
 old=g.SelectObject(mem,bitmap)
 try:
  assert u.PrintWindow(h,mem,2),'Native compositor readback unavailable'
  assert g.SelectObject(mem,old)==bitmap,'Could not release readback bitmap from its DC'
  buf=c.create_string_buffer(width*height*4);header=c.create_string_buffer(struct.pack('IiiHHIIiiII',40,width,-height,1,32,0,len(buf),0,0,0,0))
  assert g.GetDIBits(mem,bitmap,0,height,buf,header,0)==height,'Incomplete native readback'
  return Image.frombytes('RGB',(width,height),buf.raw,'raw','BGRX')
 finally:g.SelectObject(mem,old);g.DeleteObject(bitmap);g.DeleteDC(mem);u.ReleaseDC(h,dc)
p=subprocess.Popen([str(launcher),'--desktop-runtime','true','--desktop-data-root',str(out/'desktop'),'--data-root',str(out/'calibration'),'-screen-fullscreen','0','-logFile',str(out/'runtime.log')],cwd=launcher.parent)
client=None
try:
 deadline=time.monotonic()+30;descriptor=out/'desktop/runtime/connection.json'
 while not descriptor.exists():assert p.poll() is None and time.monotonic()<deadline;time.sleep(.1)
 info=json.loads(descriptor.read_text(encoding='utf-8-sig'));command=psutil.Process(info['pid']).cmdline();report['parent_argument']=command[command.index('-parentHWND')+1]
 client=Client(info)
 while True:
  state=client.call('GetDesktopState',{})
  if state['ready']:break
  assert time.monotonic()<deadline;time.sleep(.1)
 assert not u.IsWindowVisible(state['window']),'Runtime became visible before application'
 profile=json.loads(next((out/'calibration/presets').glob('*.json')).read_text(encoding='utf-8-sig'))
 request={'display_id':state['displays'][0]['id'],'preset_id':profile['id'],'preset_revision':profile['revision']}
 for artwork in ['mita','limpool','showcase','pool','train','parking','minishotgame','rainycafe']:
  request['artwork_id']=artwork;state=client.call('ApplyDesktop',request);assert state['succeeded'],state
  assert u.IsWindowVisible(state['window']),'Applied native output is hidden'
  parent=u.GetParent(state['window']);cls=c.create_unicode_buffer(256);u.GetClassNameW(parent,cls,256)
  assert parent==state['parent'] and cls.value=='WorkerW','Actual presentation surface is outside the desktop'
  target=next(d for d in state['displays'] if d['id']==request['display_id'])
  for window in [state['window']]:
   rect=w.RECT();assert u.GetWindowRect(window,c.byref(rect))
   assert [rect.left,rect.top,rect.right-rect.left,rect.bottom-rect.top]==[target['x'],target['y'],target['width'],target['height']],'Surface/child physical bounds mismatch'
  assert state['attached'] and not state['camera_started'] and state['render_errors']==0,state
  if artwork=='mita':time.sleep(7)
  image=frame(state);report['frames'].append({'artwork':artwork,'sha256':hashlib.sha256(image.tobytes()).hexdigest()})
  # Legacy failure frames contain the user's original wallpaper: retain hashes only.
  if report['parent_argument']=='delayed':image.save(out/('native-'+artwork+'.png'))
 assert len({row['sha256'] for row in report['frames']})==8,'Eight successful applies still read back the same original Windows wallpaper'
 assert report['parent_argument']=='delayed','Launcher still assigns an external presentation target'
 report['cases'].append('eight-distinct-native-desktop-frames-with-zero-render-errors')
 client.call('BeginDesktopPreview',{});assert not u.IsWindowVisible(state['window']),'Preview left desktop surface visible'
 state=client.call('EndDesktopPreview',{});assert state['attached'] and u.IsWindowVisible(state['window'])
 report['cases'].append('preview-hides-and-restores-real-surface')
 client.call('PauseDesktop',{'paused':True});assert u.IsWindowVisible(state['window']),'Pause discarded last frame'
 client.call('PauseDesktop',{'paused':False});assert u.IsWindowVisible(state['window'])
 client.call('StopDesktop',{});assert not u.IsWindowVisible(state['window']),'Stop left presentation surface visible'
 report['cases'].append('pause-retains-surface-stop-hides-it')
 report['passed']=True
finally:
 if client:client.call('QuitDesktop',{});client.close()
 p.wait(20)
 (out/'result.json').write_text(json.dumps(report,indent=2));print(json.dumps(report,indent=2));print('Evidence',out)
