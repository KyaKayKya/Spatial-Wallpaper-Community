"""Compile the real desktop client, run only memory-only raw mouse trace controls."""
from pathlib import Path
import argparse, hashlib, json, shutil, subprocess, sys, uuid
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from provenance import sources
from windows_framework import references, FRAMEWORK
parser=argparse.ArgumentParser();parser.add_argument('--repository',type=Path,required=True);parser.add_argument('--patch',type=Path);parser.add_argument('--negative-omit-flags',action='store_true');args=parser.parse_args()
base=args.repository.resolve();out=ROOT/'Validation/Content/sdk-runtime/E/mouse-raw-trace'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
paths=['Supervisor/Interactive/WindowsDesktopInput.cs','Supervisor/Tests/NativeDesktopInputHostChecks.cs']
copies={}
report={'status':'failed','scope':'background-memory-controls-and-desktop-client-compile-only','sources':sources(),'base_sources':{},'compiled_sources':{},'negative_omit_flags':args.negative_omit_flags}
def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def run(command,name):
 result=subprocess.run(command,capture_output=True,text=True,timeout=60,creationflags=subprocess.CREATE_NO_WINDOW)
 (out/name).write_text(result.stdout+result.stderr,encoding='utf-8');result.check_returncode();return result
try:
 if args.patch or args.negative_omit_flags:
  work=out/'patched';work.mkdir();run(['git','-C',str(work),'init','-q'],'git-init.log')
  for name in paths:
   p=work/name;p.parent.mkdir(parents=True,exist_ok=True);shutil.copyfile(base/name,p);copies[name]=p
  run(['git','-C',str(work),'add','--',*paths],'git-stage-baseline.log')
  if args.patch:
   run(['git','-C',str(work),'apply','--check',str(args.patch.resolve())],'patch-check.log')
   run(['git','-C',str(work),'apply',str(args.patch.resolve())],'patch.log')
   report['patch_sha256']=digest(args.patch.resolve())
  if args.negative_omit_flags:
   path=copies[paths[0]];text=path.read_text(encoding='utf-8-sig');needle='row.Flags=unchecked((uint)Marshal.ReadInt32(pointer,12));';assert text.count(needle)==1
   path.write_text(text.replace(needle,'row.Flags=0;'),encoding='utf-8')
 sys.path.insert(0,str(base/'Supervisor/Tests'))
 from native_input_client_sources import native_input_client_sources
 files=native_input_client_sources(base,desktop=True)
 report['base_sources']={p.relative_to(base).as_posix():digest(p) for p in files}
 report['base_sources']['Supervisor/Tests/native_input_client_sources.py']=digest(base/'Supervisor/Tests/native_input_client_sources.py')
 files=[copies.get(p.relative_to(base).as_posix(),p) for p in files]
 files.append(ROOT/'Supervisor/Tests/MouseRawTraceChecks.cs')
 report['compiled_sources']={str(p):digest(p) for p in files}
 report['runner_sha256']=digest(Path(__file__))
 shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),*['-r:'+str(FRAMEWORK/name) for name in ['Microsoft.CSharp.dll','System.Windows.Forms.dll','System.Drawing.dll']],'-r:'+str(out/'Newtonsoft.Json.dll'),*map(str,files)]
 run([*command,'-main:NativeDesktopInputHostChecks','-out:'+str(out/'Client.exe')],'client-compile.log')
 run([*command,'-main:MouseRawTraceChecks','-out:'+str(out/'Checks.exe')],'checks-compile.log')
 if args.negative_omit_flags:
  result=subprocess.run([str(out/'Checks.exe'),str(out)],capture_output=True,text=True,timeout=60,creationflags=subprocess.CREATE_NO_WINDOW)
  log=result.stdout+result.stderr;(out/'checks.log').write_text(log,encoding='utf-8')
  assert result.returncode==1 and 'System.Exception: raw-native-fields-exact-including-high-extra-bit' in log
 else:result=run([str(out/'Checks.exe'),str(out)],'checks.log')
 report['checks_exit']=result.returncode
 assert report['sources']==sources()
 assert all(digest(base/name)==value for name,value in report['base_sources'].items()),'base source changed during checks'
 assert all(digest(Path(name))==value for name,value in report['compiled_sources'].items()),'compiled source changed during checks'
 report['status']='expected-raw-flags-loss-rejected' if args.negative_omit_flags else 'passed-memory-controls-and-client-compile-only'
 report['binaries']={name:digest(out/name) for name in ['Client.exe','Checks.exe']}
except Exception as error:
 report['error']=repr(error);raise
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
