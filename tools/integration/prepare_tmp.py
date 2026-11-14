from pathlib import Path
import tarfile
import io
import gzip
root = Path(__file__).resolve().parents[2] / 'Prototype'
package = next((root / 'Library/PackageCache').glob('com.unity.textmeshpro*/Package Resources/TMP Essential Resources.unitypackage'))
with tarfile.open(fileobj=io.BytesIO(gzip.decompress(package.read_bytes())), mode='r:') as archive:
    members = {m.name: m for m in archive.getmembers()}
    for name, item in members.items():
        if not name.endswith('/pathname'):
            continue
        path = archive.extractfile(item).read().decode().splitlines()[0].rstrip('\x00')
        if not path.startswith('Assets/') or '..' in Path(path).parts:
            raise ValueError(path)
        key = name.rsplit('/', 1)[0]
        dest = root / path
        if key + '/asset' in members:
            dest.parent.mkdir(parents=True, exist_ok=True)
            if not dest.exists():
                dest.write_bytes(archive.extractfile(members[key + '/asset']).read())
        else:
            dest.mkdir(parents=True, exist_ok=True)
        meta = Path(str(dest) + '.meta')
        if key + '/asset.meta' in members and not meta.exists():
            meta.write_bytes(archive.extractfile(members[key + '/asset.meta']).read())
print('TMP essential resources restored')
