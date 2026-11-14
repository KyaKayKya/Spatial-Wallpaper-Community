import json,re,subprocess
from pathlib import Path
r=Path(__file__).resolve().parents[2]
settings=r/'Prototype/Assets/RainyCafe/Settings'
renderer_guid=re.search(r'guid: ([a-f0-9]+)',(settings/'RainyCafeForward.asset.meta').read_text()).group(1)
assert renderer_guid in (settings/'RainyCafeURP.asset').read_text()
assert 'm_CopyDepthMode: 0' in (settings/'RainyCafeForward.asset').read_text()
assert '12fb4c6168a6068438c5659902a12d6f' not in (r/'Prototype/Assets/RainyCafe/Prefabs/RainyCafeContent.prefab').read_text()
assert 'startLifetime:' not in (r/'Prototype/Assets/RainyCafe/Prefabs/RainyCafeContent.prefab').read_text(), 'Licensed particle settings must remain in local nested prefabs'
assert 'startLifetime:' not in (r/'Prototype/Assets/RainyCafe/Scenes/RainyCafe.unity').read_text(), 'Licensed particle settings must not be flattened into tracked scene'
clock=json.loads((r/'Validation/RainyCafe/particle-clock-check.json').read_text())
assert clock['all_seeds_fixed'] and clock['position_error_authored_m']<.00001 and clock['particles']>100
for name in ['check_report_encoding.py','inspect_candle_geometry.py','inspect_capture_times.py']:
 (r/'tools/rainycafe'/name).unlink(missing_ok=True)
for script in ['verify_capture.py','test_export.py']:
 subprocess.run(['python',str(r/'tools/rainycafe'/script)],check=True,cwd=r)
for folder in [r/'docs/reference/evidence/rainycafe',r/'docs/work/rainy-cafe']:
 for f in folder.glob('*.md'):
  for target in re.findall(r'\]\(([^)]+)\)',f.read_text(encoding='utf-8')):
   if not target.startswith('http'):assert (f.parent/target.split('#')[0]).exists(),(f,target)
files=[r/x for x in subprocess.check_output(['git','ls-files','--others','--exclude-standard'],cwd=r,text=True).splitlines()]
print('New files',len(files),'total MiB',round(sum(x.stat().st_size for x in files)/1048576,1))
for f in sorted(files,key=lambda x:x.stat().st_size,reverse=True)[:12]:print(f.relative_to(r),round(f.stat().st_size/1048576,2))
print('Detail images:',[x.name for x in (r/'Validation/RainyCafe/details').glob('*')])
