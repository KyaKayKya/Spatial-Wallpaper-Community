import csv,json,math,statistics
from pathlib import Path
ROOT=Path(__file__).resolve().parents[2]
OUT=ROOT/'Validation/Showcase/unity'
def pct(values,p):
 a=sorted(values);i=(len(a)-1)*p;lo=int(i);hi=min(lo+1,len(a)-1);return a[lo]+(a[hi]-a[lo])*(i-lo)
result={}
for mode in ['adaptive','always']:
 folder=OUT/('bench-'+mode)
 if not (folder/'frames.csv').exists():continue
 rows=list(csv.DictReader((folder/'frames.csv').open()))
 summary={}
 for phase,start,end in [('stationary',12,70),('moving',72,130)]:
  rr=[r for r in rows if start<=float(r['seconds'])<end]
  seen=set();tt=[]
  for r in rr:
   if r['new_timing']=='1' and r['frame_timestamp'] not in seen:
    seen.add(r['frame_timestamp']);tt.append(r)
  stats={}
  for field in ['cpu_frame_ms','cpu_main_ms','present_wait_ms','cpu_render_ms','gpu_ms']:
   a=[float(r[field]) for r in tt if math.isfinite(float(r[field])) and float(r[field])>=0]
   stats[field]={'median':statistics.median(a),'p95':pct(a,.95)} if a else None
  for field in ['working_set_bytes','private_bytes','unity_allocated_bytes','unity_reserved_bytes','managed_bytes','process_cpu_percent']:
   a=[float(r[field]) for r in rr if math.isfinite(float(r[field])) and float(r[field])>=0 and (not field.endswith('bytes') or float(r[field])>0)]
   stats[field]={'median':statistics.median(a),'p95':pct(a,.95)} if a else None
  stats['render_hz']=(int(rr[-1]['rendered_count'])-int(rr[0]['rendered_count']))/(float(rr[-1]['seconds'])-float(rr[0]['seconds'])) if len(rr)>1 else None
  stats['unique_timing_samples']=len(tt);stats['seconds_covered']=float(rr[-1]['seconds'])-float(rr[0]['seconds']) if rr else 0
  summary[phase]=stats
 result[mode]=summary
(OUT/'performance.json').write_text(json.dumps(result,indent=2))
print(json.dumps(result,indent=2))
