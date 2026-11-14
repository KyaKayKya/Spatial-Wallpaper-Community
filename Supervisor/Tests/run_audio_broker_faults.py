"""Real private-station process faults against the production audio broker."""
from pathlib import Path
import sys,subprocess,uuid,json,hashlib
root=Path(__file__).resolve().parents[2];sys.path.insert(0,str(root/'tools/sdk'));sys.path.insert(0,str(root/'tools/sdk/experiments'))
from check_contracts import DATA
from windows_framework import references
from native_build import compile_probe
out=root/'Validation/Content/sdk-runtime/E/audio-broker-faults'/uuid.uuid4().hex;out.mkdir(parents=True);print(out,flush=True)
files=[root/p for p in ['Supervisor/Tests/AudioBrokerFaultChecks.cs','Supervisor/Broker/AudioBroker.cs','Supervisor/Windows/TrustedAudioProcess.cs','Supervisor/Windows/Native.cs','Supervisor/Windows/RestrictedProcess.cs']]
subprocess.run([str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest',*references(),'-out:'+str(out/'Checks.exe'),*map(str,files)],check=True)
source=root/'Supervisor/Tests/AudioFaultHelper.cpp';compile_probe(source,out/'FaultHelper.exe')
result=subprocess.run([str(out/'Checks.exe'),str(out/'FaultHelper.exe')],capture_output=True,text=True,timeout=25)
(out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr)
paths=[*files,source,out/'Checks.exe',out/'FaultHelper.exe']
(out/'report.json').write_text(json.dumps({'exitCode':result.returncode,'sha256':{str(p.relative_to(root)):hashlib.sha256(p.read_bytes()).hexdigest() for p in paths}},indent=2))
result.check_returncode()
