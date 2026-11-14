"""Reject error-shader frames and missing content in actual Player captures."""
from pathlib import Path
import json
import sys
from PIL import Image

folder = Path(sys.argv[1])
files = [folder / 'player.png'] if (folder / 'player.png').exists() else list(folder.glob('*.png'))
if not files:
    raise SystemExit('FAIL: no Player images')
results = []
for path in files:
    im = Image.open(path).convert('RGB').resize((160, 90))
    pixels = list(im.getdata())
    magenta = sum(r > 150 and b > 150 and g < min(r, b) * .35 for r, g, b in pixels) / len(pixels)
    dark = sum(max(p) < 12 for p in pixels) / len(pixels)
    distinct = len(set(pixels))
    ok = magenta < .005 and dark < .8 and distinct > 500
    results.append(dict(file=path.name, passed=ok, magenta_fraction=magenta, dark_fraction=dark, distinct_colors=distinct))
(folder / 'image-checks.json').write_text(json.dumps(results, indent=2))
print(json.dumps(results, indent=2))
sys.exit(0 if all(r['passed'] for r in results) else 1)
