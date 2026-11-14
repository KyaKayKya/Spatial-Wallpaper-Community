"""Validate standalone evidence, motion independence, and actual animation changes."""
import csv,re
from pathlib import Path
out=Path(__file__).resolve().parents[2]/'Validation/RainyCafe'
for mode,count in [('stills',28),('stills-wide',28),('stills-tall',28),('video',900),('hold',1350)]:
    folder=out/mode
    report=(folder/'geometry.txt').read_text()
    assert 'pass=True' in report,report
    rows=list(csv.DictReader((folder/'capture.csv').open()))
    assert len(rows)==count,(mode,len(rows))
    assert len(list(folder.glob('*.png')))==count
    assert max(float(r['corner_error_px']) for r in rows)<.01
    if mode=='hold':
        assert len({(r['eye_x'],r['eye_y'],r['eye_z']) for r in rows})==1
        assert max(float(r['candle0_intensity']) for r in rows)-min(float(r['candle0_intensity']) for r in rows)>0.00002
        assert len({(r['candle0_x'],r['candle0_y'],r['candle0_z']) for r in rows})>100
    if mode=='video':
        for field in ('eye_x','eye_y','eye_z'):
            assert max(float(r[field]) for r in rows)-min(float(r[field]) for r in rows)>.07,field
    print('PASS:',mode,count,'actual Player frames, fixed window and stable interior')
