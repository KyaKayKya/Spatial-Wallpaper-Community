"""Inherited, bounded Worker frame IPC. Only its write handle enters the child."""
from pathlib import Path
import ctypes as c
from ctypes import wintypes as w
import threading,struct,uuid,json,hashlib,shutil,subprocess,csv,sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from evidence import load_current_build
from windows_sandbox import launch,k,a,P,SA,bind,check
ROOT=Path(__file__).resolve().parents[3]
bind(k,'CreatePipe',[c.POINTER(P),c.POINTER(P),c.POINTER(SA),w.DWORD])
bind(k,'ReadFile',[P,P,w.DWORD,c.POINTER(w.DWORD),P])
bind(k,'CancelIoEx',[P,P])
bind(k,'SetHandleInformation',[P,w.DWORD,w.DWORD])
def receive_exact(pipe,count):
 result=bytearray()
 while len(result)<count:
  block=c.create_string_buffer(min(65536,count-len(result)));read=w.DWORD()
  check(k.ReadFile(pipe,block,len(block),c.byref(read),None))
  if not read.value:raise EOFError('short IPC frame')
  result+=block.raw[:read.value]
 return bytes(result)
def decode_header(data):
 magic,width,height,count=struct.unpack('<iiii',data)
 if magic!=0x53574631 or width!=640 or height!=360 or count!=640*360*3:raise ValueError('unrecognized-or-oversized-frame')
 return count
def main():
 out=ROOT/'Validation/Content/sdk-runtime/A'/('ipc-'+uuid.uuid4().hex)
 runtime=out/'runtime';shutil.copytree(load_current_build(ROOT)/'player',runtime)
 output=runtime/'output';read=P();write=P();descriptor=P();received={};failure=[];thread=None;expected={}
 caller=next(csv.reader(subprocess.check_output(['whoami','/user','/fo','csv','/nh'],text=True).splitlines()))[1]
 def serve():
  try:
   data=receive_exact(read,decode_header(receive_exact(read,16)))
   received.update(bytes=len(data),sha256=hashlib.sha256(data).hexdigest())
   (out/'frame.rgb').write_bytes(data)
  except BaseException as error:failure.append(repr(error))
 def prepare(sid,output):
  nonlocal thread
  sddl='D:P(A;;GA;;;'+caller+')(A;;GW;;;'+sid+')S:(ML;;NW;;;LW)'
  check(a.ConvertStringSecurityDescriptorToSecurityDescriptorW(sddl,1,c.byref(descriptor),None))
  sa=SA(c.sizeof(SA),descriptor,True)
  check(k.CreatePipe(c.byref(read),c.byref(write),c.byref(sa),4096))
  check(k.SetHandleInformation(read,1,0))
  args.extend(['--stage-a-pipe-handle',str(write.value)])
  thread=threading.Thread(target=serve,daemon=True);thread.start()
  return [write.value]
 def started(pid):
  expected['pid']=pid
  check(k.CloseHandle(write));write.value=None
 args=['-batchmode','-force-d3d11','-screen-width','640','-screen-height','360','-screen-fullscreen','0','-logFile',str(output/'player.log'),'--stage-a-output',str(output)]
 report={'status':'failed'}
 try:
  code,sid=launch(runtime/'StageA.exe',args,runtime,isolated_desktop=True,lpac=False,timeout=30,prepare=prepare,on_started=started)
  thread.join(3)
  if thread.is_alive():raise TimeoutError('frame receiver did not complete')
  assert not failure,failure
  assert code==0 and received['bytes']==640*360*3,(code,received)
  report.update(status='passed-frame-ipc-only',exit_code=code,launch=expected,received=received,render=json.loads((output/'render.json').read_text()))
 finally:
  if read:k.CancelIoEx(read,None);k.CloseHandle(read)
  if write:k.CloseHandle(write)
  if descriptor:k.LocalFree(descriptor)
  report['receiver_errors']=failure
  (out/'ipc-report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
  print(json.dumps(report,indent=2));print(out/'ipc-report.json')
if __name__=='__main__':main()
