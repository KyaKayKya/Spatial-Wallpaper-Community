"""Trusted preview/desktop channels use the production Host gate and shared frames."""
from pathlib import Path
import sys,subprocess,uuid,json
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references,FRAMEWORK
from provenance import sources
desktop='--desktop' in sys.argv
out=ROOT/'Validation/Content/sdk-runtime/E/input-channel'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();print(out,flush=True)
files=[ROOT/p for p in ['Supervisor/Tests/InputChannelChecks.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputBroker.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputGate.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/WindowsMachineClock.cs','Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs']]
files.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PreviewInputChannel.cs')
if desktop:
 files[0]=ROOT/'Supervisor/Tests/DesktopChannelChecks.cs'
 files+=list((ROOT/'Supervisor/Broker').glob('DesktopInputChannel.cs'))
command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(FRAMEWORK/'Microsoft.CSharp.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]
report={'status':'not-complete','mode':'desktop' if desktop else 'preview','sources':receipt}
try:
 for name,cmd in [('compile',command),('result',[str(out/'Checks.exe')])]:
  result=subprocess.run(cmd,capture_output=True,text=True,timeout=30);(out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);result.check_returncode()
 assert sources()==receipt,'input channel source drift'
 report['status']='partial-input-channel-only'
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
