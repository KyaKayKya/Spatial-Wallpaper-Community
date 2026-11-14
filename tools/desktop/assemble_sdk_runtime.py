"""Reuse the verified Worker/Host and replace only the changed trusted presenter."""
from pathlib import Path
import argparse,hashlib,json,shutil,sys
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk'))
from release_identity import identity
from verify_delivery import verify_inventory
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--source',type=Path,required=True);p.add_argument('--preview',type=Path,required=True);p.add_argument('--host',type=Path);p.add_argument('--worker',type=Path);p.add_argument('--output',type=Path,required=True);args=p.parse_args()
 source=args.source.resolve();preview=args.preview.resolve();out=args.output.resolve();assert not out.exists(),'use a fresh owned destination'
 expected=identity();expected.pop('package_sha256');manifest=json.loads((source/'runtime.json').read_text());assert {k:manifest[k] for k in expected}==expected
 for name,digest in manifest['files'].items():assert hashlib.sha256((source/name).read_bytes()).hexdigest()==digest,name
 build=json.loads((preview/'build.json').read_text());assert build['status']=='passed-development-preview-build' and build['identity']==expected
 out.mkdir(parents=True)
 for name in manifest['files']:
  if name in ('BuildIdentity.cs','compile.log','DesktopHitTester.obj'):continue
  target=out/name;target.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source/name,target)
 for name in ('SpatialWallpaper.Preview.exe','SpatialWallpaper.Preview.exe.config','Newtonsoft.Json.dll','DesktopHitTester.exe','build.json'):shutil.copy2(preview/name,out/name)
 if args.host:
  host=args.host.resolve();receipt=json.loads((host/'build.json').read_text());assert receipt['identity']==expected and receipt['host_cleanup_protocol']==1
  for name in ('Supervisor.exe','Supervisor.exe.config','AudioSampler.exe'):
   assert hashlib.sha256((host/name).read_bytes()).hexdigest()==receipt['files'][name];shutil.copy2(host/name,out/'supervisor'/name)
  shutil.copy2(host/'build.json',out/'supervisor/build.json')
 if args.worker:
  worker=args.worker.resolve();receipt=json.loads((worker/'report.json').read_text());assert receipt['identity']==expected and receipt['status']=='production-Worker-built-not-acceptance'
  for name,digest in receipt['runtime_files'].items():assert hashlib.sha256((worker/'player'/name).read_bytes()).hexdigest()==digest,name
  destination=out/'worker'
  if destination.exists():
   assert destination.resolve().is_relative_to(out)
   shutil.rmtree(destination)
  shutil.copytree(worker/'player',destination/'player')
  shutil.copy2(worker/'report.json',destination/'report.json')
 manifest['files']={p.relative_to(out).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in out.rglob('*') if p.is_file()};(out/'runtime.json').write_text(json.dumps(manifest,indent=2),encoding='utf-8')
 verify_inventory(out,manifest['files'],exclude=['runtime.json']);print('Verified compatible main product runtime:',out)
if __name__=='__main__':main()
