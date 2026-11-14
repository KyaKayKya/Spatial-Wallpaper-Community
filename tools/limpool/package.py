"""Save reproducible local payload and standalone deliverables without vendor redistribution."""
from pathlib import Path
import subprocess,zipfile,hashlib,json,shutil
r=Path(__file__).resolve().parents[2]
common=Path(subprocess.check_output(['git','rev-parse','--path-format=absolute','--git-common-dir'],cwd=r,text=True).strip())
dest=common.parent/'.local-assets/limpool';dest.mkdir(parents=True,exist_ok=True)
files=[]
for folder in ['Prototype/Assets/Limpool/Generated','Prototype/Assets/Limpool/Meshes','Prototype/Assets/Limpool/Lighting']:
    files.extend(p for p in (r/folder).rglob('*') if p.is_file())
files.append(r/'art/limpool/Liminal_Pool_Production.blend')
manifest={str(p.relative_to(r)).replace('\\','/'):{'bytes':p.stat().st_size,'sha256':hashlib.sha256(p.read_bytes()).hexdigest()} for p in files}
with zipfile.ZipFile(dest/'limpool-assets.zip','w',zipfile.ZIP_DEFLATED,compresslevel=3) as z:
    for p in files:z.write(p,p.relative_to(r))
    z.writestr('limpool-manifest.json',json.dumps(manifest,indent=2))
(dest/'limpool-manifest.json').write_text(json.dumps(manifest,indent=2))
delivery=dest/'delivery'
if delivery.exists() and not (dest/'delivery-v1').exists():shutil.copytree(delivery,dest/'delivery-v1')
delivery.mkdir(exist_ok=True)
for rel in ['docs/guides/Limpool场景运行.md','docs/reference/evidence/limpool/验证说明.md','Validation/Limpool/performance.json','Validation/Limpool/audit.json','Validation/Limpool/build-files.json','Validation/Limpool/reflection-stability.json','Validation/Limpool/visual-regression.json','Validation/Limpool/joint-check.json','Validation/Limpool/sky-source.json']:
    q=delivery/'Documentation'/rel;q.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(r/rel,q)
shutil.copytree(r/'Prototype/Builds/Limpool',delivery/'Player',dirs_exist_ok=True)
for p in (r/'Validation/Limpool').rglob('*'):
    if not p.is_file() or 'package-docs' in p.parts:continue
    relative=p.relative_to(r/'Validation/Limpool')
    if len(relative.parts)>1 and relative.parts[0] not in ['stills-default','stills-low','hold-default','video-default','bench-default','bench-no-volume','bench-no-waterlight','bench-reflection-low','bench-no-reflection','bench-low','prefilter-before','prefilter-after','stills-split-energy']:continue
    if len(relative.parts)==1 and p.name not in ['build.log','build-result.txt','bake.log','bake-result.txt','export.json','source-model.json','build-files.json','audit.json','performance.json','performance.md','sky-source.json','visual-regression.json','joint-check.json','reflection-stability.json','tracked-water-contact.png','comparison.png','boundary-contact.png','water-detail.png','wall-light-detail.png','beam-detail.png','limpool-hold-default.mp4','limpool-video-default.mp4']:continue
    if p.suffix=='.png' and p.parent.name.startswith(('video-','hold-')) and p.stem not in ['0000','0300','0600','0899']:continue
    if p.suffix=='.png' and p.parent.name.startswith('prefilter-') and p.stem not in ['0000','0040','0041','0042','0043','0044','0045','0075','0150','0225','0299','0340','0341','0342','0375','0450','0525','0600','0640','0641','0642','0675','0750','0825','0899']:continue
    q=delivery/'Evidence'/p.relative_to(r/'Validation/Limpool');q.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(p,q)
for obsolete in ['glint-comparison.json','glint-comparison.png']:
    p=delivery/'Evidence'/obsolete
    if p.is_file():p.unlink()
fingerprint=json.loads((r/'Validation/Limpool/audit.json').read_text())['build_fingerprint']
snapshot=dest/('delivery-'+fingerprint[:12]);shutil.copytree(delivery,snapshot,dirs_exist_ok=True)
print(dest)
print(snapshot)
