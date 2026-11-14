"""Focused Windows native input registration checks; no UI/RC claim."""
import argparse
import hashlib
import json
import shutil
import subprocess
import uuid
from check_contracts import ROOT, DATA
from windows_framework import references


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--supervisor', type=__import__('pathlib').Path, required=True)
    args = parser.parse_args()
    out = ROOT / 'Validation/Content/sdk-runtime/native-render/task3/input-registry' / uuid.uuid4().hex[:12]
    out.mkdir(parents=True)
    for name in ('Supervisor.exe', 'Newtonsoft.Json.dll'):
        shutil.copyfile(args.supervisor / name, out / name)
    shutil.copyfile(args.supervisor / 'Supervisor.exe.config', out / 'NativeInputRegistryChecks.exe.config')
    command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'),
               '-nologo', '-langversion:latest', '-platform:x64', *references(), '-r:' + str(out / 'Supervisor.exe'), '-r:' + str(out / 'Newtonsoft.Json.dll'),
               '-out:' + str(out / 'NativeInputRegistryChecks.exe'), str(ROOT / 'tools/sdk/NativeInputRegistryChecks.cs')]
    built = subprocess.run(command, capture_output=True, text=True)
    (out / 'compile.log').write_text(built.stdout + built.stderr, encoding='utf-8')
    print(out, flush=True)
    print(built.stdout + built.stderr)
    if built.returncode:
        return built.returncode
    result = subprocess.run([str(out / 'NativeInputRegistryChecks.exe')], capture_output=True, text=True, timeout=15)
    (out / 'result.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    (out / 'report.json').write_text(json.dumps({'scope': 'native-input-registration-only', 'exit_code': result.returncode,
        'not_verified': ['actual window focus', 'ordered raw observations', 'DesktopRuntime ownership'],
        'binaries': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in out.iterdir() if p.suffix in ('.exe', '.dll')}}, indent=2), encoding='utf-8')
    print(result.stdout + result.stderr)
    return result.returncode


if __name__ == '__main__':
    raise SystemExit(main())
