import json
from pathlib import Path
d=json.loads((Path(__file__).resolve().parents[2]/'Validation/RainyCafe/source-inventory.json').read_text())
for o in d['objects']:
    if o['name'].startswith(('V2 rear','V2 window','V2 separate','V2 slender window')) and o['type']=='MESH':print(o)
