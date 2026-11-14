"""Capture our actual Evo Manager empty state; this never claims a drag test."""
from pathlib import Path
import subprocess,time,json,uuid,ctypes,os
from build_apps import ROOT,OUT
def stop_own_runtime(descriptor):
 if not descriptor.exists():return
 data=json.loads(descriptor.read_text(encoding='utf-8-sig'));kernel=ctypes.WinDLL('kernel32',use_last_error=True)
 kernel.OpenProcess.argtypes=[ctypes.c_ulong,ctypes.c_int,ctypes.c_ulong];kernel.OpenProcess.restype=ctypes.c_void_p
 kernel.QueryFullProcessImageNameW.argtypes=[ctypes.c_void_p,ctypes.c_ulong,ctypes.c_wchar_p,ctypes.POINTER(ctypes.c_ulong)];kernel.TerminateProcess.argtypes=[ctypes.c_void_p,ctypes.c_uint];kernel.CloseHandle.argtypes=[ctypes.c_void_p]
 handle=kernel.OpenProcess(0x1001,False,data['pid'])
 if not handle:return
 try:
  name=ctypes.create_unicode_buffer(32768);length=ctypes.c_ulong(len(name));assert kernel.QueryFullProcessImageNameW(handle,0,name,ctypes.byref(length));assert Path(name.value).resolve()==(OUT/'apps/player/ContentPlayer.exe').resolve();kernel.TerminateProcess(handle,0)
 finally:kernel.CloseHandle(handle)
def main():
 from build_apps import validate_receipt
 validate_receipt()
 run=OUT/'runs'/str(uuid.uuid4());run.mkdir(parents=True);capture=run/'manager.png';events=run/'manager-events.jsonl';process=None
 try:
  startup=subprocess.STARTUPINFO();startup.dwFlags|=subprocess.STARTF_USESHOWWINDOW;startup.wShowWindow=1
  process=subprocess.Popen([str(OUT/'apps/manager/ContentManager.exe'),'--content-data-root',str(run),'--content-screenshot',str(capture),'--content-events',str(events),'-screen-fullscreen','0','-screen-width','1120','-screen-height','760','-logFile',str(run/'manager.log')],startupinfo=startup)
  deadline=time.monotonic()+30
  while not capture.exists() and time.monotonic()<deadline:
   assert process.poll() is None,'Manager exited before capture';time.sleep(.1)
  assert capture.exists(),'Manager capture timeout';time.sleep(.2)
  log=(run/'manager.log').read_text(encoding='utf-8-sig');assert 'NullReferenceException' not in log and 'DllNotFoundException' not in log,log[-2000:]
  entries=[json.loads(line) for line in events.read_text(encoding='utf-8').splitlines()];assert any(e['event']=='operation-accepted' for e in entries),'No real Manager handshake/request acknowledgement'
  target=OUT/'reports/manager-empty.png';target.write_bytes(capture.read_bytes());(OUT/'reports/manager-view.json').write_text(json.dumps({'passed':True,'scope':'Actual Evo empty state and Manager/Runtime TCP; no drag or Apply button interaction','events':entries},ensure_ascii=False,indent=2)+'\n');print('Manager view captured',target)
 finally:
  if process and process.poll() is None:process.terminate();process.wait(timeout=15)
  stop_own_runtime(run/'runtime/connection.json')
if __name__=='__main__':main()
