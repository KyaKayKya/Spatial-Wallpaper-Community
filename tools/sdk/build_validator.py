"""Build the repository's shared admission core as an independent, author-free Validator."""
from pathlib import Path
import subprocess,sys,shutil,json,hashlib,argparse
from check_contracts import ROOT,DATA
from provenance import sources
from windows_framework import references,identity
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--output',type=Path,required=True);a=p.parse_args();out=a.output.resolve()
 if out.exists():raise ValueError('Validator output must be new')
 out.mkdir(parents=True);receipt=sources();shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Validator.exe.config')
 files=[ROOT/'Supervisor/Validation/ValidatorMain.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WindowsWorkerBoundary.cs']
 for folder in ['Security','Data']:files+=list((ROOT/'Packages/com.spatialwallpaper.sdk/Runtime'/folder).glob('*.cs'))
 from release_identity import build_identity
 version,assembly=build_identity(out);files.append(assembly)
 cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-platform:x64','-r:'+str(out/'Newtonsoft.Json.dll'),*references(),'-out:'+str(out/'Validator.exe'),*map(str,files)]
 subprocess.run(cmd,check=True);assert sources()==receipt,'Validator source drift'
 (out/'build.json').write_text(json.dumps({'identity':version,'sources':receipt,'framework_references':identity(),'files':{f.name:hashlib.sha256(f.read_bytes()).hexdigest() for f in out.iterdir() if f.is_file()}},indent=2));print(out)
if __name__=='__main__':main()
