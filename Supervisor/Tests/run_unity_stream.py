from pathlib import Path
import subprocess,uuid,sys,shutil,json,platform,hashlib,argparse
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
parser=argparse.ArgumentParser();parser.add_argument('--build',type=Path,required=True);parser.add_argument('--baseline',type=Path,required=True);args=parser.parse_args();build=args.build.resolve()
receipt=json.loads((build/'report.json').read_text())
actual_files={p.relative_to(build/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (build/'player').rglob('*') if p.is_file()}
assert actual_files==receipt['runtime_files'],'Worker binary inventory differs from build receipt'
sys.path.insert(0,str(ROOT/'tools/sdk'))
from provenance import sources
verifier_receipt=sources()
OUT=ROOT/'Validation/Content/sdk-runtime/D/unity-stream'/uuid.uuid4().hex;OUT.mkdir(parents=True);runtime=OUT/'runtime';shutil.copytree(build/'player',runtime)
shutil.copyfile(build/'scene.spatial',runtime/'scene.spatial');(runtime/'.sdk-worker-owned').write_text('SDK generated readonly Unity fixture')
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',OUT/'Newtonsoft.Json.dll')
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(OUT/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(OUT/'Checks.exe'),str(ROOT/'Supervisor/Tests/UnityStreamChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),*map(str,(ROOT/'Supervisor/Windows').glob('*.cs')),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs'),*map(str,(ROOT/'Supervisor/Core').glob('*.cs'))]
r=subprocess.run(cmd,capture_output=True,text=True);print(r.stdout+r.stderr)
if r.returncode:raise SystemExit(r.returncode)
r=subprocess.run([str(OUT/'Checks.exe'),str(runtime),str(OUT)],timeout=45)
print(OUT)
if r.returncode:raise SystemExit(r.returncode)
result=json.loads((OUT/'result.json').read_text())
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-r:C:/Windows/Microsoft.NET/Framework64/v4.0.30319/System.Drawing.dll','-r:'+str(OUT/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(OUT/'Compare.exe'),str(ROOT/'Supervisor/Tests/CompareFrameChecks.cs')]
subprocess.run(cmd,check=True,capture_output=True,text=True)
subprocess.run([str(OUT/'Compare.exe'),str(args.baseline.resolve()),str(OUT/'last-frame.rgba'),str(OUT/'frame.png'),str(OUT/'comparison.json')],check=True,timeout=30)
comparison=json.loads((OUT/'comparison.json').read_text())
assert sources()==verifier_receipt,'source changed during Unity stream verification'
report={'status':'readonly-LPAC-Unity-pixel-fixture-only','platform':platform.platform(),'worker_build':receipt,'result':result,'comparison':comparison,'verifier_sources':verifier_receipt,'baseline_sha256':hashlib.sha256(args.baseline.read_bytes()).hexdigest(),'frame_sha256':hashlib.sha256((OUT/'last-frame.rgba').read_bytes()).hexdigest()}
(OUT/'report.json').write_text(json.dumps(report,indent=2));(ROOT/'Validation/Content/sdk-runtime/reports/stage-D-stream-partial.json').write_text(json.dumps(report,indent=2));print(json.dumps(result,indent=2))
