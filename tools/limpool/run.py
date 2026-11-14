from pathlib import Path
import subprocess,os,sys
r=Path(__file__).resolve().parents[2];out=r/'Validation/Limpool';out.mkdir(parents=True,exist_ok=True)
mode=sys.argv[1];env=dict(os.environ);env.setdefault('ALLUSERSPROFILE','C:/ProgramData')
if mode=='export':
    cmd=['D:/Blender/blender.exe','--background','--factory-startup','--python',str(r/'tools/limpool/export_scene.py')]
else:
    cmd=['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe','-batchmode','-projectPath',str(r/'Prototype'),'-logFile',str(out/(mode+'.log'))]
    if mode!='bake':cmd+=['-quit']
    if mode!='baseline':cmd+=['-executeMethod',{'create':'LimpoolBuilder.Create','build':'LimpoolBuilder.Build','bake':'LimpoolBake.Bake'}[mode]]
with (out/(mode+'-stdout.log')).open('w') as log:p=subprocess.run(cmd,env=env,stdout=log,stderr=subprocess.STDOUT)
if mode=='build' and 'Shader error' in (out/'build.log').read_text(errors='replace'):
    print('Shader compilation failed; build is not valid');sys.exit(2)
(out/(mode+'.exit.txt')).write_text(str(p.returncode));print(mode,'exit',p.returncode,flush=True);sys.exit(p.returncode)
