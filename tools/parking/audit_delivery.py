"""Check Parking delivery links, file sizes and local payload integrity boundaries."""
import re,subprocess
from pathlib import Path
root=Path(__file__).resolve().parents[2]
for name in ['docs/guides/Parking场景运行.md','docs/guides/场景固定窗口规范.md','docs/reference/evidence/parking/验证说明.md','docs/reference/evidence/parking/资源来源.md']:
    p=root/name
    for target in re.findall(r'\]\(([^)]+)\)',p.read_text(encoding='utf-8')):
        if '://' not in target:
            assert (p.parent/target.split('#')[0]).exists(),(name,target)
files=subprocess.check_output(['git','ls-files','--others','--exclude-standard','-z'],cwd=root).decode().split('\0')
sizes=sorted(((root/f).stat().st_size,f) for f in files if f)
assert all(s<50*1024*1024 for s,f in sizes),sizes[-10:]
print('Largest untracked delivery files:',sizes[-8:])
print('PASS: Parking documentation links and per-file size boundaries')
