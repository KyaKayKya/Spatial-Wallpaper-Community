from pathlib import Path
import sys,subprocess
from check_contracts import ROOT,DATA,OUT,compile_checks
def main():
 if compile_checks():return 1
 OUT.mkdir(parents=True,exist_ok=True)
 sources=[ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs']+list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Execution').glob('*.cs'))+list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Resources').glob('*.cs'))
 cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(OUT/'ContractChecks.exe'),'-r:'+str(OUT/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(OUT/'ExecutionChecks.exe'),str(ROOT/'tools/sdk/ExecutionChecks.cs'),str(ROOT/'tools/sdk/InstructionChecks.cs'),str(ROOT/'tools/sdk/InventoryChecks.cs'),*map(str,sources)]
 result=subprocess.run(cmd,capture_output=True,text=True);print(result.stdout+result.stderr)
 if result.returncode:return result.returncode
 from fixtures import sample,archive,ins
 package=OUT/'execution.spatial';package.write_bytes(archive(*sample()))
 cases=[]
 for name,body in [('loop',[ins('br',0)]),('recursive',[ins('ldarg',0),ins('invoke','Demo.Update'),ins('ret')])]:
  p,c=sample();p['classes'][0]['methods'][0]['body']=body;path=OUT/(name+'.spatial');path.write_bytes(archive(p,c));cases.append(str(path))
 from instruction_fixture import create
 instruction=OUT/'instructions.spatial';instruction.write_bytes(archive(*create()))
 return subprocess.call([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(OUT/'ExecutionChecks.exe'),str(package),*cases,'--instructions',str(instruction)],timeout=20)
if __name__=='__main__':raise SystemExit(main())
