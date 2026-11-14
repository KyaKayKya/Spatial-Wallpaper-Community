from pathlib import Path
import sys,subprocess,uuid,shutil,json,zipfile,io,hashlib
ROOT=Path(__file__).resolve().parents[2];DATA=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data')
sys.path.insert(0,str(ROOT/'tools/sdk'))
from fixtures import sample,archive,ins,node,component
from provenance import sources
from windows_framework import references,identity
sys.path.insert(0,str(ROOT/'tools/sdk/experiments'));from native_build import compile_probe
out=ROOT/'Validation/Content/sdk-runtime/E/validator'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources()
p,c=sample();p['classes'][0]['methods'].append({'id':'Unused','returns':'void','parameters':[],'locals':[],'body':[ins('ldstr','Jump'),ins('call','SpatialWallpaper.Sdk::Input::GetButtonDown(System.String)->System.Boolean'),ins('pop'),ins('ldc.i4',0),ins('call','SpatialWallpaper.Sdk::Audio::GetBand(System.Int32)->System.Single'),ins('pop'),ins('ldstr','save'),ins('call','SpatialWallpaper.Sdk::Storage::Read(System.String)->System.String'),ins('pop'),ins('ldstr','prompt'),ins('call','SpatialWallpaper.Sdk::Llm::Submit(System.String)->System.Int32'),ins('pop'),ins('ret')]})
prefab=node();prefab['components'].append(component('mesh-renderer',{'mesh':None,'material':None}));c['prefabs']=[{'id':'Uninstantiated','nodes':[prefab]}]
data=archive(p,c);(out/'positive.spatial').write_bytes(data);(out/'malicious.spatial').write_bytes(b'bad archive')
with zipfile.ZipFile(io.BytesIO(data)) as z:build=json.loads(z.read('manifest.json'))['build_id']
subprocess.run([sys.executable,str(ROOT/'tools/sdk/build_validator.py'),'--output',str(out/'distribution')],check=True)
ordinary=subprocess.run([str(out/'distribution/Validator.exe'),str(out/'positive.spatial'),'0','not-used','not-used'],capture_output=True,timeout=10)
ordinary_report=json.loads(ordinary.stdout);assert ordinary.returncode==10 and ordinary_report['accepted'] is False and 'requires-AppContainer' in ordinary_report['error']
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
cmd=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),*references(),'-out:'+str(out/'Checks.exe'),str(ROOT/'Supervisor/Tests/ValidatorChecks.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs'),str(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs'),*map(str,(ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities').glob('*.cs')),*map(str,(ROOT/'Supervisor/Broker').glob('*.cs')),*map(str,(ROOT/'Supervisor/Core').glob('*.cs')),*map(str,(ROOT/'Supervisor/Windows').glob('*.cs')),*map(str,(ROOT/'Supervisor/Hosting').glob('*.cs'))]
subprocess.run(cmd,check=True)
fault=out/'fault-distribution';fault.mkdir();compile_probe(ROOT/'Supervisor/Tests/ValidatorFaultProbe.cpp',fault/'Validator.exe')
(out/'hang.spatial').write_bytes(b'H');(out/'oversize.spatial').write_bytes(b'O')
attempts=ROOT/'Validation/Sdk'/('v'+out.name[:10]);attempts.mkdir(parents=True)
subprocess.run([str(out/'Checks.exe'),str(out/'distribution'),str(out/'distribution/build.json'),str(attempts),str(out/'positive.spatial'),build,str(out/'malicious.spatial'),str(fault),str(out/'hang.spatial'),str(out/'oversize.spatial'),str(out/'results.json')],check=True,timeout=40)
assert sources()==receipt,'Validator sources changed'
(out/'report.json').write_text(json.dumps({'status':'E2-restricted-Validator-only','framework_references':identity(),'sources':receipt,'distribution':json.loads((out/'distribution/build.json').read_text()),'checks_sha256':hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest(),'fault_probe_sha256':hashlib.sha256((fault/'Validator.exe').read_bytes()).hexdigest(),'full_trust_entry_refused':ordinary_report,'observations':json.loads((out/'results.json').read_text()),'build_id':build,'expected_capabilities':['audio-features','input','llm-proxy','private-storage','view-data']},indent=2));print(out)
