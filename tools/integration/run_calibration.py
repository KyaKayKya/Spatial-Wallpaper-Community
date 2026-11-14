"""Launch the standalone calibration host with explicit Tracker ownership."""
import argparse
import os
from pathlib import Path
import shutil
import subprocess

root = Path(__file__).resolve().parents[2]
parser = argparse.ArgumentParser()
parser.add_argument('--smoke', action='store_true')
parser.add_argument('--simulate', action='store_true')
parser.add_argument('--devices', action='store_true')
parser.add_argument('--camera')
parser.add_argument('--camera-name', default='Full HD webcam')
parser.add_argument('--diagnostics',type=Path,help='Opt-in numeric eye/projection CSV, never camera images')
args = parser.parse_args()
shared = root.parent.parent
python = root / 'tracker/.venv/Scripts/python.exe'
if not python.exists():
    python = shared / 'tracker/.venv/Scripts/python.exe'
if not python.exists():
    raise SystemExit('Prepare tracker with uv sync --project tracker --locked')
env = dict(os.environ, PYTHONPATH=str(root / 'tracker/src'))
if args.devices:
    raise SystemExit(subprocess.call([str(python), '-m', 'spatial_tracker', 'devices'], cwd=root / 'tracker', env=env))
for source in (shared / 'tracker/.cache/models').glob('*'):
    dest = root / 'tracker/.cache/models' / source.name
    if source.is_file() and not dest.exists():
        dest.parent.mkdir(parents=True, exist_ok=True)
        shutil.copy2(source, dest)
command = [str(root / 'Prototype/Builds/Calibration/CalibrationDemo.exe'),
           '--tracking-python', str(python), '--tracking-root', str(root / 'tracker')]
if args.diagnostics:
    command += ['--calibration-diagnostics',str(args.diagnostics.resolve())]
if args.camera:
    command += ['--tracking-camera', args.camera, '--tracking-camera-name', args.camera_name]
if args.simulate or (args.smoke and not args.camera):
    command += ['--calibration-simulate']
if args.smoke:
    import uuid
    out = root / 'Validation/Calibration' / ('smoke-' + uuid.uuid4().hex[:8])
    out.mkdir(parents=True)
    command += ['--calibration-smoke', str(out), '--data-root', str(out / 'data'), '-logFile', str(out / 'player.log')]
    print('Evidence:', out, flush=True)
raise SystemExit(subprocess.call(command, cwd=root / 'Prototype', env=env))
