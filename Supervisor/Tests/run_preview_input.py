"""Real owned Win32 preview input -> production InputBroker -> shared author frames."""
from pathlib import Path
import sys,subprocess,uuid,json,argparse
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references,FRAMEWORK
from provenance import sources
parser=argparse.ArgumentParser(description=__doc__);parser.add_argument('--negative-no-adapter',action='store_true');args=parser.parse_args()
out=ROOT/'Validation/Content/sdk-runtime/E/native-preview-input'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();print(out,flush=True)
files=[ROOT/p for p in ['Supervisor/Tests/PreviewInputChecks.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputBroker.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputGate.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/WindowsMachineClock.cs','Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs']]
files.append(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Native/Windows/WindowsPreviewInput.cs')
command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(FRAMEWORK/'System.Windows.Forms.dll'),'-r:'+str(FRAMEWORK/'System.Drawing.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]
report={'status':'not-complete','sources':receipt,'negativeNoAdapter':args.negative_no_adapter}
try:
 for name,cmd in [('compile',command),('result',[str(out/'Checks.exe')]+(['--no-adapter'] if args.negative_no_adapter else []))]:
  result=subprocess.run(cmd,capture_output=True,text=True,timeout=30);(out/(name+'.log')).write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);result.check_returncode()
 assert sources()==receipt,'native preview source drift'
 report['status']='partial-native-preview-input-only'
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
