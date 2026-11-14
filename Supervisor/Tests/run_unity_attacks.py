from pathlib import Path
import argparse,subprocess,sys,uuid,shutil,json,hashlib,platform,zipfile
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
parser=argparse.ArgumentParser();parser.add_argument('--build',type=Path,required=True);args=parser.parse_args();build=args.build.resolve()
receipt=json.loads((build/'report.json').read_text());actual={p.relative_to(build/'player').as_posix():hashlib.sha256(p.read_bytes()).hexdigest() for p in (build/'player').rglob('*') if p.is_file()};assert actual==receipt['runtime_files'],'modified Worker build'
sys.path.insert(0,str(ROOT/'tools/sdk'));from provenance import sources
from fixtures import sample,ins,archive
verifier=sources();OUT=ROOT/'Validation/Content/sdk-runtime/D/unity-attacks'/uuid.uuid4().hex;OUT.mkdir(parents=True)
for name in ['healthy','bad']:
 root=OUT/name;shutil.copytree(build/'player',root);shutil.copyfile(build/'scene.spatial',root/'scene.spatial');(root/'.sdk-worker-owned').write_text('SDK generated readonly Unity attack fixture')
tests=[]
def add(name,body,expected,diagnostic,before=False,edit=None,structural_admitted=False):
 p,c=sample();p['classes'][0]['methods'][0]['body']=body
 if edit:edit(c)
 data=archive(p,c);path=OUT/(name+'.spatial');path.write_bytes(data)
 with zipfile.ZipFile(path) as z:identity=json.loads(z.read('manifest.json'))['build_id']
 tests.append({'name':name,'package':str(path),'build_id':identity,'package_sha256':hashlib.sha256(data).hexdigest(),'exit':expected,'required_log':diagnostic,'before_callbacks':before,'structural_admitted':structural_admitted})
add('infinite-loop',[ins('br',0)],21,'execution-budget')
add('recursive-update',[ins('ldarg',0),ins('invoke','Demo.Update'),ins('ret')],21,'execution-budget')
add('managed-amplification',[ins('ldc.i4',4096),ins('newarray','i32'),ins('pop'),ins('br',0)],21,'execution-budget')
add('native-texture-amplification',[ins('ldc.i4',1024),ins('ldc.i4',1024),ins('call','SpatialWallpaper.Sdk::Resources::CreateTexture(System.Int32,System.Int32)->Texture'),ins('pop'),ins('br',0)],21,'resource-reservation-denied')
add('forbidden-member',[ins('call','System.IO::ReadAllText'),ins('ret')],20,'member-not-allowed',True)
add('unknown-config',[ins('ret')],20,'unexpected-or-missing-fields',True,lambda c:c['nodes'][0]['components'][0]['config'].update(secret=True))
add('unbound-capability',[ins('ldstr','test'),ins('call','SpatialWallpaper.Sdk::Llm::BeginPrompt(System.String)->System.Int32'),ins('pop'),ins('ret')],20,'missing-host-binding',True,structural_admitted=True)
damaged=archive(*sample())[:-22];damaged_path=OUT/'truncated-zip.spatial';damaged_path.write_bytes(damaged);damaged_hash=hashlib.sha256(damaged).hexdigest()
tests.append({'name':'truncated-zip','package':str(damaged_path),'build_id':damaged_hash,'package_sha256':damaged_hash,'exit':20,'required_log':'InvalidDataException','before_callbacks':True,'structural_admitted':False})
(OUT/'cases.json').write_text(json.dumps(tests,indent=2))
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',OUT/'Newtonsoft.Json.dll')
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-main:UnityAttackChecks','-r:'+str(OUT/'Newtonsoft.Json.dll'),'-r:'+str(DATA/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-out:'+str(OUT/'Checks.exe'),str(ROOT/'Supervisor/Tests/UnityAttackChecks.cs'),str(ROOT/'Supervisor/Tests/UnityIsolationChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),*map(str,(ROOT/'Supervisor/Windows').glob('*.cs')),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs'),*map(str,(ROOT/'Supervisor/Core').glob('*.cs'))]
r=subprocess.run(cmd,capture_output=True,text=True);print(r.stdout+r.stderr);print(OUT,flush=True)
if r.returncode:raise SystemExit(r.returncode)
with zipfile.ZipFile(build/'scene.spatial') as package:identity=json.loads(package.read('manifest.json'))['build_id']
subprocess.run([str(OUT/'Checks.exe'),str(OUT/'healthy'),str(OUT/'bad'),str(OUT),identity,str(OUT/'cases.json')],check=True,timeout=150)
assert sources()==verifier,'verification sources changed'
report={'status':'Unity-VM-attack-fixture-only','platform':platform.platform(),'worker_build':receipt,'verifier_sources':verifier,'attack_packages':tests,'results':json.loads((OUT/'results.json').read_text())}
(OUT/'report.json').write_text(json.dumps(report,indent=2));(ROOT/'Validation/Content/sdk-runtime/reports/stage-D-unity-attacks-partial.json').write_text(json.dumps(report,indent=2))
