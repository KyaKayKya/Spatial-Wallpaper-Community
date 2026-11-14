"""Same production Query(25), owned synchronous callback, pumped/background control."""
from pathlib import Path
import argparse,ctypes,json,shutil,subprocess,sys,uuid
root=Path(__file__).resolve().parents[2]
sys.path[:0]=[str(root/'tools/sdk'),str(root/'tools/sdk/experiments')]
from check_contracts import DATA
from native_build import compile_probe
from provenance import sources
from windows_framework import references,FRAMEWORK
from windows_sandbox import k,LIMIT,check
parser=argparse.ArgumentParser();parser.add_argument('--mode',choices=['synchronous','background'],required=True);parser.add_argument('--msaa',action='store_true');parser.add_argument('--real-hook-owner',action='store_true');parser.add_argument('--callback-on-owner',action='store_true');args=parser.parse_args()
if args.callback_on_owner and not args.real_hook_owner:parser.error('--callback-on-owner requires --real-hook-owner')
if args.real_hook_owner and not args.msaa:parser.error('--real-hook-owner requires --msaa')
out=root/'Validation/Content/sdk-runtime/E/desktop-callback'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
receipt=sources();report={'sources':receipt,'mode':args.mode,'msaa':args.msaa,'realHookOwner':args.real_hook_owner,'status':'not-complete'}
helper=out/('MsaaHelper.exe' if args.msaa else 'Helper.exe')
compile_probe(root/'Supervisor/Tests/DesktopHitCallbackHelper.cpp',helper)
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
files=['Supervisor/Tests/DesktopHitCallbackChecks.cs','Supervisor/Interactive/DesktopHitClient.cs','Supervisor/Windows/Native.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/WindowsMachineClock.cs']
if args.real_hook_owner:files[0]='Supervisor/Tests/InputThreadMsaaChecks.cs';files.append('Supervisor/Interactive/OwnedInputThread.cs')
command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(FRAMEWORK/'System.Windows.Forms.dll'),'-r:'+str(FRAMEWORK/'System.Drawing.dll'),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*[str(root/p) for p in files]]
job=None;process=None
try:
 subprocess.run(command,check=True)
 job=k.CreateJobObjectW(None,None);check(job);limits=LIMIT();limits.basic.flags=0x2000|8;limits.basic.active=2;check(k.SetInformationJobObject(job,9,ctypes.byref(limits),ctypes.sizeof(limits)))
 flags=(['--block-ui'] if args.mode=='synchronous' else []) if args.real_hook_owner else [args.mode,*(['--msaa'] if args.msaa else [])]
 if args.callback_on_owner:flags=['--callback-on-owner',*(['--async-query'] if args.mode=='background' else [])]
 process=subprocess.Popen([str(out/'Checks.exe'),str(helper),str(out/'observation.json'),*flags],stdin=subprocess.PIPE,stdout=subprocess.PIPE,stderr=subprocess.PIPE,text=True)
 check(k.AssignProcessToJobObject(job,int(process._handle)))
 stdout,stderr=process.communicate('go\n',timeout=15);report.update(exitCode=process.returncode,stdout=stdout,stderr=stderr);print(stdout+stderr)
 assert process.returncode==0,'callback-control-failed'
 assert sources()==receipt
 report['status']='partial-owned-callback-control-not-production-fix'
except BaseException as error:report['failure']=repr(error);raise
finally:
 errors=[]
 if job and not k.CloseHandle(job):errors.append('job-close-failed')
 if process:
  try:
   if process.poll() is None:process.kill()
   process.wait(timeout=5);report['ownerExited']=True
  except Exception as error:errors.append(repr(error))
 report['cleanupErrors']=errors
 (out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
assert not report['cleanupErrors']
