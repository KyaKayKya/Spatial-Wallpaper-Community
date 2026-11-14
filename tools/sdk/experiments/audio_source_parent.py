"""Test-owned silent parent. Harness assigns a kill-on-close Job before start."""
import json,os,subprocess,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from timed_output import ready_line
child=None
print(json.dumps({'parent_ready':True}),flush=True)
for command in sys.stdin:
 command=command.strip()
 if command=='start':
  if child is not None:raise RuntimeError('child already active')
  child=subprocess.Popen([sys.argv[1],'--tone-right-long','80'],stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
  ready=json.loads(ready_line(child,5));assert ready['ready']
  print(json.dumps({'child_ready':True,'pid':child.pid}),flush=True)
 elif command=='stop':
  if child is None:raise RuntimeError('no child')
  child.terminate();child.wait(timeout=3);child.stdout.close();child.stderr.close();child=None
  print(json.dumps({'child_stopped':True}),flush=True)
 elif command=='exit':
  # Deliberate orphan lifecycle case. The harness retains the Job and child handle.
  os._exit(0)
 else:raise RuntimeError('unknown command')
