"""Restore the locally licensed Evo dependency without changing its GUIDs."""
from pathlib import Path
import tarfile
import hashlib
import shutil
import io
import gzip

root = Path(__file__).resolve().parents[2]
package = next(Path('D:/Unity/Plugins').glob('Evo*/**/*1.9.9.unitypackage'))
assert hashlib.sha256(package.read_bytes()).hexdigest() == '93563f754e691e615f9374af7e38af8fe015ad13fbf493c11b53c479a3cd326d'
with tarfile.open(fileobj=io.BytesIO(gzip.decompress(package.read_bytes())), mode='r:') as archive:
    members = {m.name: m for m in archive.getmembers()}
    restored = 0
    for key, member in members.items():
        if not key.endswith('/pathname'):
            continue
        path = archive.extractfile(member).read().decode().splitlines()[0].rstrip('\x00')
        if not path.startswith('Assets/Evo/') or '..' in Path(path).parts:
            continue
        prefix = key.rsplit('/', 1)[0]
        target = root / 'Prototype' / path
        asset = members.get(prefix + '/asset')
        if asset:
            target.parent.mkdir(parents=True, exist_ok=True)
            data = archive.extractfile(asset).read()
            if target.exists() and target.read_bytes() != data:
                raise RuntimeError('Conflicting local asset: ' + path)
            target.write_bytes(data)
        else:
            target.mkdir(parents=True, exist_ok=True)
        meta = members.get(prefix + '/asset.meta')
        if meta:
            Path(str(target) + '.meta').write_bytes(archive.extractfile(meta).read())
        restored += 1
print('Restored Evo entries:', restored)
