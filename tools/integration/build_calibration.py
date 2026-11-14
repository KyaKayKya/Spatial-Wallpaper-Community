from pathlib import Path
import os
import subprocess
import sys
root = Path(__file__).resolve().parents[2]
out = root / 'Validation/Calibration'
out.mkdir(parents=True, exist_ok=True)
env = dict(os.environ)
env.setdefault('ALLUSERSPROFILE', env.get('PROGRAMDATA', 'C:/ProgramData'))
args = ['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe', '-batchmode', '-quit',
        '-projectPath', str(root / 'Prototype'), '-executeMethod', 'CalibrationDemoBuild.Build',
        '-logFile', str(out / 'build.log')]
result = subprocess.run(args, env=env)
print('Unity exit', result.returncode, 'log', out / 'build.log')
sys.exit(result.returncode)
