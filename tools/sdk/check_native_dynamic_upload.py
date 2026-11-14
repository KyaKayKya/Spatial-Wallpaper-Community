"""Small native upload budget/authority checks; does not claim GPU or author bridge acceptance."""
import argparse,json,shutil,subprocess
from pathlib import Path
from check_contracts import ROOT,DATA
from windows_framework import references

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
 files=[ROOT/'tools/sdk/NativeDynamicUploadChecks.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs']
 for folder in ['Security','Data','Execution','Resources','Native']:files+=sorted((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime'/folder).glob('*.cs'))
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),*references(),'-out:'+str(out/'NativeDynamicUploadChecks.exe'),*map(str,files)]
 compile=subprocess.run(command,capture_output=True,text=True);(out/'compile.log').write_text(compile.stdout+compile.stderr)
 if compile.returncode:print(compile.stdout+compile.stderr);return compile.returncode
 result=subprocess.run([str(out/'NativeDynamicUploadChecks.exe')],capture_output=True,text=True,timeout=20);(out/'result.log').write_text(result.stdout+result.stderr);(out/'report.json').write_text(json.dumps({'exit_code':result.returncode,'scope':'bounded staging, vertex streams/submeshes, owner/generation, revocation, cancellation and budget; not Unity GPU/author bridge'},indent=2));print(result.stdout+result.stderr);return result.returncode
if __name__=='__main__':raise SystemExit(main())
