"""Build and run SDK acceptance evidence. Unimplemented stages never pass."""
from pathlib import Path
import argparse, subprocess, sys, json, time, uuid, hashlib
from evidence import load_current_build,require_permissions,require_render
ROOT=Path(__file__).resolve().parents[2]
BASE=ROOT/'Validation/Content/sdk-runtime'
def publish(path,data):
 path.parent.mkdir(parents=True,exist_ok=True)
 temp=path.with_suffix('.tmp');temp.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8');temp.replace(path)
def main():
 p=argparse.ArgumentParser(description=__doc__)
 group=p.add_mutually_exclusive_group(required=True);group.add_argument('--stage',choices=list('ABCDEFG'));group.add_argument('--all',action='store_true')
 p.add_argument('--platform',choices=['windows','macos'],required=True)
 p.add_argument('--delivery',type=Path)
 p.add_argument('--list',action='store_true')
 p.add_argument('--case',action='append',default=[])
 args=p.parse_args()
 if args.platform=='windows' and args.stage in ['F','G']:
  if not args.delivery:p.error('F/G require --delivery with one assembled installed candidate')
  command=[sys.executable,str(ROOT/'tools/sdk/verify_fg.py'),'--stage',args.stage,'--delivery',str(args.delivery)]
  if args.list:command.append('--list')
  for name in args.case:command.extend(['--case',name])
  return subprocess.call(command,cwd=ROOT)
 if args.delivery or args.list or args.case:p.error('--delivery/--list/--case currently belong to the F/G entry')
 report={'run_id':uuid.uuid4().hex,'started_at_unix':time.time(),'status':'running','stage':args.stage or 'all','platform':args.platform,'steps':[]}
 target=BASE/'reports'/('stage-'+(args.stage or 'all')+'.json');publish(target,report)
 if args.platform=='windows' and args.stage=='E':
  return subprocess.call([sys.executable,str(ROOT/'tools/sdk/verify_e.py')],cwd=ROOT)
 if args.platform=='windows' and args.stage=='D':
  return subprocess.call([sys.executable,str(ROOT/'tools/sdk/verify_d.py')],cwd=ROOT)
 if args.platform=='windows' and args.stage=='B':
  try:
   commands=[[sys.executable,'tools/sdk/generate_rules.py','--check'],[sys.executable,'tools/sdk/compile_package.py'],[sys.executable,'-m','unittest','discover','-s','tools/sdk/tests','-v'],[sys.executable,'tools/sdk/run_admission.py']]
   for cmd in commands:
    result=subprocess.run(cmd,cwd=ROOT,timeout=1200,capture_output=True,text=True);print(result.stdout+result.stderr,flush=True)
    report['steps'].append({'command':cmd[1:],'exit_code':result.returncode})
    if result.returncode:raise RuntimeError('stage B step failed')
   report['unity']=json.loads((BASE/'reports/stage-B-unity.json').read_text(encoding='utf-8'))
   report['admission']=json.loads((BASE/'reports/stage-B-admission.json').read_text(encoding='utf-8'))
   report.update(status='passed-stage-B-contracts-only',not_verified=['C# conversion and VM bindings (C)','production admission integration (F)','macOS'])
   return 0
  except Exception as error:
   report.update(status='failed',error=repr(error));return 1
  finally:
   report['finished_at_unix']=time.time();publish(target,report);print(target,flush=True)
 if args.platform=='windows' and args.stage=='C':
  try:
   for cmd in [[sys.executable,'tools/sdk/generate_rules.py','--check'],[sys.executable,'tools/sdk/compile_package.py'],[sys.executable,'tools/sdk/check_execution.py'],[sys.executable,'-m','unittest','discover','-s','tools/sdk/tests','-v'],[sys.executable,'tools/sdk/build_execution.py']]:
    result=subprocess.run(cmd,cwd=ROOT,timeout=2400,capture_output=True,text=True);print(result.stdout+result.stderr,flush=True)
    report['steps'].append({'command':cmd[1:],'exit_code':result.returncode})
    if result.returncode:raise RuntimeError('stage C step failed')
   report['execution']=json.loads((BASE/'reports/stage-C-execution.json').read_text(encoding='utf-8'))
   report.update(status='passed-stage-C-execution-mechanisms-only',not_verified=['D resource accounting and out-of-process supervision','E capabilities','F complete author resource export and client chain','G delivery','macOS'])
   return 0
  except Exception as error:report.update(status='failed',error=repr(error));return 1
  finally:
   report['finished_at_unix']=time.time();publish(target,report);print(target,flush=True)
 if args.platform!='windows' or args.stage!='A':
  report.update(status='not-implemented-or-unverified',missing=['Full all-stage certification and macOS remain unverified; run implemented stage entries individually. No SDK completion claim.']);publish(target,report);return 2
 def run(script,*arguments,has_report=False):
  before=time.time()
  result=subprocess.run([sys.executable,str(ROOT/'tools/sdk/experiments'/script),*map(str,arguments)],cwd=ROOT,text=True,capture_output=True,timeout=1200)
  step={'command':[script,*map(str,arguments)],'exit_code':result.returncode}
  report['steps'].append(step)
  raw=BASE/'A'/report['run_id'];raw.mkdir(parents=True,exist_ok=True)
  (raw/(script+'.txt')).write_text(result.stdout+result.stderr,encoding='utf-8')
  print(script,'exit',result.returncode,flush=True)
  if result.returncode:raise RuntimeError('verification step failed: '+script)
  if has_report:
   path=Path(result.stdout.strip().splitlines()[-1]).resolve()
   if not path.is_relative_to(BASE.resolve()) or path.suffix!='.json' or path.stat().st_mtime<before:raise ValueError('stale-or-foreign-evidence')
   step['evidence']=path.relative_to(ROOT).as_posix();step['result']=json.loads(path.read_text(encoding='utf-8-sig'))
   return path
 try:
  run('build_unity.py')
  permissions=run('run_permissions.py',has_report=True)
  require_permissions(json.loads(permissions.read_text())['cases'])
  render=run('run_render.py',has_report=True)
  require_render(json.loads(render.read_text())['cases'])
  ipc=run('run_frame_ipc.py',has_report=True)
  run('run_presenter.py',ipc.parent/'frame.rgb')
  for mode in ['preview','desktop']:
   data=json.loads((ipc.parent/(mode+'-presentation.json')).read_text(encoding='utf-8-sig'));assert data['verified'] and not data['error']
  build=load_current_build(ROOT)
  receipt=build/'source-receipt.json'
  report['source_receipt']=json.loads(receipt.read_text())
  report['removed_author_assembly']=json.loads((build/'removed-author-assembly.json').read_text())
  assert not list((build/'player').rglob('StageAAuthor.dll'))
  report['runtime_files']={x.relative_to(build/'player').as_posix():hashlib.sha256(x.read_bytes()).hexdigest() for x in (build/'player').rglob('*') if x.is_file()}
  report.update(status='passed-stage-A-feasibility-only',not_verified=['production sandbox hardening','resource budgets and malicious program/package regression','audio/storage/LLM brokers','macOS','integrated SDK delivery'])
  return 0
 except Exception as error:
  report.update(status='failed',error=repr(error));return 1
 finally:
  report['finished_at_unix']=time.time();publish(target,report);print(target,flush=True)
if __name__=='__main__':raise SystemExit(main())
