"""Bounded integration settings/backup checks; no Worker or audio-kernel matrix."""
from pathlib import Path
import subprocess,sys,shutil,uuid,json
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/content'))
from store_fixture import create
out=ROOT/'Validation/Content/sdk-runtime/main-manager/service-checks'/uuid.uuid4().hex[:12];out.mkdir(parents=True);create(out/'fixture.spatial')
data=Path('D:/Unity/Editor/2022.3.62f3/Editor/Data');mono=data/'MonoBleedingEdge/bin/mono.exe';sdk=ROOT/'Packages/com.spatialwallpaper.sdk/Runtime'
shutil.copy2(data/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
files=[ROOT/'tools/library/MainManagerServiceChecks.cs',ROOT/'Prototype/Assets/ContentRuntime/Runtime/ContentLibraryService.cs',*list((ROOT/'Prototype/Assets/ContentRuntime/Core').glob('*.cs')),*list((sdk/'Data').glob('*.cs')),sdk/'Hosting/RuntimePackageInspection.cs',sdk/'Capabilities/SoundMix.cs']
compile=subprocess.run([str(mono),str(data/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-r:'+str(out/'Newtonsoft.Json.dll'),'-r:'+str(data/'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),'-r:System.IO.Compression.dll','-r:System.IO.Compression.FileSystem.dll','-out:'+str(out/'Checks.exe'),*map(str,files)],capture_output=True,text=True)
log=compile.stdout+compile.stderr;result=compile
if result.returncode==0:
 result=subprocess.run([str(mono),str(out/'Checks.exe'),str(out/'store'),str(out/'fixture.spatial')],capture_output=True,text=True);log+=result.stdout+result.stderr
(out/'checks.log').write_text(log,encoding='utf-8');(out/'report.json').write_text(json.dumps({'status':'passed' if result.returncode==0 else 'failed','scope':__doc__,'exit_code':result.returncode},indent=2));print(log);print(out);raise SystemExit(result.returncode)
