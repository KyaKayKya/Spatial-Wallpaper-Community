"""Verify test-owned live thread state sampling with actual busy/suspended child controls."""
from pathlib import Path
import sys,subprocess,uuid,json,argparse,shutil,ctypes,time
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'tools/sdk'));sys.path.insert(0,str(root/'tools/sdk/experiments'))
from check_contracts import DATA
from windows_framework import references
from provenance import sources
from windows_sandbox import k,LIMIT,check
from process_observation import observe_child
parser=argparse.ArgumentParser();parser.add_argument('--negative-cached-snapshot',action='store_true');parser.add_argument('--kill-owner',action='store_true');args=parser.parse_args()
assert not(args.kill_owner and args.negative_cached_snapshot)
out=root/'Validation/Content/sdk-runtime/E/thread-sampling'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources()
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
files=[root/'Supervisor/Tests'/name for name in ['OwnedThreadSampler.cs','OwnedThreadEvidence.cs','OwnedHelperPause.cs','ThreadSamplingChecks.cs']]
files += [root/name for name in ['Supervisor/Interactive/DesktopHitClient.cs','Supervisor/Windows/Native.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/WindowsMachineClock.cs']]
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
job=k.CreateJobObjectW(None,None);check(job);limits=LIMIT();limits.basic.flags=0x2000|8;limits.basic.active=2
process=None;child=None;report={'sources':receipt,'negativeCachedSnapshot':args.negative_cached_snapshot,'killOwner':args.kill_owner,'status':'not-complete'}
try:
 check(k.SetInformationJobObject(job,9,ctypes.byref(limits),ctypes.sizeof(limits)))
 process=subprocess.Popen([str(out/'Checks.exe'),str(out),*(['--negative'] if args.negative_cached_snapshot else []),*(['--wait-owner-kill'] if args.kill_owner else [])],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
 check(k.AssignProcessToJobObject(job,int(process._handle)));process.stdin.write('go\n');process.stdin.flush()
 if args.kill_owner:
  deadline=time.monotonic()+5
  while not(out/'paused-child').exists():
   assert process.poll() is None and time.monotonic()<deadline,'pause marker missing';time.sleep(.01)
  child=observe_child(process.pid,'Checks.exe');assert child.pid==int((out/'paused-child').read_text()) and not child.exited()
  process.kill();process.wait(timeout=3);assert not child.exited(),'suspended child ended before outer Job closure'
  check(k.CloseHandle(job));job=None;assert child.exited(3),'suspended child survived owned Job closure';report['suspendedChildReapedAfterOwnerKilled']=True
 stdout,stderr=process.communicate(timeout=30);report.update(exitCode=process.returncode,stdout=stdout,stderr=stderr)
 assert sources()==receipt
 if not args.kill_owner:assert process.returncode==0,'thread-sampling-check-failed'
 report['status']='partial-owned-thread-sampling-control'
except BaseException as error:report['failure']=repr(error);raise
finally:
 errors=[]
 if job:
  if not k.CloseHandle(job):errors.append('job-close-failed')
 if process:
  try:
   if process.poll() is None:process.kill()
   process.wait(timeout=5);report['ownerExited']=True
  except Exception as error:errors.append(repr(error))
 if child:
  try:report['childExited']=child.exited(3)
  finally:child.close()
 report['cleanupErrors']=errors
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8');print(out);print(report.get('stdout','')+report.get('stderr',''))
assert not report['cleanupErrors'] and report.get('childExited',True)
