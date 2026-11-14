import json
from pathlib import Path
root=Path(__file__).resolve().parents[2]
d=json.loads((root/'Validation/RainyCafe/source-inventory.json').read_text())
for o in d['objects']:
    if o['name'].startswith(('Individual window','Window coalesced','coffee_grinder | GRANO')): continue
    if o['type'] in ('LIGHT','CAMERA') or any(x in o['name'].lower() for x in ('candle','flame','steam','street','building','rain','coffee setting','latte setting','glaz')): print(o)
print('Mesh vertices:',sum(o.get('vertices',0) for o in d['objects']))
print('ARCHITECTURE')
for o in d['objects']:
    if o['name'].startswith('V2') and o['type']=='MESH':print(o)
