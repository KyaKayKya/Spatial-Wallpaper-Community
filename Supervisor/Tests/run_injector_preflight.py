"""Controlled preflight evidence; never calls native input/window APIs."""
from pathlib import Path
import sys,subprocess,uuid,json,shutil,argparse
parser=argparse.ArgumentParser();parser.add_argument("--negative-diagnostic-throws",action="store_true");args=parser.parse_args()
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from provenance import sources
from windows_framework import references
out=ROOT/'Validation/Content/sdk-runtime/E/injector-preflight'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
report={'status':'failed','sources':sources(),'scope':'controlled foreground/cursor delegates and real self process identity; no SendInput/windows/Unity'}
try:
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
 files=[ROOT/p for p in ['Supervisor/Tests/InjectorPreflightChecks.cs','Supervisor/Tests/OwnedInputInjector.cs','Supervisor/Windows/Native.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/WindowsMachineClock.cs']]
 if args.negative_diagnostic_throws:
  original=files[1].read_text(encoding='utf-8')
  needle='}catch{ /* Diagnostics cannot turn the original rejection into exit 1 or a send. */ }'
  assert original.count(needle)==1
  mutated=out/'OwnedInputInjector.cs';mutated.write_text(original.replace(needle,'}catch{ throw; }'),encoding='utf-8');files[1]=mutated
  report['mutation']='diagnostic-exception-escapes-rejection'
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]
 for name,cmd in [('compile',command),('result',[str(out/'Checks.exe'),str(out)])]:
  result=subprocess.run(cmd,capture_output=True,text=True,timeout=30);(out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);report[name+'_exit']=result.returncode
  if name=='result' and args.negative_diagnostic_throws:
   assert result.returncode==1 and 'controlled-write-failure' in result.stderr and 'PASS write-failure-keeps-rejection' not in result.stdout
  else:result.check_returncode()
 assert report['sources']==sources();report['status']='expected-diagnostic-exception-rejected' if args.negative_diagnostic_throws else 'passed-controlled-diagnostic'
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
