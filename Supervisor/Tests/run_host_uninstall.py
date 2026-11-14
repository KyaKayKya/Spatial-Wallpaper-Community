from pathlib import Path
import sys,subprocess,uuid,shutil,json,hashlib
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'tools/sdk'))
from windows_framework import references,identity
data=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
out=root/'Validation/Content/sdk-runtime/G/host-uninstall'/uuid.uuid4().hex;out.mkdir(parents=True)
shutil.copyfile(data/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
files=[Path(__file__).with_name('HostUninstallChecks.cs'),root/'Supervisor/Host.cs']
for folder in ['Supervisor/Core','Supervisor/Windows','Supervisor/Hosting','Supervisor/Broker','Supervisor/Session','Packages/com.spatialwallpaper.sdk/Runtime/Capabilities','Packages/com.spatialwallpaper.sdk/Runtime/Hosting','Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows']:
 files.extend(p for p in (root/folder).glob('*.cs') if p.name!='FrameMailbox.cs' and (not folder.endswith('Hosting/Windows') or p.name in ['SupervisorManagerConnection.cs','PinnedProcessOwner.cs']))
receipt={p.relative_to(root).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in [Path(__file__),*files]}
command=[str(data/'MonoBleedingEdge/bin/mono.exe'),str(data/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-define:SDK_SUPERVISOR_TESTS','-main:HostUninstallChecks',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]
r=subprocess.run(command,capture_output=True,text=True);log=r.stdout+r.stderr
if r.returncode==0:
 r=subprocess.run([str(out/'Checks.exe'),str(out/'state')],capture_output=True,text=True,timeout=30);log+=r.stdout+r.stderr
(out/'checks.txt').write_text(log,encoding='utf-8');unchanged=all(hashlib.sha256((root/p).read_bytes()).hexdigest()==v for p,v in receipt.items())
(out/'report.json').write_text(json.dumps({'status':'passed-development-host-uninstall' if r.returncode==0 and unchanged else 'failed','exit_code':r.returncode,'sources':receipt,'sources_unchanged':unchanged,'framework_references':identity(),'scope':'real authenticated named-pipe Host and StorageBroker; controlled Worker boundary, no desktop input; not complete G acceptance'},indent=2),encoding='utf-8')
print(log);print(out);raise SystemExit(r.returncode if unchanged else 1)
