"""Restore hash-pinned local cafe LOD assets without depending on Validation."""
from pathlib import Path
import hashlib,json,zipfile
def restore(common):
 manifest=json.loads(Path(__file__).with_name('cafe-quality-assets.json').read_text())
 archive=Path(common)/manifest['archive'];assert hashlib.sha256(archive.read_bytes()).hexdigest()==manifest['sha256'],'cafe-quality-archive-digest'
 target=archive.with_suffix('')
 with zipfile.ZipFile(archive) as z:
  assert set(z.namelist())==set(manifest['files']),'cafe-quality-archive-inventory'
  for name,digest in manifest['files'].items():
   path=target/name;assert path.resolve().is_relative_to(target.resolve())
   content=z.read(name);assert hashlib.sha256(content).hexdigest()==digest,name
   if path.exists():assert hashlib.sha256(path.read_bytes()).hexdigest()==digest,'changed local asset: '+name
   else:path.parent.mkdir(parents=True,exist_ok=True);path.write_bytes(content)
 return target
