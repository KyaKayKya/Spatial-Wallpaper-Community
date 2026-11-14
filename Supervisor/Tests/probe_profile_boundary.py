from pathlib import Path
import sys,subprocess,uuid,json,ctypes as c,hashlib
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from native_build import compile_probe
from windows_sandbox import launch,e,P,w,bind
sys.path.insert(0,str(ROOT/'tools/sdk'));from provenance import sources
receipt=sources()
OUT=ROOT/'Validation/Content/sdk-runtime/D/profile-boundary'/uuid.uuid4().hex;OUT.mkdir(parents=True);root=OUT/'worker';root.mkdir()
compile_probe(ROOT/'Supervisor/Tests/ProfileBoundaryProbe.cpp',root/'Probe.exe')
profile={}
bind(e,'GetAppContainerFolderPath',[w.LPCWSTR,c.POINTER(P)],c.c_long)
ole=c.WinDLL('ole32');bind(ole,'CoTaskMemFree',[P],None)
args=[str(root/'output/result.json'),'placeholder']
def prepare(sid,output):
 text=P();hr=e.GetAppContainerFolderPath(sid,c.byref(text))
 if hr<0:raise RuntimeError('GetAppContainerFolderPath '+hex(hr&0xffffffff))
 try:path=Path(c.wstring_at(text))
 finally:ole.CoTaskMemFree(text)
 profile['folder_suffix']=path.name;args[1]=str(path/'sdk-synthetic-canary.txt')
 return []
code,sid=launch(root/'Probe.exe',args,root,lpac=True,capability_names=['registryRead'],image_mitigations=(1<<52)|(1<<56),prepare=prepare)
report={'exit_code':code,'result':json.loads((root/'output/result.json').read_text()),'sources':receipt,'probe_sha256':hashlib.sha256((root/'Probe.exe').read_bytes()).hexdigest(),**profile}
assert code==0 and all(report['result'][name]==0 for name in ['profile_file_write','private_registry_write','private_registry_create','private_registry_acl']),report
assert sources()==receipt,'sources changed during control verification'
(OUT/'report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report['result'],indent=2));print(OUT)
