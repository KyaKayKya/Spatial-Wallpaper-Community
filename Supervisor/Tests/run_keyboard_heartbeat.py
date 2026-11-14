"""Keyboard event invalidation state regression; not real desktop acceptance."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from provenance import sources
from windows_framework import references,FRAMEWORK
out=ROOT/'Validation/Content/sdk-runtime/E/keyboard-heartbeat'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
report={'status':'failed','sources':sources(),'scope':'production Keyboard and InputHeartbeat; ownership/publication/native forwarding substituted; no Host or desktop claim'}
def body(text,signature,replacement):
 start=text.index(signature);opening=text.index('{',start);depth=1;end=opening+1
 while depth:
  depth+=(text[end]=='{')-(text[end]=='}');end+=1
 return text[:opening]+replacement+text[end:]
try:
 original=ROOT/'Supervisor/Interactive/WindowsDesktopInput.cs';text=original.read_text()
 text=body(text,'bool Owns()', '{acceptanceOwnsCalls++;return acceptanceOwns;}')
 text=body(text,'bool Publish(', '{acceptancePublishes++;return true;}')
 needle='return CallNextHookEx(keyHook,code,message,value);';assert text.count(needle)==1
 text=text.replace(needle,'return IntPtr.Zero;')
 text=text.replace('const uint Timer=', 'bool acceptanceOwns;int acceptanceOwnsCalls,acceptancePublishes;const uint Timer=',1)
 copy=out/'KeyboardState.cs';copy.write_text(text)
 report['substituted_source_sha256']=hashlib.sha256(copy.read_bytes()).hexdigest()
 files=[ROOT/'Supervisor/Tests/DesktopKeyboardHeartbeatChecks.cs',copy,ROOT/'Supervisor/Windows/Native.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs']
 files += [ROOT/'Supervisor/Interactive'/name for name in ['DesktopHitClient.cs','OwnedInputThread.cs','InputCommitFence.cs','InputHeartbeat.cs']]
 files += [ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities'/name for name in ['InputBroker.cs','InputGate.cs','InputSnapshot.cs','WindowsMachineClock.cs']]
 command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)]
 for name,cmd in [('compile',command),('result',[str(out/'Checks.exe')])]:
  result=subprocess.run(cmd,capture_output=True,text=True,timeout=30);(out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);report[name+'_exit']=result.returncode;result.check_returncode()
 assert report['sources']==sources();report['status']='passed-keyboard-invalidation-state-only'
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
