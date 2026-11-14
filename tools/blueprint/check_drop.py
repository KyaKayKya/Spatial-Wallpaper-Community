"""Deliver a real WM_DROPFILES to this test Player's owned Unity window."""
from pathlib import Path
import ctypes as c,ctypes.wintypes as w,json,subprocess,time
r=Path(__file__).resolve().parents[2];out=r/'Validation/Blueprint/native-drop';out.mkdir(parents=True,exist_ok=True)
for name in ['waiting.json','drop-checks.json']:
 if (out/name).exists():(out/name).unlink()
command=[str(r/'Prototype/Builds/Blueprint/Blueprint.exe'),'-screen-fullscreen','0','-screen-width','1440','-screen-height','900','-logFile',str(out/'player.log'),'--blueprint-data-root',str(out/'data'),'--blueprint-drop-check',str(out)]
process=subprocess.Popen(command,cwd=r/'Prototype');user=c.WinDLL('user32',use_last_error=True);kernel=c.WinDLL('kernel32',use_last_error=True)
user.GetWindowThreadProcessId.argtypes=[w.HWND,c.POINTER(w.DWORD)];user.GetClassNameW.argtypes=[w.HWND,w.LPWSTR,c.c_int];user.PostMessageW.argtypes=[w.HWND,w.UINT,w.WPARAM,w.LPARAM]
kernel.GlobalAlloc.argtypes=[w.UINT,c.c_size_t];kernel.GlobalAlloc.restype=w.HGLOBAL;kernel.GlobalLock.argtypes=[w.HGLOBAL];kernel.GlobalLock.restype=c.c_void_p;kernel.GlobalUnlock.argtypes=[w.HGLOBAL]
try:
 deadline=time.monotonic()+20
 while not (out/'waiting.json').exists():
  if process.poll() is not None or time.monotonic()>deadline:raise RuntimeError('Drop receiver startup failed')
  time.sleep(.1)
 windows=[];callback=c.WINFUNCTYPE(w.BOOL,w.HWND,w.LPARAM)
 @callback
 def visit(hwnd,_):
  pid=w.DWORD();user.GetWindowThreadProcessId(hwnd,c.byref(pid));name=c.create_unicode_buffer(64);user.GetClassNameW(hwnd,name,64)
  if pid.value==process.pid and name.value=='UnityWndClass':windows.append(hwnd)
  return True
 user.EnumWindows.argtypes=[callback,w.LPARAM];user.EnumWindows(visit,0);assert len(windows)==1,windows
 class Drop(c.Structure):_fields_=[('offset',w.DWORD),('x',c.c_long),('y',c.c_long),('nonclient',w.BOOL),('wide',w.BOOL)]
 text=(str(r/'galacticlunairy-cherryblossomcottage.zip')+'\0\0').encode('utf-16-le');header=Drop(c.sizeof(Drop),0,0,False,True);handle=kernel.GlobalAlloc(0x2,c.sizeof(header)+len(text));pointer=kernel.GlobalLock(handle);assert pointer;c.memmove(pointer,c.byref(header),c.sizeof(header));c.memmove(pointer+c.sizeof(header),text,len(text));kernel.GlobalUnlock(handle)
 assert user.PostMessageW(windows[0],0x233,handle,0),'WM_DROPFILES delivery failed'
 assert process.wait(timeout=50)==0
 report=json.loads((out/'drop-checks.json').read_text(encoding='utf-8-sig'));assert report['pass'],report;print(json.dumps(report,ensure_ascii=False))
finally:
 if process.poll() is None:process.terminate();process.wait(timeout=10)
