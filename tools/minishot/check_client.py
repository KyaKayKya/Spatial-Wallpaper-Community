"""Actual installed MiniShot import, native clicks, focus isolation and restart."""
from pathlib import Path
import argparse,ctypes,hashlib,json,os,shutil,struct,subprocess,sys,time,uuid
from ctypes import wintypes as w
ROOT=Path(__file__).resolve().parents[2]
sys.path[:0]=[str(ROOT/'tools/sdk'),str(ROOT/'tools/content'),str(ROOT/'tools/sdk/experiments')]
from run_e2e import Client
from toolchain_process import run_toolchain

u=ctypes.WinDLL('user32',use_last_error=True);k=ctypes.WinDLL('kernel32',use_last_error=True)
u.GetForegroundWindow.restype=w.HWND;u.GetAncestor.argtypes=[w.HWND,w.UINT];u.GetAncestor.restype=w.HWND
u.GetWindowThreadProcessId.argtypes=[w.HWND,ctypes.POINTER(w.DWORD)];u.GetWindowThreadProcessId.restype=w.DWORD
u.GetWindowRect.argtypes=[w.HWND,ctypes.POINTER(w.RECT)];u.GetClientRect.argtypes=[w.HWND,ctypes.POINTER(w.RECT)];u.ClientToScreen.argtypes=[w.HWND,ctypes.POINTER(w.POINT)]
u.SetForegroundWindow.argtypes=[w.HWND];u.ShowWindow.argtypes=[w.HWND,ctypes.c_int];u.PostMessageW.argtypes=[w.HWND,w.UINT,w.WPARAM,w.LPARAM]
u.WindowFromPoint.argtypes=[w.POINT];u.WindowFromPoint.restype=w.HWND;u.SetWindowPos.argtypes=[w.HWND,w.HWND,ctypes.c_int,ctypes.c_int,ctypes.c_int,ctypes.c_int,w.UINT]
class Mouse(ctypes.Structure):_fields_=[('x',w.LONG),('y',w.LONG),('data',w.DWORD),('flags',w.DWORD),('time',w.DWORD),('extra',ctypes.c_size_t)]
class Key(ctypes.Structure):_fields_=[('key',w.WORD),('scan',w.WORD),('flags',w.DWORD),('time',w.DWORD),('extra',ctypes.c_size_t)]
class Payload(ctypes.Union):_fields_=[('mouse',Mouse),('key',Key)]
class Input(ctypes.Structure):_fields_=[('type',w.DWORD),('payload',Payload)]
u.SendInput.argtypes=[w.UINT,ctypes.POINTER(Input),ctypes.c_int];u.SendInput.restype=w.UINT
u.GetWindowTextW.argtypes=[w.HWND,w.LPWSTR,ctypes.c_int];u.GetClassNameW.argtypes=[w.HWND,w.LPWSTR,ctypes.c_int]
class Placement(ctypes.Structure):_fields_=[('length',w.UINT),('flags',w.UINT),('show',w.UINT),('minimum',w.POINT),('maximum',w.POINT),('normal',w.RECT)]
u.GetWindowPlacement.argtypes=[w.HWND,ctypes.POINTER(Placement)];u.SetWindowPlacement.argtypes=[w.HWND,ctypes.POINTER(Placement)]
def text(hwnd,kind=False):
 value=ctypes.create_unicode_buffer(256);(u.GetClassNameW if kind else u.GetWindowTextW)(hwnd,value,256);return value.value
def desktop_click(x,y,grant=None):
 u.SetCursorPos(int(x),int(y));time.sleep(.12);hit=u.WindowFromPoint(w.POINT(int(x),int(y)))
 assert text(hit,True) in ('SysListView32','WorkerW','Progman'),'desktop click overlaps another application: '+text(hit,True)
 send_mouse(2)
 try:
  if grant is not None:assert u.AllowSetForegroundWindow(w.DWORD(grant)),'test foreground grant denied'
  time.sleep(.08)
 finally:send_mouse(4)
k.CreateFileW.argtypes=[w.LPCWSTR,w.DWORD,w.DWORD,ctypes.c_void_p,w.DWORD,w.DWORD,w.HANDLE];k.CreateFileW.restype=w.HANDLE
k.GetFileSizeEx.argtypes=[w.HANDLE,ctypes.POINTER(ctypes.c_longlong)];k.ReadFile.argtypes=[w.HANDLE,ctypes.c_void_p,w.DWORD,ctypes.POINTER(w.DWORD),ctypes.c_void_p];k.CloseHandle.argtypes=[w.HANDLE]

def shared_read(path):
 handle=k.CreateFileW(str(path),0x80000000,7,None,3,0x80,None)
 if handle==w.HANDLE(-1).value:
  error=ctypes.get_last_error()
  if error in (2,3):return None
  raise ctypes.WinError(error)
 try:
  size=ctypes.c_longlong();assert k.GetFileSizeEx(handle,ctypes.byref(size));assert 52<=size.value<=17*1024*1024
  buffer=ctypes.create_string_buffer(size.value);read=w.DWORD();assert k.ReadFile(handle,buffer,size.value,ctypes.byref(read),None) and read.value==size.value;return buffer.raw
 finally:k.CloseHandle(handle)

def store_snapshot(data):
 files=[p for p in data.rglob('state.bin') if p.parent.name.startswith('live-')]
 assert len(files)<=1,'unexpected extra live installation'
 if not files:return {'version':0,'values':{},'path':None},None
 raw=shared_read(files[0])
 if raw is None:return {'version':0,'values':{},'path':None},None
 assert hashlib.sha256(raw[:-32]).digest()==raw[-32:],'store checksum';magic,version,count=struct.unpack_from('<Qqi',raw);assert magic==0x31564b5350575353 and version>=1 and 0<=count<=4096
 at=20;values={}
 for _ in range(count):
  nk,nv=struct.unpack_from('<ii',raw,at);at+=8;assert 0<nk<=256 and 0<=nv<=16*1024*1024
  name=raw[at:at+nk].decode();at+=nk;values[name]=raw[at:at+nv].decode();at+=nv
 assert at==len(raw)-32
 return {'version':version,'values':values,'path':str(files[0])},raw

def windows(pid,children=False):
 found=[];callback=ctypes.WINFUNCTYPE(w.BOOL,w.HWND,w.LPARAM)
 @callback
 def visit(hwnd,_):
  owner=w.DWORD();u.GetWindowThreadProcessId(hwnd,ctypes.byref(owner))
  if (pid is None or owner.value==pid) and (pid is not None or u.IsWindowVisible(hwnd)):found.append(hwnd)
  return True
 if children:
  @callback
  def root(hwnd,_):
   visit(hwnd,0);u.EnumChildWindows(hwnd,visit,0);return True
  u.EnumWindows(root,0)
 else:u.EnumWindows(visit,0)
 return found

def rect(hwnd):
 bounds=w.RECT();assert u.GetClientRect(hwnd,ctypes.byref(bounds));point=w.POINT();assert u.ClientToScreen(hwnd,ctypes.byref(point));return point.x,point.y,bounds.right,bounds.bottom

def send_mouse(flags):
 value=Input(type=0,payload=Payload(mouse=Mouse(flags=flags)));assert u.SendInput(1,ctypes.byref(value),ctypes.sizeof(value))==1,'mouse injection denied'

def click(hwnd,x,y):
 u.SetCursorPos(int(x),int(y));time.sleep(.08);assert u.GetAncestor(u.WindowFromPoint(w.POINT(int(x),int(y))),2)==hwnd,'click is not on the owned window'
 send_mouse(2)
 try:time.sleep(.08)
 finally:send_mouse(4)

def activate(hwnd):
 u.ShowWindow(hwnd,9);u.SetWindowPos(hwnd,None,0,0,0,0,0x13);u.SetForegroundWindow(hwnd);time.sleep(.2)
 if u.GetAncestor(u.GetForegroundWindow(),2)!=hwnd:
  bounds=w.RECT();assert u.GetWindowRect(hwnd,ctypes.byref(bounds));click(hwnd,(bounds.left+bounds.right)//2,bounds.top+12);time.sleep(.2)
 assert u.GetAncestor(u.GetForegroundWindow(),2)==hwnd,'owned foreground not acquired'

def space(hwnd):
 assert u.GetAncestor(u.GetForegroundWindow(),2)==hwnd,'space would reach another application'
 down=Input(type=1,payload=Payload(key=Key(key=32)));up=Input(type=1,payload=Payload(key=Key(key=32,flags=2)))
 assert u.SendInput(1,ctypes.byref(down),ctypes.sizeof(down))==1
 try:time.sleep(.08)
 finally:assert u.SendInput(1,ctypes.byref(up),ctypes.sizeof(up))==1

def child(args):
 from capture import Shot
 try:u.SetProcessDpiAwarenessContext(ctypes.c_void_p(-4))
 except AttributeError:pass
 out=args.child_output.resolve();data=out/'data';data.mkdir(exist_ok=True);descriptor=data/'runtime/connection.json';player=args.delivery.resolve()/'client/player/ContentPlayer.exe';attempts=ROOT/'Validation/Sdk'/('m'+out.name[-10:])/'a';attempts.mkdir(parents=True,exist_ok=True);process=None;client=None;guard=None;hwnd=None
 previous=u.GetForegroundWindow();cursor=w.POINT();u.GetCursorPos(ctypes.byref(cursor));report={'status':'failed','scope':__doc__,'attempts':str(attempts),'mode':'desktop','cases':[]};placements=[]
 for window in windows(None):
  bounds=w.RECT();u.GetWindowRect(window,ctypes.byref(bounds));place=Placement(length=ctypes.sizeof(Placement))
  if text(window,True) not in ('Progman','WorkerW','Shell_TrayWnd','Shell_SecondaryTrayWnd') and bounds.right-bounds.left>200 and bounds.bottom-bounds.top>100 and u.GetWindowPlacement(window,ctypes.byref(place)) and place.show!=2:
   placements.append((window,place));u.ShowWindow(window,6)
 def until(read,reason,seconds=30):
  end=time.monotonic()+seconds
  while time.monotonic()<end:
   assert process is None or process.poll() is None,'Player exited early'
   value=read()
   if value:return value
   time.sleep(.1)
  raise TimeoutError(reason)
 def launch(label):
  nonlocal process,client,hwnd
  process=subprocess.Popen([str(player),'--content-data-root',str(data),'--sdk-attempts',str(attempts),'--content-desktop','true','-screen-fullscreen','0','-screen-width','960','-screen-height','540','-logFile',str(out/(label+'.log'))])
  def discovered():
   try:d=json.loads(descriptor.read_text(encoding='utf-8-sig'))
   except (FileNotFoundError,PermissionError):return None
   return d if d['pid']==process.pid else None
  client=Client(until(discovered,'runtime discovery'))
  def owned():
   state=client.call('GetContentState',{});desktop=state.get('desktop');return desktop.get('window') if desktop else None
  hwnd=until(owned,'owned desktop Player window');report[label+'-window']={'handle':hwnd,'rect':rect(hwnd),'iconic':bool(u.IsIconic(hwnd))}
  try:until(lambda:client.call('GetContentState',{})['runtime_ready'],'runtime initialization')
  finally:snapshot(label+'-startup')
 def close():
  nonlocal client,process
  if client:client.close();client=None
  if process and process.poll() is None:
   u.PostMessageW(hwnd,0x10,0,0)
   code=process.wait(timeout=45);assert code==0,'Player did not exit normally: '+str(code)
  process=None
 def snapshot(name):
  state=client.call('GetContentState',{});(out/(name+'.json')).write_text(json.dumps(state,indent=2));return state
 def ready(label):
  until(lambda:client.call('GetContentState',{})['presentation_ready'],'actual presentation')
  def green():
   state=client.call('GetContentState',{});m=state['presentation_metrics'];x,y,width,height=rect(hwnd);aw=m['width']/(m['height']/.298125);image=Shot(x,y,width,height);pixel=image.getpixel((int(width*(1-.008/aw)),int(height*.008/.298125)))
   return (state,image) if pixel[1]>pixel[0]+20 and (pixel[1]>pixel[2]+10 or pixel[2]>pixel[1]+20) else None
  state,image=until(green,'author storage-ready indicator',15);image.save(out/(label+'.png'));return state
 def aim(state,index=0):
  m=state['presentation_metrics'];eye=m['eye'];scale=m['height']/.298125;center=args.targets[index];v=[n*scale for n in center];distance=-eye[2]
  px=(distance*v[0]+v[2]*eye[0])/(distance+v[2])/m['width']+.5;py=.5-(distance*v[1]+v[2]*eye[1])/(distance+v[2])/m['height'];assert 0<px<1 and 0<py<1
  x,y,width,height=rect(hwnd);return x+px*width,y+py*height
 try:
  launch('first-player');name=str(uuid.uuid4());staging=data/'staging'/(name+'.spatial');staging.parent.mkdir(exist_ok=True);shutil.copyfile(args.package,staging)
  imported=client.call('ImportContent',{'staging_id':name});state=snapshot('imported');assert not state['state']['applied'];display=state['display_id'];applied=client.call('ApplyContent',{'display_id':display,'work_id':imported['work_id']});assert applied['succeeded'],applied
  state=ready('before-hit');assert not store_snapshot(data)[0]['values'];snapshot('before-input');x,y,width,height=rect(hwnd)
  live=[json.loads(p.read_text(encoding='utf-8-sig')) for p in data.glob('sdk-runtime/launches/*/status.json') if json.loads(p.read_text(encoding='utf-8-sig'))['state']=='Running'];assert len(live)==1;viewer=live[0]['viewer_pid']
  desktop_click(*aim(state),viewer)
  try:until(lambda:text(u.GetForegroundWindow())=='Spatial Wallpaper input','desktop click did not focus the input receiver',5)
  finally:report['foreground_after_acquisition']={'title':text(u.GetForegroundWindow()),'class':text(u.GetForegroundWindow(),True)}
  def author_focused():
   x,y,width,height=rect(hwnd);aw=state['presentation_metrics']['width']/(state['presentation_metrics']['height']/.298125);image=Shot(x,y,width,height);pixel=image.getpixel((int(width*(1-.008/aw)),int(height*.008/.298125)));report['author_focus_pixel']=pixel;image.save(out/'focused.png');return pixel[2]>pixel[1]+20 and pixel[1]>pixel[0]+20
  until(author_focused,'desktop receiver focused but author did not receive focus',5);snapshot('focused');desktop_click(*aim(state));snapshot('after-click');report['aim']=aim(state)
  saved=until(lambda:store_snapshot(data)[0] if store_snapshot(data)[0]['values'].get('minishot.v1.target-01')=='hit' else None,'actual native hit not persisted',12);report['cases'].append({'name':'actual-native-click-and-storage','passed':True,'store':saved});(out/'after-hit.bin').write_bytes(store_snapshot(data)[1]);ready('after-hit');report['imported']=imported
  guard=subprocess.Popen([str(args.guard)],stdout=subprocess.PIPE,text=True);guard_info=json.loads(guard.stdout.readline());activate(guard_info['window']);time.sleep(.4);space(guard_info['window']);time.sleep(.7);assert store_snapshot(data)[0]['version']==saved['version'],'unfocused Space reset the wallpaper';report['cases'].append({'name':'other-owned-window-isolates-keyboard','passed':True});u.PostMessageW(guard_info['window'],0x10,0,0);assert guard.wait(timeout=10)==0;guard=None
  close();launch('restarted-player');state=ready('restored-hit');assert store_snapshot(data)[0]['values'].get('minishot.v1.target-01')=='hit';report['cases'].append({'name':'normal-restart-restores-applied-work-and-hit','passed':True});
  x,y,width,height=rect(hwnd);live=[json.loads(p.read_text(encoding='utf-8-sig')) for p in data.glob('sdk-runtime/launches/*/status.json') if json.loads(p.read_text(encoding='utf-8-sig'))['state']=='Running'];assert len(live)==1;desktop_click(x+width*.5,y+height*.5,live[0]['viewer_pid']);time.sleep(1)
  foreground=u.GetForegroundWindow();owner=w.DWORD();u.GetWindowThreadProcessId(foreground,ctypes.byref(owner));statuses=[json.loads(p.read_text(encoding='utf-8-sig')) for p in data.glob('sdk-runtime/launches/*/status.json')];assert any(s['viewer_pid']==owner.value for s in statuses) and text(foreground)=='Spatial Wallpaper input','desktop input receiver not focused'
  space(foreground);until(lambda:store_snapshot(data)[0] if not store_snapshot(data)[0]['values'] else None,'native reset not persisted',15);ready('after-reset');assert store_snapshot(data)[0]['version']>=saved['version']+10;report['cases'].append({'name':'actual-native-Space-clears-all-targets','passed':True});snapshot('final-state');report['status']='passed-installed-native-MiniShot-client-not-RC'
 finally:
  if guard and guard.poll() is None:
   for window in windows(guard.pid):u.PostMessageW(window,0x10,0,0)
   guard.wait(timeout=10)
  try:close()
  finally:
   u.SetCursorPos(cursor.x,cursor.y)
   for window,place in placements:u.SetWindowPlacement(window,ctypes.byref(place))
   if previous:u.SetForegroundWindow(previous)
   (out/'result.json').write_text(json.dumps(report,indent=2))

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--delivery',type=Path,required=True);p.add_argument('--package',type=Path,required=True);p.add_argument('--child-output',type=Path);p.add_argument('--guard',type=Path);a=p.parse_args()
 manifest=json.loads((ROOT.parent.parent/'Prototype/Assets/MiniShotGame/Generated/manifest.json').read_text());a.targets=[[v*manifest['scale'] for v in t['center']] for t in sorted(manifest['targets'],key=lambda t:t['name'])]
 if a.child_output:child(a);return
 from verify_delivery import verify_candidate
 from native_build import compile_probe
 verify_candidate(a.delivery);out=ROOT/'Validation/Content/sdk-runtime/G/minishot'/('client-'+uuid.uuid4().hex);out.mkdir();print(out,flush=True);compile_probe(ROOT/'tools/minishot/FocusGuard.cpp',out/'FocusGuard.exe');report={'status':'failed','process':{},'package_sha256':hashlib.sha256(a.package.read_bytes()).hexdigest()}
 try:
  with (out/'toolchain.log').open('w') as log:run_toolchain([sys.executable,Path(__file__),'--delivery',a.delivery.resolve(),'--package',a.package.resolve(),'--guard',out/'FocusGuard.exe','--child-output',out],ROOT,log,240,report['process'])
  report['result']=json.loads((out/'result.json').read_text());assert report['result']['status']=='passed-installed-native-MiniShot-client-not-RC';report['status']=report['result']['status']
 finally:(out/'report.json').write_text(json.dumps(report,indent=2))
if __name__=='__main__':main()
