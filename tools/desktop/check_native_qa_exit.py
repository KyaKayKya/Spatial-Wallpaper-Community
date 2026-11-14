"""Execute the actual QA finalizers with cleanup/source faults, without launching UI."""
from pathlib import Path
import ast,json,sys,types,uuid
ROOT=Path(__file__).resolve().parents[2]
class Timeout(Exception):pass
class LivePin:
 pid=123
 def wait(self,timeout):raise Timeout()
def main():
 out=ROOT/'Validation/Content/sdk-runtime/native-render/task3/qa-exit'/uuid.uuid4().hex[:12];out.mkdir(parents=True);checks=[]
 cases=[('check_native_runtime.py','passed-formal-Launch-native-control-development-only',fault) for fault in ['none','live-pin','source-change','cleanup-error','cleanup-unproven']]+[('check_native_blueprint.py','passed-native-blueprint-control-development-only',fault) for fault in ['none','live-pin','cleanup-error','cleanup-unproven','original-change']]+[('check_native_runtime_present.py','captured-native-child-ETW-development-only-uncontrolled-occlusion',fault) for fault in ['none','incomplete']]
 for filename,passed,fault in cases:
  source=ROOT/'tools/desktop'/filename;tree=ast.parse(source.read_text(encoding='utf-8'));main=next(n for n in tree.body if isinstance(n,ast.FunctionDef) and n.name=='main');final=next(n for n in reversed(main.body) if isinstance(n,ast.Try)).finalbody
  directory=out/(filename+'-'+fault);directory.mkdir();report={'status':passed};report.update({'cleanup_error':'injected owned cleanup failure'} if fault=='cleanup-error' else {})
  if fault=='cleanup-unproven':report['cleanup_unproven']=True
  values={'__builtins__':__builtins__,'sys':sys,'json':json,'report':report,'out':directory,'client':None,'process':None,'children':[LivePin()] if fault=='live-pin' else [],'psutil':types.SimpleNamespace(TimeoutExpired=Timeout),'subprocess':types.SimpleNamespace(TimeoutExpired=Timeout),'hashlib':None,'hashes':{'changed':'digest'} if fault=='source-change' else {},'sources':[],'inventory':lambda p:{},'source':directory,'original':{}}
  if fault=='original-change':values['original']={'changed':'digest'}
  values['monitor']=None;report['cases']=[{'complete_timing':fault!='incomplete'}]
  rejected=False
  try:exec(compile(ast.Module(body=final,type_ignores=[]),str(source),'exec'),values)
  except RuntimeError:rejected=True
  saved=json.loads((directory/'report.json').read_text(encoding='utf-8'));checks.append({'tool':filename,'fault':fault,'rejected':rejected,'saved_status':saved['status'],'passed':rejected==(fault!='none') and (saved['status']==passed)==(fault=='none')})
 (out/'report.json').write_text(json.dumps({'checks':checks,'passed':all(c['passed'] for c in checks)},indent=2),encoding='utf-8');print(out)
 assert all(c['passed'] for c in checks),checks
 print('PASS',len(checks),'actual QA finalizer fault cases')
if __name__=='__main__':main()
