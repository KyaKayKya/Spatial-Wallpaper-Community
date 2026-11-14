"""Bounded trusted desktop-hit helper protocol and kernel process cleanup."""
from pathlib import Path
import sys,subprocess,uuid,json,shutil
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'));sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from check_contracts import DATA
from provenance import sources
from windows_framework import references,FRAMEWORK
from native_build import compile_probe
desktop='--desktop-input' in sys.argv
cleanup='--hook-cleanup' in sys.argv
acquisition_state='--acquisition-state' in sys.argv
real='--real' in sys.argv or desktop
negative='--negative-no-adapter' in sys.argv
delayed='--delayed-confirmation' in sys.argv
compile_only='--compile-only' in sys.argv
if delayed and (not desktop or negative):raise SystemExit('delayed confirmation requires positive --desktop-input')
if cleanup and (desktop or real or negative or delayed):raise SystemExit('hook cleanup is a noninteractive standalone mode')
if acquisition_state and (cleanup or desktop or real or negative or delayed):raise SystemExit('acquisition state is a noninteractive standalone mode')
if negative and not desktop:raise SystemExit('--negative-no-adapter requires --desktop-input')
out=ROOT/'Validation/Content/sdk-runtime/E/desktop-hit-client'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();print(out,flush=True)
compile_probe(ROOT/'Supervisor/Tests/DesktopHitFaultHelper.cpp',out/'Good.exe')
for mode in ['Hang','Partial','Stale','Invalid']:shutil.copyfile(out/'Good.exe',out/(mode+'.exe'))
if real:compile_probe(ROOT/'Supervisor/Interactive/DesktopHitTester.cpp',out/'DesktopHitTester.exe')
files=[ROOT/p for p in ['Supervisor/Tests/DesktopHitChecks.cs','Supervisor/Windows/Native.cs','Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputBroker.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputGate.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/WindowsMachineClock.cs']]
files+=list((ROOT/'Supervisor/Interactive').glob('DesktopHitClient.cs'))
if real:files[0]=ROOT/'Supervisor/Tests/DesktopHitRealChecks.cs'
if desktop or cleanup:
 files[0]=ROOT/('Supervisor/Tests/DesktopHookCleanupChecks.cs' if cleanup else 'Supervisor/Tests/DesktopInputChecks.cs')
 files+=list((ROOT/'Supervisor/Interactive').glob('WindowsDesktopInput.cs'))
if acquisition_state:
 files[0]=ROOT/'Supervisor/Tests/DesktopAcquisitionChecks.cs'
 files+=list((ROOT/'Supervisor/Interactive').glob('WindowsDesktopInput.cs'))
if desktop or cleanup or acquisition_state:
 files.extend(ROOT/'Supervisor/Interactive'/name for name in ['OwnedInputThread.cs','InputCommitFence.cs','InputHeartbeat.cs'])
command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(FRAMEWORK/'Microsoft.CSharp.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]
if real:command+=['-r:'+str(FRAMEWORK/'System.Windows.Forms.dll'),'-r:'+str(FRAMEWORK/'System.Drawing.dll')]
report={'status':'not-complete','sources':receipt,'mode':'hook-cleanup' if cleanup else 'desktop-input' if desktop else 'explorer-hit' if real else 'fault-helper','negativeNoAdapter':negative,'delayedConfirmation':delayed,'compileOnly':compile_only,'exitCodes':{}}
if acquisition_state:report['mode']='acquisition-state'
try:
 for name,args in [('compile',command),('result',[str(out/'Checks.exe'),str(out/'DesktopHitTester.exe') if real else str(out)]+(['--no-adapter'] if negative else ['--delayed-confirmation'] if delayed else []))]:
  if compile_only and name=='result':continue
  result=subprocess.run(args,capture_output=True,text=True,timeout=30);log=result.stdout+result.stderr;(out/(name+'.log')).write_text(log,encoding='utf-8');print(log)
  report['exitCodes'][name]=result.returncode
  if name=='result' and negative:
   assert result.returncode==1 and 'PASS actual-blank-and-icon-fixture' in log and log.count('PASS native-injection-delivered')==2 and 'System.Exception: completed-blank-click-acquires-actual-focus' in log and 'PASS completed-blank-click-acquires-actual-focus' not in log,'negative must fail only at native focus acquisition'
  else:result.check_returncode()
 assert sources()==receipt
 report['status']='compiled-not-runtime-verified' if compile_only else 'partial-native-hook-cleanup' if cleanup else 'partial-desktop-input-missing-adapter-negative' if negative else 'partial-native-desktop-input' if desktop else 'partial-real-explorer-hit' if real else 'partial-bounded-desktop-hit-client'
 if acquisition_state and not compile_only:report['status']='partial-acquisition-state-negatives'
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
