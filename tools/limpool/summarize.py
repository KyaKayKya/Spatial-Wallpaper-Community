from pathlib import Path
import csv,json,statistics,math
r=Path(__file__).resolve().parents[2]/'Validation/Limpool'
results=[]
def stats(values):
    v=sorted(float(x) for x in values if math.isfinite(float(x)))
    return {'median':statistics.median(v),'p95':v[min(len(v)-1,math.ceil(len(v)*.95)-1)]} if v else None
for path in sorted(r.glob('bench-*/frames.csv')):
    rows=list(csv.DictReader(path.open()))
    for phase,label in [('1','fixed'),('2','moving')]:
        samples=[x for x in rows if x['phase']==phase and (12<float(x['seconds'])<68 if phase=='1' else 72<float(x['seconds'])<128)]
        seen=set();timings=[]
        for x in samples:
            if x['new_timing']=='1' and x['frame_timestamp']!='0' and x['frame_timestamp'] not in seen:timings.append(x);seen.add(x['frame_timestamp'])
        if not timings:raise RuntimeError('No timing samples: '+str(path))
        item={'quality':path.parent.name[6:],'phase':label,'samples':len(timings),'cpu_ms':stats(x['cpu_frame_ms'] for x in timings),'cpu_main_ms':stats(x['cpu_main_ms'] for x in timings),'present_wait_ms':stats(x['present_wait_ms'] for x in timings),'gpu_ms':stats(x['gpu_ms'] for x in timings),'working_set_mib':stats(float(x['working_set_bytes'])/1048576 for x in samples),'private_mib':stats(float(x['private_bytes'])/1048576 for x in samples),'fps':(float(samples[-1]['rendered_count'])-float(samples[0]['rendered_count']))/(float(samples[-1]['seconds'])-float(samples[0]['seconds']))}
        results.append(item)
(r/'performance.json').write_text(json.dumps(results,indent=2))
print(json.dumps(results,indent=2))
