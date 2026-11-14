"""Validate the real Player's attention transitions and physical window invariants."""
import csv,json
from pathlib import Path

root=Path(__file__).resolve().parents[2]
capture=root/'Validation/Mita/capture'
with (capture/'gaze.csv').open(encoding='utf-8-sig') as stream:rows=list(csv.DictReader(stream))
def section(start,end):return [r for r in rows if start<float(r['time'])<end]
def only(start,end,source):
    part=section(start,end)
    assert part and all(r['source']==source for r in part),(start,end,source,{r['source'] for r in part})
only(6.6,7.3,'Native')
assert any(r['source']=='User' for r in section(7.4,8.25))
only(9,9.45,'Mouse')
only(9.6,13.45,'Click')
only(13.6,13.95,'Native')
assert any(r['source']=='User' for r in section(14.4,14.95))
only(15.1,15.9,'Native')
assert all(r['source']=='Native' and float(r['eyeWeight'])==0 and float(r['headWeight'])==0 for r in rows if r['busy']=='True')
only(22,22.9,'Native')
only(23.1,26.9,'Click')
for r in section(23.1,26.9):
    assert max(abs(float(r[k])-v) for k,v in [('targetX',0),('targetY',0),('targetZ',-.6)])<.0001,'Click without fresh tracking must use the reference eye'
for r in rows:
    if r['source']=='User' or (r['source']=='Click' and r['fresh']=='True'):
        assert max(abs(float(r['target'+axis])-float(r['view'+axis])) for axis in 'XYZ')<.0001
    if r['source']=='Mouse':assert abs(float(r['targetZ']))<.0001,'Mouse ray must hit physical screen plane'
assert max(float(r['cameraAngle']) for r in rows)<.00001
assert max(float(r['cornerError']) for r in rows)<.05
tracked=[r for r in rows if float(r['eyeWeight'])>.999 and r['source']!='Native']
assert tracked and max(float(r['eyeError']) for r in tracked)<2
assert max(float(r['eyeWeight']) for r in section(15.6,15.9))==0,'Lost tracking must fade to native within 0.5 seconds'
assert json.loads((capture/'gaze-pause.json').read_text())['frozen']
assert max(float(r['activationStep']) for r in rows)<10,'New attention must start from current native eye direction, not an old target'
switches=[]
for r in rows:
    if not switches or r['source']!=switches[-1]['source']:switches.append({'time':float(r['time']),'source':r['source']})
print('PASS: real Player attention sequence, loss/recovery, original action priority, reference fallback, pause and aperture')
print(json.dumps({'frames':len(rows),'max_eye_error':max(float(r['eyeError']) for r in tracked),'max_activation_step':max(float(r['activationStep']) for r in rows),'switches':switches},indent=2))
