"""Reject missing frames, nonfinite corner errors and failed imported-geometry checks."""
import csv,math
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]/'Validation/Train'
for name,count in [('stills',13),('scaled-stills',13),('video',720),('hold',720)]:
 folder=ROOT/name
 rows=list(csv.DictReader((folder/'capture.csv').open()))
 assert len(rows)==count,(name,len(rows))
 errors=[float(r['corner_error_px']) for r in rows]
 assert all(math.isfinite(e) and e<.01 for e in errors),(name,max(errors))
 assert 'pass=True' in (folder/'geometry.txt').read_text()
 assert 'interiorTransformChanges=0;' in (folder/'geometry.txt').read_text()
 log=(folder/'player.log').read_text(errors='replace')
 assert 'shader is not supported' not in log and 'NullReferenceException' not in log,(name,'Player rendering error')
 assert len(list(folder.glob('*.png')))==count
 print(name,'PASS',count,'frames','max corner error',max(errors))
