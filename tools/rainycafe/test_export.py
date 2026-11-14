"""Check actual source-to-Unity export evidence, not nominal room dimensions."""
import hashlib
import json
from pathlib import Path
root=Path(__file__).resolve().parents[2]
folder=root/'Prototype/Assets/RainyCafe/Generated'
assert (folder/'manifest.json').exists(), 'RainyCafe export is missing'
data=json.loads((folder/'manifest.json').read_text())
assert data['source_sha256']=='c36e16f2dec75691d54277fd8b32b48f67b7e7fbf6246df1985e378b72faa2cb'
assert data['volume'][:2]==[5.4,3.0375]
assert data['candle_baked'] is False, 'Moving candle shadows must not be baked'
assert data['geometry_checks']['misses']==0, data['geometry_checks']
assert data['geometry_checks']['eye_count']>=27
assert len(data['candles'])==3
assert data['candles'][2][0]<-2 and data['candles'][2][2]>2.52, 'Upper shelf candle anchor missing'
assert len(data['cups'])>=2
assert len(data['objects'])>=5
for name,expected in data['sha256'].items():
    assert hashlib.sha256((folder/name).read_bytes()).hexdigest()==expected,name
assert max(o['bounds'][1][2] for o in data['objects'])>20, 'Source street depth was flattened'
print('PASS: source hash, uniform window units, actual closure rays, unbaked candles, depth and file integrity')
