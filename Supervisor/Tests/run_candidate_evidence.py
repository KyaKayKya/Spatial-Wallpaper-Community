"""Pure candidate diagnostic controls; no desktop, helper, input or Unity."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib,shutil,argparse
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references
parser=argparse.ArgumentParser();parser.add_argument('--negative-omit-raw-hit',action='store_true');args=parser.parse_args()
out=root/'Validation/Content/sdk-runtime/E/candidate-evidence'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
files=[root/'Supervisor/Tests/NativeDesktopInputCandidateChecks.cs',root/'Supervisor/Tests/DesktopCandidateEvidenceChecks.cs']
receipt={str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files if p.exists()}
if args.negative_omit_raw_hit:
 text=files[0].read_text(encoding='utf-8-sig');needle='row["hit"]=query()??(JToken)JValue.CreateNull();';assert text.count(needle)==1
 copy=out/'Candidate.cs';copy.write_text(text.replace(needle,'query();row["hit"]=new JObject();'),encoding='utf-8');files[0]=copy
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
report={'scope':'pure-candidate-diagnostics','sources':receipt,'status':'failed','negative_omit_raw_hit':args.negative_omit_raw_hit}
try:
 for name,command in [('compile',[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,[p for p in files if p.exists()])]),('checks',[str(out/'Checks.exe')])]:
  result=subprocess.run(command,capture_output=True,text=True,timeout=15);log=result.stdout+result.stderr;(out/(name+'.log')).write_text(log,encoding='utf-8');print(log)
  if name=='checks' and args.negative_omit_raw_hit:assert result.returncode==1 and 'System.Exception: raw-nonblank-candidate-retained' in log
  else:result.check_returncode()
 assert all(hashlib.sha256((root/p).read_bytes()).hexdigest()==h for p,h in receipt.items());report['status']='expected-missing-raw-hit-rejected' if args.negative_omit_raw_hit else 'passed'
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
