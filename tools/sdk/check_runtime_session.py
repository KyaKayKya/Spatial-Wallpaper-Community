"""Shared session ownership checks; no presentation or client integration claim."""
import subprocess,uuid,json,hashlib,argparse
from provenance import sources
from check_contracts import ROOT,DATA
from windows_framework import references
out=ROOT/'Validation/Content/sdk-runtime/F/session-owner'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
parser=argparse.ArgumentParser();parser.add_argument('--negative',choices=['skip-terminal-wait','release-on-cleanup-failure']);args=parser.parse_args()
report={'status':'failed','negative':args.negative,'sources':sources(),'scope':'shared lifecycle with controlled delayed resources only'}
files=[ROOT/'tools/sdk/RuntimeSessionChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeSession.cs']
try:
 if args.negative:
  source=files[-1];text=source.read_text()
  if args.negative=='skip-terminal-wait':
   old='try{await lease.Completion.ConfigureAwait(false);}catch(Exception error){if(failure==null)failure=error;}'
   new='';expected='cleanup-return-without-resource-completion-is-not-released'
  else:
   old='}catch(Exception error){cleanup=error;}'
   new='}catch(Exception error){cleanup=error;lock(gate)released=true;}'
   expected='cleanup-failure-retains-resource-ownership'
  assert text.count(old)==1;copy=out/'RuntimeSession.cs';copy.write_text(text.replace(old,new));files[-1]=copy
  report['mutated_source_sha256']=hashlib.sha256(copy.read_bytes()).hexdigest()
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)]
 for name,command in [('compile',command),('result',[str(out/'Checks.exe')])]:
  result=subprocess.run(command,capture_output=True,text=True,timeout=20);log=result.stdout+result.stderr;(out/(name+'.log')).write_text(log,encoding='utf-8');print(log);report[name+'_exit']=result.returncode
  if args.negative and name=='result':assert result.returncode==1 and 'System.Exception: '+expected+'\n' in log.replace('\r\n','\n'),'wrong-negative-failure'
  else:result.check_returncode()
 assert sources()==report['sources'];report.update(status='expected-session-mutation-rejected' if args.negative else 'passed-controlled-session-ownership-only',binary_sha256=hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest())
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
