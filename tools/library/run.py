"""Start the artwork library. The camera opens only after applying settings in the app."""
from pathlib import Path
import argparse,os,shutil,subprocess,uuid,time
root=Path(__file__).resolve().parents[2]
main=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=root,text=True).strip()).parent
parser=argparse.ArgumentParser();parser.add_argument('--smoke',action='store_true');parser.add_argument('--simulate',action='store_true');args=parser.parse_args()
python=next((p for p in [root/'tracker/.venv/Scripts/python.exe',main/'tracker/.venv/Scripts/python.exe',main/'.worktrees/manual-calibration/tracker/.venv/Scripts/python.exe'] if p.exists()),None)
if python is None:raise SystemExit('Prepare tracker: uv sync --project tracker --locked')
for source in [main/'tracker/.cache/models',main/'.worktrees/manual-calibration/tracker/.cache/models']:
 if source.exists():shutil.copytree(source,root/'tracker/.cache/models',dirs_exist_ok=True)
env=dict(os.environ,PYTHONPATH=str(root/'tracker/src'))
command=[str(root/'Prototype/Builds/Library/SpatialWallpaper.exe'),'--tracking-python',str(python),'--tracking-root',str(root/'tracker')]
if args.smoke or args.simulate:command+=['--calibration-simulate']
if args.smoke:
 out=root/'Validation/Library'/('smoke-'+uuid.uuid4().hex[:8]);out.mkdir(parents=True)
 command+=['--library-smoke',str(out),'--data-root',str(out/'data'),'-logFile',str(out/'player.log')]
 print('Evidence:',out,flush=True)
 with subprocess.Popen(command,cwd=root/'Prototype',env=env) as player:
  try:
   deadline=time.monotonic()+240
   while player.poll() is None and not (out/'errors.txt').exists() and time.monotonic()<deadline:time.sleep(.5)
   if (out/'errors.txt').exists():player.terminate();player.wait();raise SystemExit((out/'errors.txt').read_text())
   code=player.wait(timeout=max(1,deadline-time.monotonic()))
  except subprocess.TimeoutExpired:player.terminate();player.wait();raise SystemExit('FAIL Player timed out; see '+str(out))
 if code!=0 or not (out/'result.txt').exists():raise SystemExit('FAIL Player: '+str(code)+'; see '+str(out))
 print((out/'result.txt').read_text());raise SystemExit(0)
raise SystemExit(subprocess.call(command,cwd=root/'Prototype',env=env))
