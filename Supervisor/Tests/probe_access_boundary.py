
from pathlib import Path
import ctypes as c,json,shutil,subprocess,sys,uuid,winreg
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from native_build import compile_probe
from windows_sandbox import launch
base=ROOT/'Validation/Content/sdk-runtime/D/access'/uuid.uuid4().hex;base.mkdir(parents=True)
compile_probe(ROOT/'Supervisor/Tests/AccessBoundaryProbe.cpp',base/'Probe.exe')
compile_probe(ROOT/'Supervisor/Tests/BoundaryCanary.cpp',base/'Canary.dll',library=True)
private=base/'private';private.mkdir()
file=private/'private.txt';file.write_text('SDK synthetic file canary')
apps=private/'all-apps.txt';apps.write_text('SDK synthetic broad-package file')
external=private/'external.dll';shutil.copyfile(base/'Canary.dll',external)
for p in [apps,external]:subprocess.run(['icacls',str(p),'/grant','*S-1-15-2-1:(RX)','/Q'],check=True,capture_output=True)
keyname='Software\\SpatialWallpaperSdkCanary\\'+uuid.uuid4().hex
protectedname=keyname+'Protected'
for name in [keyname,protectedname]:
 with winreg.CreateKey(winreg.HKEY_CURRENT_USER,name) as key:winreg.SetValueEx(key,'Fake',0,winreg.REG_SZ,'SDK synthetic registry canary')
# Explicit user-only protected ACL, applied only to this generated canary key.
from windows_sandbox import a,k,P,w,bind
caller=subprocess.check_output(['whoami','/user','/fo','csv','/nh'],text=True).split(',')[1].strip().strip('"')
sd=P();dacl=P();present=w.BOOL();defaulted=w.BOOL()
bind(a,'GetSecurityDescriptorDacl',[P,c.POINTER(w.BOOL),c.POINTER(P),c.POINTER(w.BOOL)])
bind(a,'SetSecurityInfo',[P,w.DWORD,w.DWORD,P,P,P,P],w.DWORD)

assert a.ConvertStringSecurityDescriptorToSecurityDescriptorW('D:P(A;;KA;;;'+caller+')',1,c.byref(sd),None)
assert a.GetSecurityDescriptorDacl(sd,c.byref(present),c.byref(dacl),c.byref(defaulted))
with winreg.OpenKey(winreg.HKEY_CURRENT_USER,protectedname,0,winreg.KEY_ALL_ACCESS) as key:
 error=a.SetSecurityInfo(P(int(key)),4,0x80000004,None,None,dacl,None)
 assert error==0,error
k.LocalFree(sd)
target='SpatialWallpaperSdkCanary-'+uuid.uuid4().hex
subprocess.run([str(base/'Probe.exe'),'--seed',target],check=True)
report={}
try:
 for mode in ['full','appcontainer','lpac-registry','lpac-registry-image-policy']:
  root=base/mode;root.mkdir();shutil.copyfile(base/'Probe.exe',root/'Probe.exe')
  trusted=root/'trusted.dll';shutil.copyfile(base/'Canary.dll',trusted)
  out=root/'output';out.mkdir();scratch=out/'scratch.dll';shutil.copyfile(base/'Canary.dll',scratch)
  def prepare(sid,directory):
   subprocess.run(['icacls',str(scratch),'/grant','*S-1-15-2-2:(RX)','*S-1-15-2-1:(RX)','/Q'],check=True,capture_output=True)
   return []
  args=[str(out/'result.json'),str(file),str(apps),keyname,protectedname,str(external),str(scratch),str(trusted),target]
  if mode=='full':code=subprocess.run([str(root/'Probe.exe'),*args],timeout=15).returncode
  else:code,_=launch(root/'Probe.exe',args,root,lpac=mode.startswith('lpac'),prepare=prepare,capability_names=['registryRead'] if mode.startswith('lpac') else [],image_mitigations=(1<<52)|(1<<56) if mode.endswith('policy') else 0)
  report[mode]={'exit_code':code,'result':json.loads((out/'result.json').read_text()) if (out/'result.json').exists() else None}
finally:
 subprocess.run([str(base/'Probe.exe'),'--cleanup',target],check=True)
 for name in [keyname,protectedname]:winreg.DeleteKey(winreg.HKEY_CURRENT_USER,name)
for mode,value in report.items():
 assert value['exit_code']==0 and value['result'] is not None,(mode,value)
full=report['full']['result']
assert all(v==0 for v in full.values()),full
assert report['appcontainer']['result']['all_apps_file']==0
assert report['appcontainer']['result']['external_dll']==0
for mode in ['lpac-registry','lpac-registry-image-policy']:
 value=report[mode]['result']
 for name in ['private_file','all_apps_file','inherited_private_registry','protected_private_registry','external_dll','private_credential']:assert value[name]!=0,(mode,name,value)
 assert value['operational_system_registry']==0 and value['trusted_dll']==0,value
assert report['lpac-registry']['result']['scratch_dll']==0
assert report['lpac-registry-image-policy']['result']['scratch_dll']!=0
print(json.dumps(report,indent=2));(base/'report.json').write_text(json.dumps(report,indent=2));print(base)
