from pathlib import Path
import sys,subprocess,uuid,json,shutil,hashlib
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'tools/sdk'))
from check_contracts import DATA
from windows_framework import references,FRAMEWORK
from provenance import sources
base=root/'Validation/Content/sdk-runtime';out=base/'E/receiver-lifetime'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources();print(out,flush=True)
files=[root/'Supervisor/Tests/ReceiverLifetimeChecks.cs']
for folder in ['Supervisor/Core','Supervisor/Broker','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities','Supervisor/Interactive']:files+=list((root/folder).glob('*.cs'))
files += [root/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting'/name for name in ['WorkerFrameCodec.cs','WorkerExitCodes.cs','RuntimeLimits.Generated.cs']]
files += [root/'Supervisor/Windows'/name for name in ['Native.cs','RestrictedProcess.cs','TrustedAudioProcess.cs']]
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
mode=sys.argv[1] if len(sys.argv)>1 else 'before';assert mode in ['before','connecting','cleanup']
fixture_paths=[Path(__file__),root/'Supervisor/Tests/ReceiverLifetimeChecks.cs']
for fixture in fixture_paths:shutil.copyfile(fixture,out/fixture.name)
report={'status':'not-complete','sources':receipt,'mode':mode,'fixtures':{p.name:hashlib.sha256(p.read_bytes()).hexdigest() for p in fixture_paths},'scope':'real-owned-HWND-destruction-with-controlled-failing-connection-no-Host','exit_codes':{}}
try:
 for name,cmd in [('compile',[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(FRAMEWORK/'System.Windows.Forms.dll'),'-r:'+str(FRAMEWORK/'System.Drawing.dll'),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]),('result',[str(out/'Checks.exe'),mode])]:
  result=subprocess.run(cmd,capture_output=True,text=True,timeout=30);log=result.stdout+result.stderr;(out/(name+'.log')).write_text(log,encoding='utf-8');print(log);report['exit_codes'][name]=result.returncode;result.check_returncode()
 assert sources()==receipt
 report['status']='partial-real-window-controlled-connection'
finally:(out/'report.json').write_text(json.dumps(report,indent=2),encoding='utf-8')
