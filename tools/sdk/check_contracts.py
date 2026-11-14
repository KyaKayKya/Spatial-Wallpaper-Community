from pathlib import Path
import subprocess,sys,shutil
ROOT=Path(__file__).resolve().parents[2]
DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
OUT=ROOT/'Validation/Content/sdk-runtime/B/checks'
def compile_checks(output=None,framework=False):
 out=OUT if output is None else output
 out.mkdir(parents=True,exist_ok=True)
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
 sources=list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Security').glob('*.cs'))
 if framework:
  from windows_framework import references
  refs=references()
 else:refs=['-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-r:System.IO.Compression.dll','-r:System.IO.Compression.FileSystem.dll']
 cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),*refs,'-out:'+str(out/'ContractChecks.exe'),str(ROOT/'tools/sdk/ContractChecks.cs'),*map(str,(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Data').glob('*.cs')),*map(str,sources)]
 result=subprocess.run(cmd,capture_output=True,text=True)
 if output is not None:(out/'contract-compile.log').write_text(result.stdout+result.stderr,encoding='utf-8')
 print((result.stdout+result.stderr)[:5000]);return result.returncode
if __name__=='__main__':raise SystemExit(compile_checks())
