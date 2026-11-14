"""Background owner loop controls. No HWND, hooks, input injection or Unity."""
from pathlib import Path
import sys,subprocess,json,uuid,shutil,hashlib,argparse
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references
parser=argparse.ArgumentParser();parser.add_argument('--negative-omit-command-stages',action='store_true');args=parser.parse_args()
out=root/'Validation/Content/sdk-runtime/E/input-owner-trace'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
files=[root/'Supervisor/Interactive/OwnedInputThread.cs',root/'Supervisor/Tests/InputOwnerTraceChecks.cs']
receipt={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in [*files,Path(__file__)]}
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
if args.negative_omit_command_stages:
 text=files[0].read_text(encoding='utf-8-sig')
 for needle in ('Mark(3);','Mark(4);'):
  assert text.count(needle)==1;text=text.replace(needle,'')
 copy=out/'OwnedInputThread.cs';copy.write_text(text,encoding='utf-8');files[0]=copy
report={'sources':receipt,'scope':'background-controlled-owner-only','negative':args.negative_omit_command_stages,'status':'failed'}
try:
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]
 subprocess.run(command,check=True,timeout=30)
 result=subprocess.run([str(out/'Checks.exe'),str(out)],capture_output=True,text=True,timeout=15)
 log=result.stdout+result.stderr;(out/'checks.log').write_text(log,encoding='utf-8');print(log);report['exit_code']=result.returncode
 if args.negative_omit_command_stages:
  assert result.returncode==1 and 'System.Exception: owner-stall-identifies-command' in log and 'PASS actual-owner-thread-exited' in log
  report['status']='expected-missing-command-stages-rejected'
 else:result.check_returncode();report['status']='partial-owner-stage-controls'
 assert all(hashlib.sha256((root/p).read_bytes()).hexdigest()==h for p,h in receipt.items())
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
