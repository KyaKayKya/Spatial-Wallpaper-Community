"""Core native capability checks; no AppContainer or user input automation."""
import json
import subprocess
import uuid
from check_contracts import ROOT, DATA
from windows_framework import references

def main():
    out = ROOT / 'Validation/Content/sdk-runtime/native-capabilities/core' / uuid.uuid4().hex[:12]
    out.mkdir(parents=True)
    sources = [ROOT / 'tools/sdk/NativeCapabilitiesCoreChecks.cs',
               ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs',
               ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs']
    for folder in ('Data', 'Security', 'Execution', 'Resources', 'Native', 'Capabilities', 'Capabilities/Client'):
        sources += list((ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime' / folder).glob('*.cs'))
    sources += [ROOT / ('Supervisor/' + name) for name in ('Core/FaultJournal.cs', 'Broker/BrokerSession.cs', 'Broker/PrivateStore.cs', 'Broker/StorageBroker.cs')]
    import shutil
    shutil.copyfile(DATA / 'Managed/Newtonsoft.Json.dll', out / 'Newtonsoft.Json.dll')
    command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'),
               '-nologo', '-langversion:latest', '-platform:x64', '-define:SDK_SUPERVISOR_TESTS',
               '-r:' + str(out / 'Newtonsoft.Json.dll'), *references(),
               '-out:' + str(out / 'NativeCapabilitiesCoreChecks.exe'), *map(str, sources)]
    result = subprocess.run(command, capture_output=True, text=True)
    (out / 'compile.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    print(result.stdout + result.stderr)
    if result.returncode:
        return result.returncode
    result = subprocess.run([str(out / 'NativeCapabilitiesCoreChecks.exe'), str(out / 'run')], capture_output=True, text=True, timeout=30)
    (out / 'result.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    (out / 'report.json').write_text(json.dumps({'exit_code': result.returncode, 'scope': 'native-view-wire-and-shared-persistent-storage-core-no-LPAC'}, indent=2), encoding='utf-8')
    print(result.stdout + result.stderr)
    print(out)
    return result.returncode

if __name__ == '__main__':
    raise SystemExit(main())
