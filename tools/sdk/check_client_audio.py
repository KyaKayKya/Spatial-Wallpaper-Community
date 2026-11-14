"""Actual original client audio rebind/clear and unchanged revision across modern/legacy."""
from pathlib import Path
import argparse,hashlib,json,shutil,subprocess,sys,uuid
from check_contracts import ROOT,DATA
from windows_framework import references
from toolchain_process import run_toolchain
from provenance import sources
sys.path.insert(0,str(ROOT/'Supervisor/Tests'));from run_production import packages
sys.path.insert(0,str(ROOT/'tools/content'));from store_fixture import create as legacy_fixture
p=argparse.ArgumentParser();p.add_argument('--client',type=Path,required=True);p.add_argument('--runtime',type=Path,required=True);args=p.parse_args()
out=ROOT/'Validation/Content/sdk-runtime/G/client-audio'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True);report={'status':'failed','sources':sources()}
try:
 packages(out);legacy_fixture(out/'legacy.spatial');shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll');shutil.copyfile(ROOT/'Supervisor/Supervisor.exe.config',out/'Checks.exe.config')
 names=['Supervisor/Tests/ContentAudioSelectionChecks.cs','Prototype/Assets/ContentRuntime/Runtime/ContentHostClient.cs','Prototype/Assets/Tracking/Core/TrackingWire.cs','Prototype/Assets/Tracking/Core/RuntimeLease.cs','Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PinnedProcessOwner.cs','Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/TrustedAudioSource.cs']
 done=subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*[str(ROOT/name) for name in names]],capture_output=True,text=True);(out/'compile.log').write_text(done.stdout+done.stderr);done.check_returncode();report['process']={}
 with (out/'checks.log').open('w') as log:run_toolchain([out/'Checks.exe',out,args.client.resolve()/'player/ContentPlayer.exe',args.runtime.resolve(),ROOT/'Validation/Sdk'/('ga'+out.name[:10])],ROOT,log,180,report['process'])
 report.update(status='passed-development-original-client-audio-rebind',result=json.loads((out/'result.json').read_text()))
finally:
 report['sources_unchanged']=sources()==report['sources'];(out/'report.json').write_text(json.dumps(report,indent=2));print(out,flush=True)
