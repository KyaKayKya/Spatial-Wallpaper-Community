from pathlib import Path
import subprocess,shutil,sys,argparse,json,hashlib
import uuid
root=Path(__file__).resolve().parents[2]
parser=argparse.ArgumentParser();parser.add_argument('--output',type=Path);parser.add_argument('--minimal-fixture',action='store_true');parser.add_argument('--long-state-path',action='store_true');args=parser.parse_args();out=args.output.resolve() if args.output else root/'Validation/Content/p3'
assert out.is_relative_to(root/'Validation/Content'),'store evidence must remain under this workspace Validation/Content';out.mkdir(parents=True,exist_ok=True)
package=out/'static-validation/sample.spatial'
if args.minimal_fixture:
 from store_fixture import create
 create(package)
sys.path.insert(0,str(root/'tools/sdk'));from provenance import sources
receipt=sources();receipt.update({p.relative_to(root).as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in [Path(__file__),Path(__file__).with_name('store_fixture.py'),Path(__file__).with_name('StoreChecks.cs')]})
run_directory=root/'Validation/Sdk'/('l'+uuid.uuid4().hex[:10]) if args.minimal_fixture else out/'runs'/str(uuid.uuid4())
if args.long_state_path:
 assert args.minimal_fixture,'long-path regression uses the original minimal schema2 fixture'
 parent=root/'Validation/Content/long-store'/uuid.uuid4().hex
 run_directory=parent/('x'*(155-len(str(parent))-1))
 assert len(str(run_directory))==155 and not run_directory.exists()
data=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data');mono=data/'MonoBleedingEdge/bin/mono.exe'
shutil.copy2(data/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
command=[str(mono),str(data/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),'-r:'+str(data/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-r:System.IO.Compression.dll','-r:System.IO.Compression.FileSystem.dll','-out:'+str(out/'StoreChecks.exe'),str(root/'tools/content/StoreChecks.cs')]
command+=list(map(str,(root/'Packages/com.spatialwallpaper.sdk/Runtime/Data').glob('*.cs')))+list(map(str,(root/'Prototype/Assets/ContentRuntime/Core').glob('*.cs')))
command.append(str(root/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimePackageInspection.cs'))
result=subprocess.run(command,capture_output=True,text=True);text=result.stdout+result.stderr
if result.returncode==0:
 import uuid
 result=subprocess.run([str(mono),str(out/'StoreChecks.exe'),str(run_directory),str(out/'static-validation/sample.spatial')],capture_output=True,text=True);text+=result.stdout+result.stderr
print(text);(out/'reports').mkdir(exist_ok=True);(out/'reports/store.txt').write_text(text,encoding='utf-8')
(out/'report.json').write_text(json.dumps({'status':'passed-existing-library-development-regression' if result.returncode==0 else 'failed','exit_code':result.returncode,'sources':receipt,'test_state_path':str(run_directory),'test_state_path_chars':len(str(run_directory)),'long_state_path':args.long_state_path,'binary_sha256':hashlib.sha256((out/'StoreChecks.exe').read_bytes()).hexdigest() if (out/'StoreChecks.exe').exists() else None,'fixture_sha256':hashlib.sha256(package.read_bytes()).hexdigest() if package.exists() else None,'scope':'schema2 library/transaction compatibility with original minimal data; '+('155-character state root, final build below MAX_PATH and old hash-plus-UUID temporary path above MAX_PATH' if args.long_state_path else 'short owned library path')+'; not arbitrary long-path support, scene fidelity or sandbox acceptance'},indent=2));raise SystemExit(result.returncode)
