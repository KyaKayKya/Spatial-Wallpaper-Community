"""Install a verified SDK runtime into an already verified original main build."""
from pathlib import Path
import argparse,hashlib,json,shutil,sys,uuid
from verify_receipt import validate
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from verify_delivery import verify_inventory
from release_identity import identity
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--runtime',type=Path,required=True);args=p.parse_args();receipt=validate()
 source=args.runtime.resolve();manifest=json.loads((source/'runtime.json').read_text());expected=identity();expected.pop('package_sha256');assert {k:manifest[k] for k in expected}==expected;verify_inventory(source,manifest['files'],exclude=['runtime.json'])
 product=ROOT/'Prototype/Builds/Library';target=product/'sdk-runtime';old={name[12:]:sha for name,sha in receipt['files'].items() if name.startswith('sdk-runtime/')}
 assert target.resolve().is_relative_to(product.resolve());assert {p.relative_to(target).as_posix():digest(p) for p in target.rglob('*') if p.is_file()}==old,'unrecorded runtime files must be preserved and reviewed'
 backup=ROOT/'Validation/Content/sdk-runtime/main-manager'/('build-before-runtime-overlay-'+uuid.uuid4().hex+'.json');backup.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(ROOT/'Validation/Desktop/build-receipt.json',backup)
 current={**manifest['files'],'runtime.json':digest(source/'runtime.json')}
 for name,sha in old.items():
  if name not in current:
   owned=(target/name).resolve();assert owned.is_relative_to(target.resolve()) and digest(owned)==sha;owned.unlink()
 for name,sha in current.items():
  destination=(target/name).resolve();assert destination.is_relative_to(target.resolve());destination.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source/name,destination);assert digest(destination)==sha
 verify_inventory(target,manifest['files'],exclude=['runtime.json'])
 receipt['files']={**{n:s for n,s in receipt['files'].items() if not n.startswith('sdk-runtime/')},**{'sdk-runtime/'+n:s for n,s in current.items()}}
 receipt['sdk_runtime_overlay']={'scope':'only trusted runtime binaries replaced after unchanged original Unity build','prior_receipt_sha256':digest(backup),'runtime_manifest_sha256':current['runtime.json'],'identity':expected}
 (ROOT/'Validation/Desktop/build-receipt.json').write_text(json.dumps(receipt,indent=2),encoding='utf-8');validate();print('PASS original Player unchanged; current SDK runtime inventory bound to build receipt')
if __name__=='__main__':main()
