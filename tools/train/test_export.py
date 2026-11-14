"""Validate actual exported Train geometry, independent of the camera."""
import json,hashlib
from pathlib import Path
root=Path(__file__).resolve().parents[2]
p=root/'Prototype/Assets/Train/Generated/manifest.json'
assert p.exists(), 'Train evaluated geometry and bake manifest not exported'
d=json.loads(p.read_text())
assert d['source_sha256'] and d['objects']
w,h,depth=d['volume']
assert abs(w/h-16/9)<1e-6
for o in d['objects']:
    for v in o['bounds']:
        assert -w/2-1e-5<=v[0]<=w/2+1e-5,(o['name'],v)
        assert -h/2-1e-5<=v[1]<=h/2+1e-5,(o['name'],v)
        assert -1e-5<=v[2]<=depth+1e-5,(o['name'],v)
assert all((p.parent/f).exists() for f in d['files'])
assert set(d['sha256'])==set(d['files'])
for f,expected in d['sha256'].items():assert hashlib.sha256((p.parent/f).read_bytes()).hexdigest()==expected,f
for corner in [[-w/2,-h/2,0],[w/2,-h/2,0]]:
    assert any(sum((a-b)**2 for a,b in zip(corner,v))<1e-12 for v in d['floor_vertices'])
print('PASS: complete evaluated bounds, 16:9 volume and actual floor front corners')
