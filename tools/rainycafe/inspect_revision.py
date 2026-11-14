import json
from pathlib import Path
r=Path(__file__).resolve().parents[2]
data=json.loads((r/'Validation/RainyCafe/source-inventory.json').read_text())
for o in data['objects']:
 n=o['name'].lower()
 b=o.get('bounds')
 if b and not o['hidden'] and b[0][0]<-2.38 and b[1][0]>-2.68 and b[0][2]>1.95 and b[0][2]<2.54 and b[0][1]<3.4 and b[1][1]>0:print(json.dumps(o))
