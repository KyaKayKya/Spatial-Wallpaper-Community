from pathlib import Path
from urllib.request import urlopen
import hashlib
root = Path(__file__).resolve().parents[2] / 'Prototype/Assets/Calibration/Fonts'
root.mkdir(parents=True, exist_ok=True)
base = 'https://raw.githubusercontent.com/adobe-fonts/source-han-sans/release/'
expected = {'SourceHanSansSC-Regular.otf': 'f1d8611151880c6c336aabeac4640ef434fa13cbfbf1ffe82d0a71b2a5637256', 'LICENSE.txt': 'fcac737e761ec63dbfbdce11030a1780161920d80315edba9c8beff1c2bac5a2'}
for name, source in [('SourceHanSansSC-Regular.otf', 'OTF/SimplifiedChinese/SourceHanSansSC-Regular.otf'), ('LICENSE.txt', 'LICENSE.txt')]:
    target = root / name
    if not target.exists():
        with urlopen(base + source, timeout=60) as response:
            target.write_bytes(response.read())
    digest = hashlib.sha256(target.read_bytes()).hexdigest()
    if digest != expected[name]:
        raise SystemExit('Font source changed; review license and update the pinned hash: ' + name)
    print(name, target.stat().st_size, digest)
