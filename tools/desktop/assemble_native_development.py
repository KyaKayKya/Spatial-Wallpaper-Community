"""Add recorded legacy SDK and original Tracker dependencies to Native-Owner.

Development only: this does not align the new SDK/five packages or certify RC.
The installed Python interpreter configured by Launch remains an external dependency.
Existing dependency directories are never replaced.
"""
from pathlib import Path
import hashlib,json,shutil,sys
ROOT=Path(__file__).resolve().parents[2]
sys.path.insert(0,str(ROOT/'tools/sdk'))
from verify_delivery import verify_inventory

def digest(p):return hashlib.sha256(p.read_bytes()).hexdigest()
def inventory(p):return {x.relative_to(p).as_posix():digest(x) for x in p.rglob('*') if x.is_file()}
def main():
 target=ROOT/'Prototype/Builds/Native-Owner';baseline=ROOT/'Prototype/Builds/Native-Baseline'
 evidence=ROOT/'Validation/Content/sdk-runtime/native-render/task3'
 build=evidence/'unity-desktop-native-tracker/result.json';b=json.loads(build.read_text())
 assert b['exit_code']==0 and b['sources_unchanged']
 for name,sha in b['source_sha256'].items():assert digest(ROOT/name)==sha,name
 for name,sha in b['files_sha256'].items():assert digest(target/name)==sha,name
 for name in ['sdk-runtime','Tracker']:assert not (target/name).exists(),'preserve existing dependency: '+name
 receipt=evidence/'native-development-package.json';assert not receipt.exists()
 runtime=baseline/'sdk-runtime';manifest=json.loads((runtime/'runtime.json').read_text())
 verify_inventory(runtime,manifest['files'],exclude=['runtime.json'])
 tracker_source=ROOT/'tracker';inputs={}
 for folder in ['src']:
  for p in (tracker_source/folder).rglob('*.py'):inputs[p.relative_to(tracker_source).as_posix()]=p
 for name in ['pyproject.toml','uv.lock']:inputs[name]=tracker_source/name
 for p in (baseline/'Tracker/.cache/models').iterdir():
  if p.is_file():inputs['.cache/models/'+p.name]=p
 assert {'.cache/models/face_landmarker.task','.cache/models/canonical_face_model.obj'}<=inputs.keys()
 tracker_hashes={name:digest(p) for name,p in inputs.items()}
 # Fresh owned dependency roots only; never mutate the baseline or local assets.
 shutil.copytree(runtime,target/'sdk-runtime')
 for name,source in inputs.items():
  out=target/'Tracker'/name;out.parent.mkdir(parents=True,exist_ok=True);shutil.copy2(source,out)
 verify_inventory(target/'sdk-runtime',manifest['files'],exclude=['runtime.json'])
 assert inventory(target/'Tracker')==tracker_hashes
 for name,source in inputs.items():assert digest(source)==tracker_hashes[name]
 data={'status':'assembled-native-development-dependencies-not-SDK-release-not-RC','implementation_commit':'98bd9db7b7802f74b04a42e364280e2ec34ac721','player_receipt':{'path':build.relative_to(ROOT).as_posix(),'sha256':digest(build)},'legacy_sdk_runtime_manifest_sha256':digest(runtime/'runtime.json'),'tracker_inputs':{name:{'source':str(p),'sha256':tracker_hashes[name]} for name,p in inputs.items()},'external_dependency':'Installed Python interpreter from Launch.exe.config; this package does not embed Python.','files':inventory(target),'not_verified':['default dependency Launch execution','GUI full flow','new SDK/runtime/five package aligned release','images/performance/multi-display','RC']}
 receipt.write_text(json.dumps(data,ensure_ascii=False,indent=2),encoding='utf-8')
 print('assembled',len(data['files']),'files; receipt SHA256',digest(receipt))
if __name__=='__main__':main()
