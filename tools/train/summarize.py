"""Summarize measured Player timings, with warmup/phase-transition exclusion."""
import csv,json,math,statistics
from pathlib import Path
OUT=Path(__file__).resolve().parents[2]/'Validation/Train'
def stats(a):
    a=sorted(v for v in a if math.isfinite(v) and v>=0)
    if not a:return None
    i=(len(a)-1)*.95;lo=int(i);hi=min(lo+1,len(a)-1)
    return {'median':statistics.median(a),'p95':a[lo]+(a[hi]-a[lo])*(i-lo)}
result={}
for mode in ['bench-dynamic','bench-static']:
    folder=OUT/mode;rows=list(csv.DictReader((folder/'frames.csv').open()))
    assert max(float(r['seconds']) for r in rows)>129,'Incomplete benchmark: '+mode
    phases={}
    for phase,start,end in [('stationary',12,70),('moving',72,130)]:
        rr=[r for r in rows if start<=float(r['seconds'])<end];seen=set();tt=[]
        for r in rr:
            if r['new_timing']=='1' and r['frame_timestamp'] not in seen:seen.add(r['frame_timestamp']);tt.append(r)
        s={k:stats([float(r[k]) for r in tt]) for k in ['cpu_frame_ms','cpu_main_ms','present_wait_ms','cpu_render_ms','gpu_ms']}
        for k in ['working_set_bytes','private_bytes','unity_allocated_bytes','managed_bytes','process_cpu_percent']:
            s[k]=stats([float(r[k]) for r in rr if not k.endswith('bytes') or float(r[k])>0])
        duration=float(rr[-1]['seconds'])-float(rr[0]['seconds'])
        s.update(render_hz=(int(rr[-1]['rendered_count'])-int(rr[0]['rendered_count']))/duration,unique_timing_samples=len(tt),seconds_covered=duration,render_intervals=sorted(set(int(r['render_interval']) for r in rr)))
        phases[phase]=s
    result[mode]={'hardware':(folder/'hardware.txt').read_text(),'phases':phases}
(OUT/'performance.json').write_text(json.dumps(result,indent=2))
for mode,info in result.items():
    for phase,s in info['phases'].items():print(mode,phase,'Hz',round(s['render_hz'],2),'GPU',s['gpu_ms'],'CPU main',s['cpu_main_ms'],'working MB',s['working_set_bytes']['median']/1048576)
