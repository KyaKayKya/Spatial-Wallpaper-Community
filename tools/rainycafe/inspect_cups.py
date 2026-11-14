import json
from pathlib import Path
d=json.loads((Path(__file__).resolve().parents[2]/'Validation/RainyCafe/source-inventory.json').read_text())
for o in d['objects']:
    if o['type']!='MESH':continue
    b=o['bounds'];x=(b[0][0]+b[1][0])/2;y=(b[0][1]+b[1][1])/2
    if b[0][2]>.75 and b[1][2]<1.05 and (abs(x-1.96)<.2 and abs(y-.85)<.2 or abs(x-1.98)<.2 and abs(y-2.7)<.2 or abs(x-1.91)<.1 and abs(y-3)<.1): print(o)
