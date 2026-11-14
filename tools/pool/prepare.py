"""Restore Pool source assets and the pinned MIT shader graphs into this checkout."""
from pathlib import Path
import hashlib
import json
import shutil
import subprocess
import zipfile

ROOT = Path(__file__).resolve().parents[2]
COMMON = Path(subprocess.check_output(['git', 'rev-parse', '--path-format=absolute', '--git-common-dir'], cwd=ROOT, text=True).strip()).parent
archive = next((COMMON / 'art/Pool').glob('*.zip'))
target = ROOT / 'Prototype/Assets/Pool'
with zipfile.ZipFile(archive) as bundle:
    prefix = 'Poolcore_Static_v2/'
    for line in bundle.read(prefix + 'checksums.sha256').decode().splitlines():
        digest, name = line.split(maxsplit=1)
        name = name.lstrip('*')
        payload = bundle.read(prefix + name)
        if hashlib.sha256(payload).hexdigest() != digest:
            raise ValueError('Source checksum mismatch: ' + name)
    for name in bundle.namelist():
        relative = name.removeprefix(prefix)
        if relative == 'export/pool_static.fbx':
            dest = target / 'Models/pool_static.fbx'
        elif relative.startswith('assets/textures/'):
            dest = target / 'Textures' / Path(relative).name
        elif relative in ('SOURCES_AND_LICENSES.md', 'export/validation_manifest.json', 'export/revision_validation.json'):
            dest = target / 'Source' / Path(relative).name
        else:
            continue
        dest.parent.mkdir(parents=True, exist_ok=True)
        dest.write_bytes(bundle.read(name))
vendor = ROOT / '.local-assets/urp-water'
revision = subprocess.check_output(['git', '-C', str(vendor), 'rev-parse', 'HEAD'], text=True).strip()
if revision != '1d18105ab76f894e3187d32bb4e412dbcb35df2c':
    raise ValueError('Unreviewed water revision: ' + revision)
third = ROOT / 'Prototype/Assets/ThirdParty/URPWater'
shutil.copytree(vendor / 'Assets/Graphs', third / 'Graphs', dirs_exist_ok=True)
third.mkdir(parents=True, exist_ok=True)
shutil.copy2(vendor / 'LICENSE', third / 'LICENSE.txt')
shutil.copy2(vendor / 'README.md', third / 'README.md')
manifest = dict(source_archive=archive.name, source_sha256=hashlib.sha256(archive.read_bytes()).hexdigest(),
                water_repository='https://github.com/aniruddhahar/URP-WaterShaders', water_commit=revision,
                water_license='MIT', source_meshes=40, source_triangles=13700)
(target / 'Source/import-manifest.json').write_text(json.dumps(manifest, indent=2) + '\n', encoding='utf-8')
print(json.dumps(manifest))
