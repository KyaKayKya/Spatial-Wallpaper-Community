"""One installed-client content uninstall path; no desktop input or full G claim."""
from pathlib import Path
import argparse,hashlib,json,shutil,subprocess,uuid
from check_install_upgrade import CHECK_FILES,check_sources,extract,make_old_probe
from check_contracts import ROOT,DATA
from windows_framework import references
from toolchain_process import run_toolchain
from verify_delivery import verify_candidate

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--delivery',type=Path,required=True);p.add_argument('--package',type=Path,required=True);a=p.parse_args()
 candidate=a.delivery.resolve();verify_candidate(candidate);run=uuid.uuid4().hex
 out=ROOT/'Validation/Content/sdk-runtime/G/content-uninstall'/run;out.mkdir(parents=True);print(out,flush=True)
 data=(ROOT/'Validation/Sdk'/('un'+run[:10])).resolve();data.mkdir();app=data/'app'
 receipt=check_sources();report={'status':'failed','sources':receipt,'scope':__doc__,'candidate_manifest_sha256':hashlib.sha256((candidate/'assembly-report.json').read_bytes()).hexdigest(),'data':str(data),'process':{}}
 try:
  extract(candidate/'SpatialWallpaperClient-windows-x64.zip',app)
  shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
  built=subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*[str(ROOT/name) for name in CHECK_FILES]],capture_output=True,text=True)
  (out/'compile.log').write_text(built.stdout+built.stderr);built.check_returncode()
  make_old_probe(a.package.resolve(),out/'old-modern.spatial',quaternion=True)
  from store_fixture import create
  create(out/'legacy.spatial');phase=out/'run';phase.mkdir()
  with (phase/'checks.log').open('w') as log:run_toolchain([out/'Checks.exe',phase,app/'player/ContentPlayer.exe',app/'player/sdk-runtime',data,'uninstall'],ROOT,log,180,report['process'])
  report['result']=json.loads((phase/'result.json').read_text(encoding='utf-8-sig'))
  assert report['process']['exit_code']==0 and report['result']['all_retained_kernels_exited'] is True
  assert check_sources()==receipt,'uninstall source drift'
  report['status']='passed-development-installed-content-uninstall-not-RC'
  # The generated installation is a duplicate of the retained candidate ZIP.
  assert app.resolve().parent==data and data.parent==(ROOT/'Validation/Sdk').resolve() and not app.is_symlink() and not app.is_junction()
  for f in app.rglob('*'):assert not f.is_symlink() and not f.is_junction(),'linked test installation'
  report['duplicate_installation_removed_bytes']=sum(f.stat().st_size for f in app.rglob('*') if f.is_file());shutil.rmtree(app)
 finally:
  report['sources_unchanged']=check_sources()==receipt;(out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)

if __name__=='__main__':
 import sys
 sys.path.insert(0,str(ROOT/'tools/content'))
 main()
