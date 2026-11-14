"""Exercise only installation-check status discovery; no player or desktop input."""
from pathlib import Path
import hashlib,json,shutil,subprocess,uuid
from check_install_upgrade import CHECK_FILES,ROOT,DATA
from windows_framework import references

def main():
 out=ROOT/'Validation/Content/sdk-runtime/G/install-status-read'/uuid.uuid4().hex
 out.mkdir(parents=True);print(out,flush=True)
 files=[ROOT/name for name in CHECK_FILES]+[ROOT/'Supervisor/Tests/InstallStatusReadChecks.cs']
 report={'status':'failed','sources':{str(p.relative_to(ROOT)):hashlib.sha256(p.read_bytes()).hexdigest() for p in files},'scope':__doc__}
 try:
  shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
  shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
  compile_command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-main:InstallStatusReadChecks',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]
  for name,command in [('compile',compile_command),('checks',[str(out/'Checks.exe'),str(out/'state')])]:
   result=subprocess.run(command,capture_output=True,text=True,timeout=30)
   (out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8')
   report[name+'_exit']=result.returncode;result.check_returncode()
  report['status']='passed-install-check-status-discovery-only'
 finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
if __name__=='__main__':main()
