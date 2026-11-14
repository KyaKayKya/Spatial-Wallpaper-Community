from pathlib import Path
import subprocess
from check_contracts import DATA,ROOT,OUT as CONTRACT_OUT,compile_checks
OUT=ROOT/'Validation/Content/sdk-runtime/D/checks'
def main():
 if compile_checks():return 1
 OUT.mkdir(parents=True,exist_ok=True)
 mono=str(DATA/'MonoBleedingEdge/bin/mono.exe')
 command=[mono,str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(CONTRACT_OUT/'ContractChecks.exe'),'-out:'+str(OUT/'LedgerChecks.exe'),str(ROOT/'tools/sdk/LedgerChecks.cs'),*map(str,(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Resources').glob('*.cs'))]
 result=subprocess.run(command,capture_output=True,text=True);print(result.stdout+result.stderr)
 if result.returncode:return result.returncode
 import shutil
 shutil.copyfile(CONTRACT_OUT/'ContractChecks.exe',OUT/'ContractChecks.exe')
 return subprocess.call([mono,str(OUT/'LedgerChecks.exe')],timeout=20)
if __name__=='__main__':raise SystemExit(main())
