"""Controlled actual continuity author C# replay; no Unity or native input."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib,argparse
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references
parser=argparse.ArgumentParser();parser.add_argument('--negative',choices=['queue-bound','late-ack']);args=parser.parse_args()
out=ROOT/'Validation/Content/sdk-runtime/E/input-continuity-author'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
files=[ROOT/'Supervisor/Tests/InputContinuityAuthorChecks.cs',ROOT/'tools/sdk/experiments/StageEInputContinuityAuthor.cs']
report={'status':'failed','scope':'actual author with mocked SDK frames/storage; no Worker/native proof','inputs':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files}}
try:
 if args.negative:
  original=files[1].read_text()
  needle='if(queued!=null){failure="edge-queue-overflow";return;}' if args.negative=='queue-bound' else 'current=null;}'
  assert original.count(needle)==1
  mutation='' if args.negative=='queue-bound' else 'current=null;queued=null;}'
  copy=out/'MutantAuthor.cs';copy.write_text(original.replace(needle,mutation));files[1]=copy
 for name,cmd in [('compile',[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)]),('result',[str(out/'Checks.exe')])]:
  result=subprocess.run(cmd,capture_output=True,text=True,timeout=30);(out/(name+'.log')).write_text(result.stdout+result.stderr);print(result.stdout+result.stderr);
  if name=='result' and args.negative:
   expected='second-pending-edge-rejected' if args.negative=='queue-bound' else 'late-reply-preserves-next-ack'
   assert result.returncode==1 and 'System.Exception: '+expected in result.stderr,'mutant rejected for wrong reason'
  else:result.check_returncode()
 report['status']='expected-mutant-rejected' if args.negative else 'passed-controlled-author-replay'
finally:(out/'report.json').write_text(json.dumps(report,indent=2))
