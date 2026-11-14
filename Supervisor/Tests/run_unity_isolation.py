from pathlib import Path
import argparse,subprocess,sys,uuid,shutil,json,hashlib,platform
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
parser=argparse.ArgumentParser();parser.add_argument('--build',type=Path,required=True);args=parser.parse_args();build=args.build.resolve()
receipt=json.loads((build/'report.json').read_text());actual={p.relative_to(build/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (build/'player').rglob('*') if p.is_file()};assert actual==receipt['runtime_files'],'modified Worker build'
sys.path.insert(0,str(ROOT/'tools/sdk'));from provenance import sources
verifier=sources();OUT=ROOT/'Validation/Content/sdk-runtime/D/unity-isolation'/uuid.uuid4().hex;OUT.mkdir(parents=True)
for name in ['healthy','bad']:
 root=OUT/name;shutil.copytree(build/'player',root);shutil.copyfile(build/'scene.spatial',root/'scene.spatial');(root/'.sdk-worker-owned').write_text('SDK generated readonly Unity isolation fixture')
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',OUT/'Newtonsoft.Json.dll')
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(OUT/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(OUT/'Checks.exe'),str(ROOT/'Supervisor/Tests/UnityIsolationChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),*map(str,(ROOT/'Supervisor/Windows').glob('*.cs')),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs'),*map(str,(ROOT/'Supervisor/Core').glob('*.cs'))]
r=subprocess.run(cmd,capture_output=True,text=True);print(r.stdout+r.stderr);print(OUT)
if r.returncode:raise SystemExit(r.returncode)
with __import__('zipfile').ZipFile(build/'scene.spatial') as package:identity=json.loads(package.read('manifest.json'))['build_id']
subprocess.run([str(OUT/'Checks.exe'),str(OUT/'healthy'),str(OUT/'bad'),str(OUT),identity],check=True,timeout=100)
assert sources()==verifier,'verification sources changed'
report={'status':'Unity-GPU-isolation-fixture-only','platform':platform.platform(),'worker_build':receipt,'verifier_sources':verifier,'results':json.loads((OUT/'results.json').read_text())}
(OUT/'report.json').write_text(json.dumps(report,indent=2));(ROOT/'Validation/Content/sdk-runtime/reports/stage-D-unity-isolation-partial.json').write_text(json.dumps(report,indent=2))
