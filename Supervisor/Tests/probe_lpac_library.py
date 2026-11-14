from pathlib import Path
import sys,shutil,subprocess,uuid,json
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk/experiments'))
from native_build import compile_probe
from windows_sandbox import launch
base=ROOT/'Validation/Content/sdk-runtime/D/library'/uuid.uuid4().hex;base.mkdir(parents=True)
compile_probe(ROOT/'Supervisor/Tests/LibraryBoundaryProbe.cpp',base/'Probe.exe')
report={}
for mode in ['full','appcontainer','lpac','lpac-directory','lpac-registry']:
 root=base/mode;root.mkdir();shutil.copyfile(base/'Probe.exe',root/'Probe.exe')
 dll=root/'runtime/mono-2.0-bdwgc.dll';dll.parent.mkdir();shutil.copyfile('D:/Unity/Editor/2022.3.62f3/Editor/Data/MonoBleedingEdge/EmbedRuntime/mono-2.0-bdwgc.dll',dll)
 output=root/'output';output.mkdir()
 def prepare(sid,directory):
  if mode=='lpac-directory':subprocess.run(['icacls',str(dll.parent),'/grant','*S-1-15-2-2:(OI)(CI)(RX)','/T','/Q'],check=True,capture_output=True)
  return []
 args=[str(output/'result.json'),str(dll)]
 if mode=='full':code=subprocess.run([str(root/'Probe.exe'),*args],timeout=15).returncode
 else:code,_=launch(root/'Probe.exe',args,root,lpac=mode.startswith('lpac'),prepare=prepare,timeout=15,capability_names=['registryRead'] if mode=='lpac-registry' else [])
 report[mode]={'exit_code':code,'result':json.loads((output/'result.json').read_text()) if (output/'result.json').exists() else None}
print(json.dumps(report,indent=2));(base/'report.json').write_text(json.dumps(report,indent=2));print(base)
