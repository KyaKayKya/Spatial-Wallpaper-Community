"""Minimal actual LPAC native View/private-storage integration check."""
import argparse
import json
import shutil
import subprocess
import uuid
from check_contracts import ROOT, DATA, compile_checks
from windows_framework import references
from fixtures import sample

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--worker', required=True)
    parser.add_argument('--supervisor', required=True)
    args = parser.parse_args()
    out = ROOT / 'Validation/Content/sdk-runtime/native-capabilities/checks' / uuid.uuid4().hex[:12]
    if compile_checks(out, framework=True):
        return 1
    supervisor = ROOT / args.supervisor
    shutil.copyfile(supervisor / 'Supervisor.exe', out / 'Supervisor.exe')
    shutil.copyfile(supervisor / 'Supervisor.exe.config', out / 'NativeCapabilitiesChecks.exe.config')
    sources = [ROOT / 'tools/sdk/NativeCapabilitiesChecks.cs',
               ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs',
               ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs']
    for folder in ('Execution', 'Resources', 'Native'):
        sources += list((ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime' / folder).glob('*.cs'))
    command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'),
               '-nologo', '-langversion:latest', '-platform:x64', '-r:' + str(out / 'ContractChecks.exe'),
               '-r:' + str(out / 'Newtonsoft.Json.dll'), '-r:supervisor=' + str(out / 'Supervisor.exe'),
               *references(), '-out:' + str(out / 'NativeCapabilitiesChecks.exe'), *map(str, sources)]
    result = subprocess.run(command, capture_output=True, text=True)
    (out / 'compile.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    print(result.stdout + result.stderr)
    if result.returncode:
        return result.returncode
    program, content = sample()
    fixture = out / 'seed.json'
    fixture.write_text(json.dumps({'program': program, 'content': content}), encoding='utf-8')
    result = subprocess.run([str(out / 'NativeCapabilitiesChecks.exe'), str((ROOT / args.worker).resolve()), str(fixture), str(out / 'run')], capture_output=True, text=True, timeout=60)
    (out / 'result.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    print(result.stdout + result.stderr)
    print(out)
    return result.returncode

if __name__ == '__main__':
    raise SystemExit(main())
