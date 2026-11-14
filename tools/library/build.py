from pathlib import Path
import subprocess,os
root=Path(__file__).resolve().parents[2]
out=root/'Validation/Library';out.mkdir(parents=True,exist_ok=True)
env=dict(os.environ);env.setdefault('ALLUSERSPROFILE',env.get('PROGRAMDATA','C:/ProgramData'))
result=subprocess.run(['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-quit','-projectPath',str(root/'Prototype'),'-executeMethod','LibraryBuild.Build','-logFile',str(out/'build.log')],env=env)
print('Unity exit',result.returncode,'log',out/'build.log');raise SystemExit(result.returncode)
