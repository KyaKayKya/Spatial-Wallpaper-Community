"""Build the standalone trusted SDK preview; no Unity Editor runtime dependency."""
from pathlib import Path
import argparse,subprocess,shutil,json,hashlib
from check_contracts import ROOT,DATA
from provenance import sources
from windows_framework import references,identity
from trusted_client_sources import client_sources
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path,required=True);parser.add_argument('--source-override',type=Path);args=parser.parse_args();out=args.output.resolve()
 if out.exists():raise ValueError('preview output must be a new directory')
 out.mkdir(parents=True);receipt=sources();from release_identity import build_identity
 version,assembly=build_identity(out)
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'SpatialWallpaper.Preview.exe.config')
 files=client_sources(ROOT)+list((ROOT/'Supervisor/Client').glob('*.cs'))+[assembly]
 report={'status':'failed','sources':receipt,'identity':version,'framework_references':identity(),'generated_identity_sha256':hashlib.sha256(assembly.read_bytes()).hexdigest()}
 if args.source_override:
  override=args.source_override.resolve();assert override.is_relative_to(ROOT/'Validation/Content/sdk-runtime') and override.name in ['ContentSurfaceProgram.cs','NativeInputSourceProgram.cs'],'only owned cleanup fault fixture source may be overridden'
  original=ROOT/'Supervisor/Client'/override.name;assert original in files;files=[override if f==original else f for f in files];report['mutated_sources_sha256']={original.relative_to(ROOT).as_posix():hashlib.sha256(override.read_bytes()).hexdigest()}
 try:
  command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-optimize+','-target:winexe','-main:SpatialWallpaper.Supervision.Preview.PreviewProgram',*references(),'-r:System.Drawing.dll','-r:System.Windows.Forms.dll','-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'SpatialWallpaper.Preview.exe'),*map(str,files)]
  done=subprocess.run(command,capture_output=True,text=True);(out/'compile.log').write_text(done.stdout+done.stderr);print(done.stdout+done.stderr);report['compile_exit_code']=done.returncode;done.check_returncode();assert sources()==receipt,'preview build source drift';report['status']='passed-development-preview-build'
  from experiments.native_build import compile_probe
  compile_probe(ROOT/'Supervisor/Interactive/DesktopHitTester.cpp',out/'DesktopHitTester.exe')
 finally:
  report['sources_unchanged']=sources()==receipt;report['files']={name:hashlib.sha256((out/name).read_bytes()).hexdigest() for name in ['SpatialWallpaper.Preview.exe','SpatialWallpaper.Preview.exe.config','Newtonsoft.Json.dll','DesktopHitTester.exe'] if (out/name).is_file()};(out/'build.json').write_text(json.dumps(report,indent=2));print(out)
if __name__=='__main__':main()
