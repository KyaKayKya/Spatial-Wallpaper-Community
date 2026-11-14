"""Related Tracker cancellation boundary and authenticated disconnect checks."""
from pathlib import Path
import argparse,subprocess,os,json,hashlib,uuid
from check_contracts import ROOT
p=argparse.ArgumentParser();p.add_argument('--related',action='store_true');args=p.parse_args()
out=ROOT/'Validation/Content/sdk-runtime/F/tracking-writer'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
files=[ROOT/'tracker/src/spatial_tracker/worker.py',ROOT/'tracker/tests/test_worker.py'];receipt={f.relative_to(ROOT).as_posix():hashlib.sha256(f.read_bytes()).hexdigest() for f in files};report={'status':'failed','sources':receipt,'scope':__doc__}
try:
 common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=ROOT,text=True).strip()).parent
 selected='disconnect_release or runtime_identity_mismatch or write_loop_observes_shutdown_after_completed_drain' if args.related else 'write_loop_observes_shutdown_after_completed_drain'
 env=os.environ.copy();env['PYTHONPATH']=str(ROOT/'tracker/src')
 done=subprocess.run([str(common/'tracker/.venv/Scripts/python.exe'),'-m','pytest','tests/test_worker.py','-k',selected,'-q','--junitxml='+str(out/'junit.xml')],cwd=ROOT/'tracker',env=env,capture_output=True,text=True,timeout=30);(out/'checks.log').write_text(done.stdout+done.stderr);report['exit_code']=done.returncode;done.check_returncode();report['status']='passed-development-tracker-writer-shutdown'
finally:
 report['sources_unchanged']=receipt=={f.relative_to(ROOT).as_posix():hashlib.sha256(f.read_bytes()).hexdigest() for f in files};(out/'report.json').write_text(json.dumps(report,indent=2));print(out)
