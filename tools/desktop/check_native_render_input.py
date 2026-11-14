"""Actual native renderer/input/script composition; partial development checks."""
import argparse,hashlib,json,pathlib,shutil,subprocess,sys,uuid,time
from contextlib import nullcontext
ROOT=pathlib.Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references,FRAMEWORK
from case_process import Job

def main():
 p=argparse.ArgumentParser(description=__doc__)
 for name in ['client','supervisor','player','worker','program']:p.add_argument('--'+name,type=pathlib.Path,required=True)
 p.add_argument('--renderer-crash',action='store_true')
 p.add_argument('--showcase',action='store_true')
 p.add_argument('--os-clicks',action='store_true',help='actual bounded desktop primary clicks; requires input.native-script.json')
 p.add_argument('--os-all-input',action='store_true',help='existing seven buttons, held/edge queries, pointer and both wheel directions; requires all-input fixture')
 p.add_argument('--os-exclusion',choices=['icon','taskbar'],help='actual hover/OS wheel exclusion after healthy local wheel positive; requires all-input fixture; no icon/taskbar click')
 p.add_argument('--os-exclusion-click',action='store_true',help='use a real single primary click immediately after entering the exclusion; also require same-session button down/up positive')
 p.add_argument('--os-focus-grant',action='store_true',help='test-only injector foreground grant to the pinned trusted input role, matching the original native input fixture; no physical input claim')
 p.add_argument('--qa-leftover',action='store_true',help=argparse.SUPPRESS)
 p.add_argument('--qa-pre-revoke',action='store_true',help=argparse.SUPPRESS)
 a=p.parse_args();out=ROOT/'Validation/Content/sdk-runtime/native-render/task3/native-render-input'/uuid.uuid4().hex[:12];out.mkdir(parents=True)
 for name in ['Supervisor.exe','Newtonsoft.Json.dll']:shutil.copyfile(a.supervisor/name,out/name)
 shutil.copyfile(a.supervisor/'Supervisor.exe.config',out/'NativeRenderInputChecks.exe.config')
 sources=[ROOT/'tools/desktop/NativeRenderInputChecks.cs']+[ROOT/'Prototype/Assets/Desktop/Windows'/name for name in ['NativeRenderProcess.cs','NativeDesktopTarget.cs','WindowsDesktopWindow.cs']]+[ROOT/'Prototype/Assets/Desktop/Core/DesktopDisplay.cs']
 sources.extend([ROOT/'Supervisor/Interactive/DesktopHitClient.cs',ROOT/'Supervisor/Windows/Native.cs'])
 frozen_paths=sources+[pathlib.Path(__file__),a.program]
 for folder in [a.client,a.supervisor,a.player.parent,a.worker]:frozen_paths.extend(f for f in folder.rglob('*') if f.is_file())
 frozen_hashes={str(f.resolve()):hashlib.sha256(f.read_bytes()).hexdigest() for f in frozen_paths}
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-platform:x64',*references(),'-r:'+str(FRAMEWORK/'System.Drawing.dll'),'-r:'+str(FRAMEWORK/'System.Windows.Forms.dll'),'-r:'+str(out/'Supervisor.exe'),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'NativeRenderInputChecks.exe'),*map(str,sources)]
 b=subprocess.run(command,capture_output=True,text=True);(out/'compile.log').write_text(b.stdout+b.stderr,encoding='utf-8');print(out,flush=True);print(b.stdout+b.stderr)
 if b.returncode:return b.returncode
 compiled_hashes={str(f.resolve()):hashlib.sha256(f.read_bytes()).hexdigest() for f in out.iterdir() if f.suffix in ['.exe','.dll','.config']}
 run=ROOT/'Validation/NB'/('native-render-input-'+out.name)
 command=[str(out/'NativeRenderInputChecks.exe'),str(a.player.resolve()),str(run),str((a.client/'SpatialWallpaper.Preview.exe').resolve()),str((a.supervisor/'Supervisor.exe').resolve()),str(a.worker.resolve()),str(a.program.resolve())]+(['--renderer-crash'] if a.renderer_crash else [])+(['--showcase'] if a.showcase else [])+(['--os-clicks'] if a.os_clicks else [])+(['--os-focus-grant'] if a.os_focus_grant else [])
 assert sum(bool(v) for v in [a.os_clicks,a.os_all_input,a.os_exclusion])<=1,'choose one OS fixture'
 os_input=a.os_clicks or a.os_all_input or bool(a.os_exclusion)
 assert not a.os_focus_grant or os_input,'foreground grant requires OS fixture'
 if a.os_all_input:command.append('--os-all-input')
 if a.os_exclusion:command.extend(['--os-exclusion',a.os_exclusion])
 if a.os_exclusion_click:
  assert a.os_exclusion,'excluded click requires an exclusion target'
  command.append('--os-exclusion-click')
 if a.qa_leftover:command.append('--qa-leftover')
 if a.qa_pre_revoke:
  assert a.os_exclusion,'pre-revoke is only an OS exclusion negative control'
  command.append('--qa-pre-revoke')
 if os_input:
  from check_main_manager import exposed_desktop,u
  from PIL import ImageGrab
  display={'x':0,'y':0,'width':u.GetSystemMetrics(0),'height':u.GetSystemMetrics(1)}
 job=Job();process=None;failure=None;job_receipt={}
 try:
  with exposed_desktop(display) if os_input else nullcontext():
   process=subprocess.Popen([*command,'--await-parent-job'],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
   job.assign(process);job_receipt['assigned_before_release']=True
   process.stdin.write('RUN\n');process.stdin.flush();process.stdin.close();process.stdin=None
   deadline=time.monotonic()+120;last_diagnostic=None
   while process.poll() is None:
    if time.monotonic()>deadline:raise subprocess.TimeoutExpired(command,120)
    if os_input:
     for diagnostic in run.rglob('input-diagnostic.json'):
      try:data=json.loads(diagnostic.read_text(encoding='utf-8'))
      except (json.JSONDecodeError,FileNotFoundError):continue # Test-only writer/cleanup can race the observation.
      (run/'source-diagnostic.json').write_text(json.dumps(data,indent=2),encoding='utf-8')
      encoded=json.dumps(data,sort_keys=True)
      if encoded!=last_diagnostic:
       with (run/'source-diagnostic.jsonl').open('a',encoding='utf-8') as stream:stream.write(encoded+'\n')
       last_diagnostic=encoded
    for name in ['os-clicks-before','os-clicks-positive','os-clicks-negative'] if a.os_clicks or a.os_exclusion else []:
     request=run/(name+'.json');marker=run/(name+'.captured')
     if request.exists() and not marker.exists():
      try:data=json.loads(request.read_text(encoding='utf-8'))
      except json.JSONDecodeError:continue
      d=data['display'];ImageGrab.grab(bbox=(d['x'],d['y'],d['x']+d['width'],d['y']+d['height']),all_screens=True).save(run/(name+'.png'));marker.write_text('Actual desktop screenshot captured',encoding='utf-8')
    time.sleep(.05)
 except Exception as error:
  failure=repr(error)
 finally:
  try:
   active=job.active()
   if process is not None and process.poll() is not None:
    retired=time.monotonic()+.5
    while active and time.monotonic()<retired:time.sleep(.01);active=job.active()
   job_receipt['active_before_fallback']=active
   if active:
    job_receipt['remaining_job_members']=job.members()
    job.terminate()
   deadline=time.monotonic()+5
   while job.active() and time.monotonic()<deadline:time.sleep(.01)
   job_receipt['active_after_cleanup']=job.active()
   job.wait_members()
  finally:job.close()
  if process is not None:
   stdout,stderr=process.communicate(timeout=5)
   result=subprocess.CompletedProcess(command,process.returncode or (1 if failure else 0),stdout,stderr)
  else:result=subprocess.CompletedProcess(command,1,'','process failed before launch')
  if failure:result=subprocess.CompletedProcess(command,1,result.stdout,result.stderr+'\n'+failure)
  if job_receipt.get('active_before_fallback') or job_receipt.get('active_after_cleanup')!=0:result=subprocess.CompletedProcess(command,1,result.stdout,result.stderr+'\nowned Job fallback required or cleanup unproven')
  (out/'job-cleanup.json').write_text(json.dumps(job_receipt,indent=2),encoding='utf-8')
 unchanged=all(pathlib.Path(path).is_file() and hashlib.sha256(pathlib.Path(path).read_bytes()).hexdigest()==digest for path,digest in {**frozen_hashes,**compiled_hashes}.items())
 if not unchanged:result=subprocess.CompletedProcess(command,1,result.stdout,result.stderr+'\ninput sources/program/binaries changed during verification')
 (out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');(out/'receipt.json').write_text(json.dumps({'exit_code':result.returncode,'run':run.relative_to(ROOT).as_posix(),'inputs_unchanged':unchanged,'frozen_input_sha256':frozen_hashes,'compiled_sha256':compiled_hashes,'sources_sha256':{s.relative_to(ROOT).as_posix():frozen_hashes[str(s.resolve())] for s in sources},'files_sha256':{pathlib.Path(path).name:digest for path,digest in compiled_hashes.items()}},indent=2),encoding='utf-8');print(result.stdout+result.stderr);return result.returncode
if __name__=='__main__':raise SystemExit(main())
