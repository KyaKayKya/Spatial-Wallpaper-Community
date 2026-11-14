"""Keep one compact synthetic Player verification set; never consumes camera frames."""
from pathlib import Path
import hashlib
import shutil
import sys
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parents[2]
source = (root / 'Validation/Calibration' / sys.argv[1]).resolve()
if source.parent != (root / 'Validation/Calibration').resolve() or not source.name.startswith('smoke-'):
    raise SystemExit('Expected a local smoke directory name')
target = root / 'Validation/Calibration/verified'
target.mkdir(parents=True, exist_ok=True)
for name in ['calibration.png', 'calibration-wide.png', 'showcase.png', 'result.txt']:
    shutil.copy2(source / name, target / name)
names = ['front', 'left', 'right', 'up', 'down', 'near', 'far', 'level-min', 'level-max']
sheet = Image.new('RGB', (1440, 882), '#edf1f5')
draw = ImageDraw.Draw(sheet)
for i, name in enumerate(names):
    image = Image.open(source / (name + '.png')).convert('RGB')
    image.thumbnail((480, 270))
    x, y = (i % 3) * 480, (i // 3) * 294
    sheet.paste(image, (x, y + 24))
    draw.text((x + 8, y + 5), name, fill='#1b2533')
sheet.save(target / 'geometry-views.jpg', quality=90)
build = root / 'Prototype/Builds/Calibration'
data = build / 'CalibrationDemo_Data'
# Unity's exe is a stable bootstrap; code, scenes and texture streams identify the actual build.
candidates = [build / 'CalibrationDemo.exe', data / 'Managed/Assembly-CSharp.dll',
              *data.glob('level*'), *data.glob('sharedassets*')]
records = []
for path in sorted(set(candidates)):
    if not path.is_file():
        continue
    digest = hashlib.sha256()
    with path.open('rb') as stream:
        for block in iter(lambda: stream.read(1024 * 1024), b''):
            digest.update(block)
    records.append(digest.hexdigest() + '  ' + path.relative_to(build).as_posix())
(target / 'build-sha256.txt').write_text('\n'.join(records) + '\n', encoding='utf-8')
print(target)
