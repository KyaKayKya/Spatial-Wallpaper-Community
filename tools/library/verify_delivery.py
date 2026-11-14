"""Check the tracked library delivery's metadata and documentation links."""
from pathlib import Path
import re,sys,subprocess
root=Path(__file__).resolve().parents[2]
assets=root/'Prototype/Assets/Library'
if '--normalize' in sys.argv:
 for item in [Path(str(assets)+'.meta'),*assets.rglob('*')]:
  if item.is_file() and item.suffix in ['.meta','.unity']:
   text=item.read_text(encoding='utf-8');item.write_bytes(('\n'.join(line.rstrip() for line in text.splitlines())+'\n').encode('utf-8'))
for item in [assets,*assets.rglob('*')]:
 if item.suffix=='.meta':continue
 assert Path(str(item)+'.meta').exists(),f'Missing Unity metadata: {item}'
for name in ['showcase','pool','train','parking','minishotgame','limpool','rainycafe']:
 for rel in [f'Scenes/{name}.unity',f'Resources/Library/{name}.jpg']:
  assert (assets/rel).is_file(),f'Missing shipped artwork asset: {rel}'
assert (assets/'Resources/Library/no-wallpaper.png').is_file(), 'Missing common no-wallpaper cover'
for name in ['ManagerIcons.asset','ManagerSurface.mat']:
 item=root/'Prototype/Assets/Calibration/Resources'/name
 assert item.is_file() and Path(str(item)+'.meta').is_file(),f'Missing shared UI resource: {name}'
docs=['README.md','Prototype/README.md','docs/README.md','docs/guides/作品库运行.md','docs/reference/evidence/library/作品库验证.md']
for rel in docs:
 file=root/rel
 for target in re.findall(r'\]\(([^)]+)\)',file.read_text(encoding='utf-8-sig')):
  if '://' in target or target.startswith('#'):continue
  target=target.split('#')[0]
  assert (file.parent/target).exists(),f'Broken link: {rel} -> {target}'
subprocess.run(['git','diff','--check'],cwd=root,check=True)
print('PASS library asset metadata, seven scene/thumbnail pairs, documentation links, diff whitespace')
