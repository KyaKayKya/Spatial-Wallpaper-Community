"""Real Worker storage matrix. Reports core uninstall semantics, never product UI coverage."""
from pathlib import Path
import argparse,sys,subprocess,uuid,shutil,json,hashlib,platform,zipfile,traceback
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from provenance import sources
from windows_framework import references,identity
def digest(path):return hashlib.sha256(path.read_bytes()).hexdigest()
def need(value,message):
 if not value:raise ValueError(message)
REQUIRED_CASES={'two-live-LPAC-workers-CAS','cross-build-after-broker-restart','other-installation-empty','preview-empty','same-id-reinstalled-empty','preview-cleared','new-id-reinstalled-empty','unrelated-installation-nonempty-preserved','read-Io-rejected-after-storage-recovered','log-write-failure-still-cleans-worker','result-marker-conflict-retries-without-retrying-winner'}
def validate_results(results):
 need(isinstance(results,list),'matrix results must be a list')
 cases=[entry.get('case') for entry in results]
 need(len(cases)==len(REQUIRED_CASES) and set(cases)==REQUIRED_CASES,'matrix results missing or duplicate required cases')
def dependencies(receipt,author=False):
 paths={'tools/sdk/build_execution.py','tools/sdk/build_storage_matrix.py','tools/sdk/experiments/StageEStorageMatrixAuthor.cs','tools/sdk/experiments/StageEStorageMatrixExport.cs'} if author else {'tools/sdk/build_worker.py','tools/sdk/build_execution.py','tools/sdk/experiments/SdkWorkerBuild.cs','tools/sdk/experiments/StageCPipeline.cs'}
 prefix='Packages/com.spatialwallpaper.sdk/' if author else 'Packages/com.spatialwallpaper.sdk/Runtime/'
 return {k:v for k,v in receipt.items() if k.startswith(prefix) or k in paths}
def validate_dependencies(recorded,current,author=False):
 need(dependencies(recorded,author)==dependencies(current,author),('author' if author else 'worker')+' source dependency drift')
def validate_authors(authors,receipt):
 report=json.loads((authors/'report.json').read_text());entries=report['authors']
 need(set(entries)=={'1','2','3'},'author roles must be exactly 1/2/3')
 validate_dependencies(report.get('sources',{}),receipt,True)
 builds=[]
 for role,entry in entries.items():
  metadata=json.loads((authors/role/'author.json').read_text(encoding='utf-8-sig'))
  need(metadata==entry['metadata'] and metadata['role']==int(role),'author metadata drift')
  package=authors/role/'scene.spatial';need(digest(package)==entry['package_sha256'],'author package drift')
  with zipfile.ZipFile(package) as archive:manifest=json.loads(archive.read('manifest.json'))
  build=manifest.pop('build_id');canonical=json.dumps(manifest,sort_keys=True,separators=(',',':'),ensure_ascii=False).encode()
  need(build==hashlib.sha256(canonical).hexdigest() and metadata['build']==build,'author build ID drift');builds.append(build)
 need(len(set(builds))==3,'author builds must differ')
 return report
def validate_worker(worker,receipt):
 report=json.loads((worker/'report.json').read_text())
 need({p.relative_to(worker/'player').as_posix():digest(p) for p in (worker/'player').rglob('*') if p.is_file()}==report['runtime_files'],'worker inventory drift')
 validate_dependencies(report.get('sources',{}),receipt)
 return report
def main():
 parser=argparse.ArgumentParser();parser.add_argument('--worker',type=Path,required=True);parser.add_argument('--authors',type=Path,required=True);args=parser.parse_args()
 out=ROOT/'Validation/Content/sdk-runtime/E/storage-matrix'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();worker=args.worker.resolve();authors=args.authors.resolve()
 report={'status':'failed','scope':'E storage real Worker matrix and trusted core uninstall only; F uninstall UI absent/unverified','sources':receipt,'platform':platform.platform(),'worker':str(worker),'authors_path':str(authors),'stage':'input-verification'}
 def run(command,name,timeout):
  try:
   result=subprocess.run(command,capture_output=True,text=True,timeout=timeout,creationflags=subprocess.CREATE_NO_WINDOW)
   (out/name).write_text(result.stdout+result.stderr,encoding='utf-8');result.check_returncode();return result
  except subprocess.TimeoutExpired as error:
   def text(value):return value.decode(errors='replace') if isinstance(value,bytes) else value or ''
   (out/name).write_text(text(error.stdout)+text(error.stderr)+'\nTIMEOUT',encoding='utf-8');raise
 try:
  wr=validate_worker(worker,receipt);ar=validate_authors(authors,receipt)
  report.update(framework=identity(),worker_build_sha256=digest(worker/'report.json'),worker_runtime_files=wr['runtime_files'],worker_dependencies=dependencies(receipt),authors=ar)
  report['stage']='validator-build';run([sys.executable,str(ROOT/'tools/sdk/build_validator.py'),'--output',str(out/'validator')],'validator-build.log',120)
  report['validator']=json.loads((out/'validator/build.json').read_text());shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
  files=[ROOT/'Supervisor/Tests/StorageMatrixChecks.cs']
  for folder in ['Supervisor/Core','Supervisor/Windows','Supervisor/Hosting','Supervisor/Broker','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities']:files+=list((ROOT/folder).glob('*.cs'))
  files += [ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'/name for name in ['WorkerFrameCodec.cs','RuntimeLimits.Generated.cs','WorkerExitCodes.cs']]
  report['stage']='driver-build';run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-define:SDK_SUPERVISOR_TESTS',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],'driver-build.log',120)
  report['checks_sha256']=digest(out/'Checks.exe');report['stage']='real-worker-matrix'
  attempts=ROOT/'Validation/Sdk'/('k'+out.name[:10]);result=run([str(out/'Checks.exe'),str(out),str(worker),str(authors),str(out/'validator'),str(attempts)],'result.log',240)
  need((out/'results.json').is_file(),'matrix results missing');validate_results(json.loads((out/'results.json').read_text(encoding='utf-8-sig')))
  need(sources()==receipt,'storage matrix source drift');report.update(status='passed',exit_code=result.returncode);print(result.stdout+result.stderr);return 0
 except Exception as error:
  report.update(error=repr(error),exit_code=getattr(error,'returncode',None));(out/'failure.log').write_text(traceback.format_exc(),encoding='utf-8');print(error,file=sys.stderr);return 1
 finally:
  for name in ['results.json','partial-results.json']:
   if (out/name).is_file():report['results']=json.loads((out/name).read_text(encoding='utf-8-sig'));break
  (out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)
if __name__=='__main__':raise SystemExit(main())
