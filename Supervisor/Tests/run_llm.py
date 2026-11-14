"""Run trusted LLM transport against real local HTTP fixtures, without external credentials."""
from pathlib import Path
import sys,subprocess,uuid,shutil,json,hashlib
ROOT=Path(__file__).resolve().parents[2];sys.path.insert(0,str(ROOT/'tools/sdk'))
from check_contracts import DATA
from provenance import sources
from windows_framework import references,identity
out=ROOT/'Validation/Content/sdk-runtime/E/llm'/uuid.uuid4().hex;out.mkdir(parents=True);receipt=sources()
shutil.copyfile(DATA/'Managed/Newtonsoft.Json.dll',out/'Newtonsoft.Json.dll')
files=[ROOT/'Supervisor/Tests/LlmChecks.cs',ROOT/'Supervisor/Tests/LlmLifecycleChecks.cs',ROOT/'Supervisor/Tests/LlmTransportChecks.cs',ROOT/'Supervisor/Broker/BrokerSession.cs',ROOT/'Supervisor/Broker/LlmBroker.cs']
files+=list((ROOT/'Supervisor/Core').glob('*.cs'))
files += [ROOT/'Supervisor/Tests/LlmEndpointChecks.cs',*[ROOT/'Supervisor/Broker'/name for name in ['PrivateStore.cs','StorageBroker.cs','CapabilityEndpoint.cs']],ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/CapabilityWire.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs']
files+=[ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerExitCodes.cs',ROOT/'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs']
command=[str(DATA/'MonoBleedingEdge/bin/mono.exe'),str(DATA/'MonoBleedingEdge/lib/mono/4.5/csc.exe'),'-nologo','-langversion:latest','-define:SDK_SUPERVISOR_TESTS',*references(),'-r:'+str(out/'Newtonsoft.Json.dll'),'-out:'+str(out/'Checks.exe'),*map(str,files)]
print(out,flush=True);result=subprocess.run(command,capture_output=True,text=True);(out/'compile.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);result.check_returncode()
result=subprocess.run([str(out/'Checks.exe'),str(out)],capture_output=True,text=True,timeout=30);(out/'result.log').write_text(result.stdout+result.stderr,encoding='utf-8');print(result.stdout+result.stderr);result.check_returncode()
assert sources()==receipt,'LLM test source drift'
(out/'report.json').write_text(json.dumps({'status':'partial-LLM-real-HTTP-core-only','sources':receipt,'framework':identity(),'checks_sha256':hashlib.sha256((out/'Checks.exe').read_bytes()).hexdigest()},indent=2))
