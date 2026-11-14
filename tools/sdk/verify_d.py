"""Stage D: build both production and injected-fault binaries, run all applicable resource/supervision gates."""
from pathlib import Path
import sys,subprocess,json,uuid,time,hashlib,platform
from provenance import ROOT,sources
from process_output import artifact_line
BASE=ROOT/'Validation/Content/sdk-runtime'
def main():
 run=uuid.uuid4().hex;out=BASE/'D/acceptance'/run;out.mkdir(parents=True);receipt=sources();report={'run_id':run,'status':'running','platform':platform.platform(),'sources':receipt,'steps':[],'commit':subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip()}
 target=BASE/'reports/stage-D.json'
 def publish():target.write_text(json.dumps(report,indent=2),encoding='utf-8');(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
 def step(script,*arguments,artifact=False):
  command=[sys.executable,str(ROOT/script),*map(str,arguments)];index=len(report['steps']);print('D step',index,script,flush=True);began=time.time()
  result=subprocess.run(command,cwd=ROOT,timeout=2400,capture_output=True,text=True);log=out/(str(index)+'.txt');log.write_text(result.stdout+result.stderr,encoding='utf-8');record={'command':[script,*map(str,arguments)],'exit_code':result.returncode,'log':log.relative_to(ROOT).as_posix()};report['steps'].append(record);publish()
  if result.returncode:raise RuntimeError('stage-D-failed:'+script+'; '+str(log))
  if artifact:
   path=Path(artifact_line(result.stdout)).resolve();evidence=path/'report.json'
   if not path.is_relative_to(BASE.resolve()) or not evidence.is_file() or evidence.stat().st_mtime<began:raise RuntimeError('missing-stale-or-foreign-evidence:'+script)
   record['evidence']=evidence.relative_to(ROOT).as_posix();record['evidence_sha256']=hashlib.sha256(evidence.read_bytes()).hexdigest();publish();return path
 try:
  step('tools/sdk/generate_rules.py','--check');step('tools/sdk/compile_package.py');step('tools/sdk/check_execution.py');step('tools/sdk/check_resources.py');step('tools/sdk/check_worker_protocol.py')
  command=[sys.executable,'-m','unittest','discover','-s','tools/sdk/tests','-v'];result=subprocess.run(command,cwd=ROOT,timeout=240,capture_output=True,text=True);(out/'tests.txt').write_text(result.stdout+result.stderr,encoding='utf-8');report['steps'].append({'command':command[1:],'exit_code':result.returncode});assert result.returncode==0,'SDK tests failed';publish()
  step('tools/sdk/build_resource_probe.py',artifact=True)
  author=step('tools/sdk/build_execution.py',artifact=True)
  worker=step('tools/sdk/build_worker.py',artifact=True)
  fixture=step('tools/sdk/build_stream_worker.py','--package',author/'scene.spatial',artifact=True)
  step('Supervisor/Tests/run_unity_stream.py','--build',fixture,'--baseline',author/'play.png')
  for script in ['run_fault_journal.py','run_watchdog.py','run_runtime.py','run_journal_actor.py','run_restricted_process.py','run_permissions.py','probe_access_boundary.py','run_profile_boundary.py','run_process_supervision.py','run_crash_owner.py']:
   step('Supervisor/Tests/'+script)
  step('Supervisor/Tests/run_unity_attacks.py','--build',fixture)
  step('Supervisor/Tests/run_unity_isolation.py','--build',fixture)
  step('Supervisor/Tests/run_host.py','--worker',worker,artifact=True)
  step('Supervisor/Tests/run_recovery.py','--worker',worker,artifact=True)
  step('Supervisor/Tests/run_budget.py','--worker',worker,'--repetitions','3',artifact=True)
  if sources()!=receipt:raise RuntimeError('source changed during D acceptance')
  report.update(status='passed-stage-D-resource-supervision-only',not_verified=['E input/storage/audio/LLM capabilities','F full author export, sandbox preview and desktop client integration','G independent delivery and performance grades','target integrated GPU and hardware outside the measured reference host','macOS','destructive GPU/driver tests'])
  return 0
 except Exception as error:report.update(status='failed',error=repr(error));return 1
 finally:report['finished_at_unix']=time.time();publish();print(out,flush=True)
if __name__=='__main__':raise SystemExit(main())
