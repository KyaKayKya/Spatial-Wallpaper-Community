import sys,unittest,os,tempfile
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
class EndpointWait(unittest.TestCase):
 @unittest.skipUnless(sys.platform=='win32','Windows descriptor publication sharing')
 def test_endpoint_reader_does_not_block_atomic_publication(self):
  import endpoint_wait
  import ctypes
  from ctypes import wintypes
  replace=ctypes.WinDLL('kernel32',use_last_error=True).ReplaceFileW
  replace.argtypes=[wintypes.LPCWSTR,wintypes.LPCWSTR,wintypes.LPCWSTR,wintypes.DWORD,ctypes.c_void_p,ctypes.c_void_p]
  def publish(fresh,path):
   if not replace(str(path),str(fresh),None,0,None,None):raise ctypes.WinError(ctypes.get_last_error())
  self.assertTrue(hasattr(endpoint_wait,'open_endpoint'),'missing replacement-compatible endpoint reader')
  with tempfile.TemporaryDirectory() as folder:
   path=Path(folder)/'endpoint.json';fresh=Path(folder)/'fresh.json'
   path.write_text('{"auth":"old"}');fresh.write_text('{"auth":"new"}')
   with path.open('rb'):
    with self.assertRaises(PermissionError):publish(fresh,path)
   with endpoint_wait.open_endpoint(path) as old:
    publish(fresh,path)
    self.assertEqual(old.read(),b'{"auth":"old"}')
   self.assertEqual(endpoint_wait.read_endpoint(path),{'auth':'new'})
 def test_exclusive_replacement_and_stale_endpoint_do_not_finish_startup(self):
  from endpoint_wait import wait_endpoint
  error=PermissionError(13,'sharing violation');error.winerror=32
  crt_error=PermissionError(13,'CRT sharing violation')
  values=iter([error,crt_error,{'auth':'old'},{'auth':'new'}]);clock=[0.];events=[]
  def read():
   value=next(values)
   if isinstance(value,Exception):raise value
   return value
  result=wait_endpoint(read,lambda:True,'old',1,lambda:clock[0],lambda value:clock.__setitem__(0,clock[0]+value),events)
  self.assertEqual(result['auth'],'new');self.assertEqual(events,[32,13])
 def test_persistent_denial_and_exited_backend_never_pass(self):
  from endpoint_wait import wait_endpoint
  clock=[0.]
  def denied():
   error=PermissionError(13,'access denied');error.winerror=5;raise error
  with self.assertRaises(TimeoutError):wait_endpoint(denied,lambda:True,None,.1,lambda:clock[0],lambda value:clock.__setitem__(0,clock[0]+value))
  with self.assertRaises(RuntimeError):wait_endpoint(lambda:{'auth':'new'},lambda:False,None,1)
