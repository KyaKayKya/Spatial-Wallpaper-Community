"""Original Tracker TCP shutdown and retained-owner retry after unproved exit."""
from pathlib import Path
import argparse, subprocess, uuid, shutil, json, hashlib
from check_contracts import ROOT, DATA
from windows_framework import references
from toolchain_process import run_toolchain

p=argparse.ArgumentParser();p.add_argument('--case',choices=['normal','unproven'],default='normal');args=p.parse_args()
out=ROOT/'Validation/Content/sdk-runtime/F/tracking-host'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
core=list((ROOT/'Prototype/Assets/Tracking/Core').glob('*.cs'))
inputs=[*core,*(ROOT/'tracker/src').rglob('*.py'),ROOT/'tracker/uv.lock',ROOT/'tracker/pyproject.toml',ROOT/'Prototype/Assets/StreamingAssets/tracking-calibration-synthetic.json',ROOT/'Supervisor/Tests/TrackingHostChecks.cs',Path(__file__)]
def receipt():return {f.relative_to(ROOT).as_posix():hashlib.sha256(f.read_bytes()).hexdigest() for f in inputs}
report={'status':'failed','case':args.case,'sources':receipt(),'scope':__doc__}
try:
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
 shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
 with (out/'compile.log').open('w') as log:
  subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-define:SDK_TRACKING_TESTS',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),str(ROOT/'Supervisor/Tests/TrackingHostChecks.cs'),*map(str,core)],stdout=log,stderr=subprocess.STDOUT,check=True,timeout=30)
 common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent
 report['process']={}
 with (out/'checks.log').open('w') as log:
  run_toolchain([out/'Checks.exe',common/'tracker/.venv/Scripts/python.exe',ROOT/'tracker',ROOT/'Prototype/Assets/StreamingAssets/tracking-calibration-synthetic.json',out/'result.json',args.case],ROOT,log,30,report['process'])
 assert receipt()==report['sources'],'tracking-inputs-changed-during-check'
 report['result']=json.loads((out/'result.json').read_text());report['status']='passed-development-original-tracking-host'
finally:
 report['sources_unchanged']=receipt()==report['sources'];(out/'report.json').write_text(json.dumps(report,indent=2));print(out)
