"""Check the unchanged Unity baseline with the established batch environment."""
import os
import subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[2]
env=os.environ.copy()
env['ALLUSERSPROFILE']=env.get('ProgramData','C:/ProgramData')
result=subprocess.run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(root/'Prototype'),'-logFile',str(root/'Validation/RainyCafe/baseline-unity.log')],cwd=root,env=env)
print('Unity baseline exit:',result.returncode)
raise SystemExit(result.returncode)
