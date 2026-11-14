from pathlib import Path
import json
r=Path(__file__).resolve().parents[2]
d=json.loads((r/'Prototype/Assets/Limpool/Generated/architecture.json').read_text())
for o in d['items']:
 if not o['rail']:
  b=[[min(v[a] for v in o['vertices']) for a in 'xyz'],[max(v[a] for v in o['vertices']) for a in 'xyz']]
  print(o['name'],b)
items={o['name']:o for o in d['items']}
sill=items['Rear_Left_Window_Sill'];wall=items['Wall_Left__inner_x_minus4']
overlap=max(v['x'] for v in wall['vertices'])-min(v['x'] for v in sill['vertices'])
result={'sill_left_source_m':min(v['x'] for v in sill['vertices']),'wall_inner_source_m':max(v['x'] for v in wall['vertices']),'overlap_source_m':overlap,'pass':overlap>=.019}
(r/'Validation/Limpool/joint-check.json').write_text(json.dumps(result,indent=2))
assert result['pass'],result
