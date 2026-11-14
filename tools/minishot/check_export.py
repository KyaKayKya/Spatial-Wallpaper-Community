"""Check actual exported geometry independently of Unity and Blender's validation reports."""
import hashlib
import json
import math
import struct
from pathlib import Path

ROOT = Path(__file__).resolve().parents[2]
GENERATED = ROOT / 'Prototype/Assets/MiniShotGame/Generated'


def check():
    manifest_path = GENERATED / 'manifest.json'
    assert manifest_path.is_file(), 'MiniShotGame geometry has not been exported'
    manifest = json.loads(manifest_path.read_text(encoding='utf-8'))
    assert manifest['screen'] == [1.6, .9]
    objects = {}
    for item in manifest['objects']:
        data = (GENERATED / item['file']).read_bytes()
        assert hashlib.sha256(data).hexdigest() == item['sha256'], item['file']
        magic, count, indices = struct.unpack_from('<III', data)
        assert magic == 0x4D534731 and indices % 3 == 0
        assert len(data) == 12 + count * 28 + indices * 4
        vertices = list(struct.iter_unpack('<7f', data[12:12+count*28]))
        faces = struct.unpack_from('<%dI' % indices, data, 12+count*28)
        assert max(faces) < count
        assert all(math.isfinite(x) for v in vertices for x in v)
        objects[item['name']] = vertices
    assert len(objects) == 28
    floor = objects['Floor']
    assert all(abs(v[1]+.45)<1e-6 for v in floor)
    for x in [-.8,.8]:
        assert any(math.dist(v[:3], (x,-.45,0))<1e-6 for v in floor)
    targets = manifest['targets']
    assert len(targets) == 10
    assert sum(t['center'][2] < 0 for t in targets) == 2
    for t in targets:
        v = objects[t['name']]
        cx,cy,cz = t['center']
        radii = [math.hypot(q[0]-cx,q[1]-cy) for q in v]
        assert min(radii)<1e-6, 'Filled center missing: '+t['name']
        assert abs(max(radii)-t['radius'])<1e-6
        assert abs(max(q[0] for q in v)-min(q[0] for q in v)-2*t['radius'])<1e-6
        assert abs(max(q[1] for q in v)-min(q[1] for q in v)-2*t['radius'])<1e-6
        assert abs(max(q[2] for q in v)-min(q[2] for q in v)-.004)<1e-6
        trail = objects['Trail_'+t['name']]
        assert all(abs(q[0]-cx)<.00116 and abs(q[1]-cy)<.00116 for q in trail)
        assert abs(max(q[2] for q in trail)-3.6)<1e-6
        assert all(max(q[3:6]) == 0 for q in trail if q[2]>2.05)
    report = {'pass':True,'objects':len(objects),'filledRoundTargets':10,'foregroundTargets':2,
              'actualFloorCorners':True,'blackTrailEnds':True,'sourceSha256':manifest['sourceSha256']}
    output = ROOT/'Validation/MiniShotGame'
    output.mkdir(parents=True,exist_ok=True)
    (output/'export-checks.json').write_text(json.dumps(report,indent=2)+'\n',encoding='utf-8')
    print(json.dumps(report))


if __name__ == '__main__':
    check()
