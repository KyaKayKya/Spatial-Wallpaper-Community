"""Owned input message thread lifecycle controls; not desktop integration acceptance."""
from pathlib import Path
import sys,subprocess,json,uuid,argparse
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references,FRAMEWORK
from provenance import sources
parser=argparse.ArgumentParser();group=parser.add_mutually_exclusive_group();group.add_argument('--fence',action='store_true');group.add_argument('--heartbeat',action='store_true');args=parser.parse_args()
out=root/'Validation/Content/sdk-runtime/E/input-thread'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
receipt=sources();report={'sources':receipt,'status':'not-complete','exitCodes':{}}
files=[root/('Supervisor/Tests/InputCommitFenceChecks.cs' if args.fence else 'Supervisor/Tests/InputThreadChecks.cs')];implementation=root/('Supervisor/Interactive/InputCommitFence.cs' if args.fence else 'Supervisor/Interactive/OwnedInputThread.cs')
if args.heartbeat:files=[root/'Supervisor/Tests/InputHeartbeatChecks.cs'];implementation=root/'Supervisor/Interactive/InputHeartbeat.cs'
if implementation.exists():files.append(implementation)
commands=[('compile',[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(FRAMEWORK/'Microsoft.CSharp.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]),('checks',[str(out/'Checks.exe')])]
try:
 for name,command in commands:
  result=subprocess.run(command,capture_output=True,text=True,timeout=15);log=result.stdout+result.stderr;print(log);(out/(name+'.log')).write_text(log,encoding='utf-8');report['exitCodes'][name]=result.returncode;result.check_returncode()
 assert sources()==receipt;report['status']='partial-owned-input-thread-lifecycle'
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
