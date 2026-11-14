"""Verify provenance rejection against copies of actual converted storage authors."""
from pathlib import Path
import argparse,copy,json,shutil,subprocess,sys,uuid,traceback
import run_storage_matrix
from run_storage_matrix import ROOT,sources,validate_authors,validate_worker,digest,validate_results,validate_dependencies,REQUIRED_CASES
run_context={}
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--authors',type=Path,required=True);parser.add_argument('--worker',type=Path,required=True);parser.add_argument('--negative-subset-only',action='store_true');args=parser.parse_args()
 source=args.authors.resolve();receipt=sources();validate_authors(source,receipt);validate_worker(args.worker.resolve(),receipt)
 out=ROOT/'Validation/Content/sdk-runtime/E/storage-matrix-evidence-negative'/uuid.uuid4().hex;out.mkdir(parents=True);results=[]
 print(out,flush=True)
 run_context.update(output=out,sources=receipt,cases=results,negative_subset_only=args.negative_subset_only)
 if args.negative_subset_only:
  def subset_only(recorded,current,author=False):
   run_storage_matrix.need(all(recorded.get(k)==v for k,v in run_storage_matrix.dependencies(current,author).items()),('author' if author else 'worker')+' source dependency drift')
  run_storage_matrix.validate_dependencies=subset_only
 cases=[('missing-role','author roles'),('extra-role','author roles'),('actual-metadata','author metadata drift'),('wrong-role','author metadata drift'),('package-bytes','author package drift'),('build-id','author build ID drift'),('duplicate-build','author builds must differ'),('author-source','author source dependency drift'),('compiler-source','author source dependency drift')]
 cases.append(('removed-author-dependency','author source dependency drift'))
 for name,expected in cases:
  target=out/name;shutil.copytree(source,target);report=json.loads((target/'report.json').read_text());metadata=json.loads((target/'1/author.json').read_text(encoding='utf-8-sig'))
  if name=='missing-role':del report['authors']['3']
  elif name=='extra-role':report['authors']['4']=copy.deepcopy(report['authors']['3'])
  elif name=='actual-metadata':metadata['build']='0'*64
  elif name=='wrong-role':metadata['role']=2;report['authors']['1']['metadata']=metadata
  elif name=='package-bytes':
   with (target/'1/scene.spatial').open('ab') as file:file.write(b'changed')
  elif name=='build-id':metadata['build']='0'*64;report['authors']['1']['metadata']=metadata
  elif name=='duplicate-build':
   shutil.copyfile(target/'2/scene.spatial',target/'1/scene.spatial');metadata['build']=report['authors']['2']['metadata']['build'];report['authors']['1']['metadata']=metadata;report['authors']['1']['package_sha256']=digest(target/'1/scene.spatial')
  elif name=='author-source':report['sources']['tools/sdk/experiments/StageEStorageMatrixAuthor.cs']='0'*64
  elif name=='compiler-source':report['sources']['Packages/com.spatialwallpaper.sdk/Editor/CilboxProgramCompiler.cs']='0'*64
  elif name=='removed-author-dependency':report['sources']['Packages/com.spatialwallpaper.sdk/Runtime/RemovedImplementation.cs']='0'*64
  (target/'1/author.json').write_text(json.dumps(metadata));(target/'report.json').write_text(json.dumps(report))
  try:validate_authors(target,receipt)
  except ValueError as error:
   if expected not in str(error):raise
   results.append({'case':name,'rejected':str(error)})
  else:raise AssertionError('tampered evidence accepted: '+name)
 complete=[{'case':case} for case in sorted(REQUIRED_CASES)];validate_results(complete)
 worker_copy=out/'removed-worker-dependency';shutil.copytree(args.worker.resolve(),worker_copy)
 previous=json.loads((worker_copy/'report.json').read_text())
 previous['sources']['Packages/com.spatialwallpaper.sdk/Runtime/RemovedImplementation.cs']='0'*64
 (worker_copy/'report.json').write_text(json.dumps(previous))
 try:validate_worker(worker_copy,receipt)
 except ValueError as error:results.append({'case':'removed-worker-dependency','rejected':str(error)})
 else:raise AssertionError('removed Worker dependency accepted')
 outside=copy.deepcopy(receipt);outside['Supervisor/Tests/UnrelatedFixture.cs']='0'*64
 validate_dependencies(outside,receipt);validate_dependencies(outside,receipt,True)
 for name,candidate in [('missing-result',complete[:-1]),('duplicate-result',complete+[complete[0]]),('empty-results',[])]:
  try:validate_results(candidate)
  except ValueError as error:results.append({'case':name,'rejected':str(error)})
  else:raise AssertionError('incomplete results accepted: '+name)
 # Exercise the public entry point too: rejected input must leave a failure report.
 result=subprocess.run([sys.executable,str(ROOT/'Supervisor/Tests/run_storage_matrix.py'),'--worker',str(args.worker.resolve()),'--authors',str(out/'package-bytes')],capture_output=True,text=True,timeout=60)
 (out/'entrypoint.log').write_text(result.stdout+result.stderr,encoding='utf-8');assert result.returncode==1
 failure_path=Path(result.stdout.strip().splitlines()[-1]);failure=json.loads((failure_path/'report.json').read_text())
 assert failure['status']=='failed' and failure['stage']=='input-verification' and 'author package drift' in failure['error']
 (out/'report.json').write_text(json.dumps({'status':'passed','sources':receipt,'cases':results,'entrypoint_failure_report':str(failure_path/'report.json')},indent=2));print(out)
if __name__=='__main__':
 try:main()
 except Exception:
  if run_context:
   output=run_context.pop('output');error=traceback.format_exc();(output/'failure.log').write_text(error,encoding='utf-8')
   (output/'report.json').write_text(json.dumps(dict(run_context,status='failed',error=error),indent=2))
  raise
