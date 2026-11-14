"""Run the frozen Worker performance sampling matrix for one installed candidate.

Unavailable frame metrics remain unmeasured. This runner cannot certify RC.
"""
from pathlib import Path
import argparse,hashlib,itertools,json,sys,time,uuid
from provenance import ROOT,sources
from case_process import CaseProcess
from verify_delivery import verify_inventory,digest

def cases(policy):
 p=policy['performance']
 return [{'width':size[0],'height':size[1],'fps':fps,'repetition':repeat,
          'warmup_seconds':p['warmup_seconds'],'sample_seconds':p['sample_seconds']}
         for size,fps,repeat in itertools.product(p['resolutions'],p['target_fps'],range(1,p['repetitions']+1))]

def require_sampling_owner(code,tree):
 from input_cycles import owner_proof
 if code!=0:raise ValueError('performance-case-failed')
 try:owner_proof({'status':'passed','process_tree':tree})
 except ValueError as error:raise ValueError('performance-owner-unproven:'+str(error)) from error

def main():
 p=argparse.ArgumentParser(description=__doc__);p.add_argument('--delivery',type=Path,required=True);p.add_argument('--list',action='store_true');args=p.parse_args()
 policy_path=ROOT/'tools/sdk/release-policy.json';policy=json.loads(policy_path.read_text());matrix=cases(policy)
 if args.list:print(json.dumps(matrix,indent=2));return 0
 delivery=args.delivery.resolve();manifest=json.loads((delivery/'assembly-report.json').read_text());verify_inventory(delivery,manifest['files'],exclude=['assembly-report.json'])
 out=ROOT/'Validation/Content/sdk-runtime/G/performance'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
 runtime=delivery/'runtime';receipt=sources();report={'status':'failed','sources':receipt,'delivery':str(delivery),'delivery_manifest_sha256':digest(delivery/'assembly-report.json'),'policy_sha256':digest(policy_path),'policy':policy,'matrix':matrix,'steps':[],'scope':__doc__,'started_at_unix':time.time()}
 def save():(out/'report.json').write_text(json.dumps(report,indent=2))
 save()
 try:
  for index,case in enumerate(matrix):
   before=set((ROOT/'Validation/Content/sdk-runtime/G/frame-timing').glob('*/report.json'));began=time.time()
   argv=['--worker',str(runtime/'worker'),'--supervisor',str(runtime/'supervisor'),'--validator',str(runtime/'validator'),'--rc-performance',*[v for key in ['width','height','fps','repetition'] for v in ['--'+key,str(case[key])]]]
   row={'case':case,'status':'failed','command':[sys.executable,str(ROOT/'tools/sdk/check_frame_timing.py'),*argv]};report['steps'].append(row)
   with (out/(str(index)+'.log')).open('w') as log:
    owner=CaseProcess()
    try:
     owner.start(ROOT/'tools/sdk/check_frame_timing.py',argv,ROOT,log);code=owner.wait(case['warmup_seconds']+case['sample_seconds']+150)
    finally:owner.close();row['process_tree']=owner.evidence.copy()
   require_sampling_owner(code,row['process_tree'])
   fresh=set((ROOT/'Validation/Content/sdk-runtime/G/frame-timing').glob('*/report.json'))-before
   if len(fresh)!=1:raise ValueError('performance-fresh-report-count')
   path=fresh.pop();value=json.loads(path.read_text())
   if path.stat().st_mtime<began or value['sources']!=receipt or value.get('sources_unchanged') is not True or value['status']!='completed-RC-performance-sampling-results-unmeasured':raise ValueError('performance-report-stale-or-incomplete')
   if any(value[key]!=case[key] for key in ['width','height','repetition','warmup_seconds','sample_seconds']) or value['target_fps']!=case['fps'] or not value['rc_performance_sampling']:raise ValueError('performance-case-parameters')
   row.update(status='completed-sampling-performance-unmeasured',artifact=str(path),artifact_sha256=digest(path),grade=None);save()
   if sources()!=receipt:raise ValueError('performance-source-drift')
  if len(report['steps'])!=len(matrix):raise ValueError('performance-matrix-incomplete')
  verify_inventory(delivery,manifest['files'],exclude=['assembly-report.json'])
  report['status']='completed-frozen-performance-matrix-results-unmeasured'
 except BaseException as error:report['error']=repr(error)
 finally:
  report['source_unchanged']=sources()==receipt;report['finished_at_unix']=time.time()
  if not report['source_unchanged']:report['status']='failed'
  save();print(out,flush=True)
 return 0 if report['status']=='completed-frozen-performance-matrix-results-unmeasured' else 1

if __name__=='__main__':raise SystemExit(main())
