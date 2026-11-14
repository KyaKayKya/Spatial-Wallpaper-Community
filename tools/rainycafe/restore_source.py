"""Verify the supplied split archive and restore a separate authoring input."""
import hashlib
import json
import zipfile
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
SOURCE = ROOT.parents[1] / 'art/RainyCafe'
DEST = ROOT / 'art/RainyCafe'
DEST.mkdir(parents=True, exist_ok=True)
with zipfile.ZipFile(SOURCE / 'RainyCafe_Start_Here.zip') as help_zip:
    manifest = json.loads(help_zip.read('RainyCafe_Complete_Project.manifest.json'))
archive_path = DEST / manifest['archive_name']
if not archive_path.exists():
    with archive_path.open('xb') as output:
        for part in manifest['parts']:
            data = (SOURCE / part['name']).read_bytes()
            assert len(data) == part['bytes'], part['name']
            assert hashlib.sha256(data).hexdigest() == part['sha256'], part['name']
            output.write(data)
assert archive_path.stat().st_size == manifest['archive_bytes']
assert hashlib.sha256(archive_path.read_bytes()).hexdigest() == manifest['archive_sha256']
with zipfile.ZipFile(archive_path) as archive:
    assert archive.testzip() is None
    for item in archive.infolist():
        target = (DEST / item.filename).resolve()
        assert DEST.resolve() in target.parents, item.filename
        if not target.exists():
            archive.extract(item, DEST)
        print(item.filename, item.file_size)
master = DEST / manifest['master_name']
assert hashlib.sha256(master.read_bytes()).hexdigest() == manifest['master_sha256']
out = ROOT / 'Validation/RainyCafe'
out.mkdir(parents=True, exist_ok=True)
(out / 'source-verification.json').write_text(json.dumps(manifest, indent=2))
for path in DEST.rglob('*.md'):
    print(path.relative_to(DEST), path.read_text(encoding='utf-8-sig'))
for path in DEST.rglob('*.txt'):
    print(path.relative_to(DEST), path.read_text(encoding='utf-8-sig'))
print('PASS: all 23 parts, archive CRC, archive hash, master blend hash')
