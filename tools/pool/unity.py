"""Run a Unity batch method in this isolated project and retain its exit status."""
import os
from pathlib import Path
import subprocess
import sys

root = Path(__file__).resolve().parents[2]
method = sys.argv[1]
name = sys.argv[2] if len(sys.argv) > 2 else method
output = root / 'Validation/Pool'
output.mkdir(parents=True, exist_ok=True)
env = dict(os.environ)
env.setdefault('ALLUSERSPROFILE', 'C:/ProgramData')
quit_args = [] if method == 'PoolBakery.Bake' else ['-quit']
result = subprocess.run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe', '-batchmode', *quit_args,
                         '-projectPath', str(root / 'Prototype'), '-executeMethod', method,
                         '-logFile', str(output / (name + '.log'))], env=env)
(output / (name + '.exit.txt')).write_text(str(result.returncode))
print('Unity exit:', result.returncode, 'log:', output / (name + '.log'))
sys.exit(result.returncode)
