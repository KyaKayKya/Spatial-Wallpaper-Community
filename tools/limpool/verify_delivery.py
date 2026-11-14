"""Verify the copied Player and evidence, not just the worktree originals."""
from pathlib import Path
import subprocess,json,hashlib,sys
r=Path(__file__).resolve().parents[2]
common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=r,text=True).strip())
d=common.parent/'.local-assets/limpool/delivery'
if '--snapshot' in sys.argv:
    fingerprint=json.loads((r/'Validation/Limpool/audit.json').read_text())['build_fingerprint']
    d=d.with_name('delivery-'+fingerprint[:12])
def sha(p):
    h=hashlib.sha256()
    with p.open('rb') as f:
        for block in iter(lambda:f.read(4*1024*1024),b''):h.update(block)
    return h.hexdigest()
manifest=json.loads((r/'Validation/Limpool/build-files.json').read_text())
assert {str(p.relative_to(d/'Player')) for p in (d/'Player').rglob('*') if p.is_file()}==set(manifest),'Delivery has extra or missing Player files'
for rel,digest in manifest.items():assert sha(d/'Player'/rel)==digest,rel
for name in ['limpool-hold-default.mp4','limpool-video-default.mp4','reflection-stability.json','performance.json','audit.json','tracked-water-contact.png']:
    assert sha(d/'Evidence'/name)==sha(r/'Validation/Limpool'/name),name
for rel in ['docs/guides/Limpool场景运行.md','docs/reference/evidence/limpool/验证说明.md']:
    assert sha(d/'Documentation'/rel)==sha(r/rel),rel
print('PASS: copied Player, videos, regression, performance and documentation match the verified build')
