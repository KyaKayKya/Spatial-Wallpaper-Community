"""Original local HTTP fixture. Test credential is public and never a real service key."""
from http.server import ThreadingHTTPServer,BaseHTTPRequestHandler
import json,threading,time
class LlmFixture:
 def __init__(self):
  self.requests=[];owner=self
  class Handler(BaseHTTPRequestHandler):
   def log_message(self,*args):pass
   def handle(self):
    try:super().handle()
    except (ConnectionResetError,ConnectionAbortedError):pass
   def do_POST(self):
    request=json.loads(self.rfile.read(int(self.headers['Content-Length'])));owner.requests.append({'body':request,'test_auth_matches':self.headers.get('Authorization')=='Bearer fixture-only-key'})
    if request['messages'][0]['content']=='hold':time.sleep(2)
    body=json.dumps({'choices':[{'message':{'content':'reply-as-data'}}]}).encode();self.send_response(200);self.send_header('Content-Type','application/json');self.send_header('Content-Length',str(len(body)));self.end_headers()
    try:self.wfile.write(body)
    except (BrokenPipeError,ConnectionResetError,ConnectionAbortedError):pass
  self.server=ThreadingHTTPServer(('127.0.0.1',0),Handler);self.thread=threading.Thread(target=self.server.serve_forever,daemon=True);self.thread.start()
  self.config={'endpoint':f'http://127.0.0.1:{self.server.server_port}/completion','model':'fixture-model','credential':'fixture-only-key'}
 def close(self):self.server.shutdown();self.server.server_close();self.thread.join(2)
