import json
from pathlib import Path
root=Path(__file__).resolve().parents[2]
d=json.loads((root/'Prototype/Assets/RainyCafe/Generated/manifest.json').read_text())
for item in d['objects']:print(item)
