"""Build a standalone .NET Framework schema5 validator. No Unity runtime dependency."""
import argparse,hashlib,json,shutil,subprocess
from pathlib import Path
from check_contracts import ROOT, DATA
from windows_framework import references,identity
from release_identity import native_identity

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'NativeValidator.exe.config')
 files=[ROOT/'tools/sdk/NativePackageValidator.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs']
 for folder in ['Security','Data','Execution','Resources','Native']:files+=sorted((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime'/folder).glob('*.cs'))
 before={str(f.relative_to(ROOT)):hashlib.sha256(f.read_bytes()).hexdigest() for f in files}
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-platform:x64','-r:'+str(out/'Newtonsoft.Json.dll'),*references(),'-out:'+str(out/'NativeValidator.exe'),*map(str,files)]
 result=subprocess.run(command,capture_output=True,text=True);(out/'compile.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr)
 if result.returncode:return result.returncode
 assert before=={str(f.relative_to(ROOT)):hashlib.sha256(f.read_bytes()).hexdigest() for f in files},'source drift'
 (out/'build.json').write_text(json.dumps({'identity':native_identity(),'source_commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),'sources':before,'framework':identity(),'runtime_dependencies':['Windows x64','.NET Framework 4.8'],'files':{f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in out.iterdir() if f.is_file()}},indent=2),encoding='utf-8');print(out);return 0
if __name__=='__main__':raise SystemExit(main())
