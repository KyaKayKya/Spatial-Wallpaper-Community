"""Summarize real Player timings after warmup, with timestamp deduplication."""
import csv,json,math,statistics
from pathlib import Path
out=Path(__file__).resolve().parents[2]/'Validation/RainyCafe'
def stats(values):
    v=sorted(x for x in values if math.isfinite(x) and x>=0)
    if not v:return None
    rank=(len(v)-1)*.95;low=int(rank)
    return dict(median=statistics.median(v),p95=v[low]+(v[min(low+1,len(v)-1)]-v[low])*(rank-low))
result={}
for mode in ('bench-full','bench-static','bench-no-shadows'):
    folder=out/mode
    with (folder/'frames.csv').open() as stream:rows=list(csv.DictReader(stream))
    assert max(float(r['seconds']) for r in rows)>129,'Incomplete run: '+mode
    phases={}
    for phase,start,end in [('stationary',12,70),('moving',72,130)]:
        selected=[r for r in rows if start<=float(r['seconds'])<end]
        seen=set();timings=[]
        for r in selected:
            if r['new_timing']=='1' and r['frame_timestamp'] not in seen:
                seen.add(r['frame_timestamp']);timings.append(r)
        values={k:stats([float(r[k]) for r in timings]) for k in ('cpu_frame_ms','cpu_main_ms','present_wait_ms','cpu_render_ms','gpu_ms')}
        assert len(timings)>=300, 'Insufficient unique frame timings: '+mode+' '+phase
        assert values['gpu_ms'] and values['gpu_ms']['median']>0, 'GPU timing unavailable: '+mode+' '+phase
        for k in ('working_set_bytes','private_bytes','unity_allocated_bytes','managed_bytes','process_cpu_percent'):
            values[k]=stats([float(r[k]) for r in selected])
        duration=float(selected[-1]['seconds'])-float(selected[0]['seconds'])
        values.update(render_hz=(int(selected[-1]['rendered_count'])-int(selected[0]['rendered_count']))/duration,unique_samples=len(timings),seconds=duration,render_intervals=sorted(set(int(r['render_interval']) for r in selected)))
        assert values['render_intervals']==[1], 'Animation cadence failed: '+mode+' '+phase
        phases[phase]=values
    gpu_rows=[]
    with (folder/'gpu-state.csv').open() as stream:
        for row in csv.DictReader(stream):
            clean={k.strip():v.strip() for k,v in row.items() if k and v}
            if clean.get('name') and clean['name']!='name':gpu_rows.append(clean)
    gpu={}
    for name in sorted({r['name'] for r in gpu_rows}):
        # One sample per second per GPU; discard startup/10 s warmup at both ends.
        selected=[r for r in gpu_rows if r['name']==name][12:-2]
        assert selected, 'GPU monitor did not produce enough samples'
        values={}
        for field in ('power.draw [W]','clocks.current.graphics [MHz]','clocks.current.memory [MHz]','utilization.gpu [%]','memory.used [MiB]'):
            measured=[]
            for row in selected:
                raw=row.get(field,'').split(' ')[0]
                try:measured.append(float(raw))
                except ValueError:continue
            values[field]=stats(measured)
        values['pstates']=sorted({r.get('pstate','unknown') for r in selected});gpu[name]=values
    result[mode]={'hardware':(folder/'hardware.txt').read_text(),'quality':json.loads((folder/'quality.json').read_text()),'gpu_state':gpu,'phases':phases}
(out/'performance.json').write_text(json.dumps(result,indent=2))
for mode,info in result.items():
    for phase,s in info['phases'].items():
        print(mode,phase,'Hz',round(s['render_hz'],2),'CPU',s['cpu_frame_ms'],'GPU',s['gpu_ms'],'RAM MiB',round(s['working_set_bytes']['median']/1048576,1))
