"""Related native script bridge checks; never a frozen RC suite."""
import subprocess
import uuid
import argparse
import shutil
import hashlib
import json
from pathlib import Path
from check_contracts import ROOT, DATA
from check_contracts import compile_checks


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--phase', choices=('pure', 'worker', 'all'), default='all')
    parser.add_argument('--worker', type=Path)
    parser.add_argument('--supervisor', type=Path)
    parser.add_argument('--boundary-probe', type=Path)
    parser.add_argument('--negative-issued-input-assert', action='store_true', help='test harness only: fail with a live issued batch and prove cleanup')
    args = parser.parse_args()
    if args.negative_issued_input_assert and (args.phase != 'worker' or args.boundary_probe is None):
        parser.error('--negative-issued-input-assert requires worker phase and boundary probe')
    if args.phase == 'all':
        raise SystemExit('Full native scene/input/display acceptance is not wired yet; explicit --phase pure/worker runs only that partial scope')
    OUT = ROOT / 'Validation/Content/sdk-runtime/native-render/task3/checks' / uuid.uuid4().hex[:12]
    print(OUT, flush=True)
    if compile_checks(OUT, framework=args.phase == 'worker'):
        return 1
    files = [ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs',
             ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs',
             ROOT / ('tools/sdk/ScriptWorkerChecks.cs' if args.phase == 'worker' else 'tools/sdk/ScriptBridgeChecks.cs')]
    for folder in ('Execution', 'Resources', 'Native'):
        files += list((ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime' / folder).glob('*.cs'))
    extra = []
    facade = ['-r:' + str(DATA / 'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll')]
    if args.phase == 'worker':
        if args.worker is None or args.supervisor is None:
            parser.error('--phase worker requires --worker and --supervisor directories')
        from windows_framework import references
        extra += ['-platform:x64', '-r:supervisor=' + str(args.supervisor.resolve() / 'Supervisor.exe'), *references()]
        files += [ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/NativeScriptControl.cs',
                  ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/WindowsMachineClock.cs',
                  ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/Windows/PinnedProcessOwner.cs',
                  ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Native/Windows/ScriptProcessClient.cs']
        files.append(ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Native/Windows/NativeScriptSession.cs')
        facade = []
        shutil.copyfile(args.supervisor / 'Supervisor.exe', OUT / 'Supervisor.exe')
        shutil.copyfile(args.supervisor / 'Supervisor.exe.config', OUT / 'ScriptBridgeChecks.exe.config')
    command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'),
               '-nologo', '-langversion:latest', '-r:' + str(OUT / 'ContractChecks.exe'),
               '-r:' + str(OUT / 'Newtonsoft.Json.dll'), *facade,
               *extra, '-out:' + str(OUT / 'ScriptBridgeChecks.exe'), *map(str, files)]
    result = subprocess.run(command, capture_output=True, text=True)
    print(result.stdout + result.stderr)
    (OUT / 'compile.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    if result.returncode:
        return result.returncode
    from fixtures import sample, archive
    program, content = sample()
    fixture = OUT / 'native-script-input.json'
    fixture.write_text(json.dumps({'program': program, 'content': content}), encoding='utf-8')
    legacy = OUT / 'native-script-rejected-legacy.spatial'
    legacy.write_bytes(archive(program, content))
    if args.phase == 'worker':
        arguments = [str(OUT / 'ScriptBridgeChecks.exe'), str(args.worker.resolve()), str(fixture), str(OUT), str(ROOT / 'Validation/SB' / OUT.name / 'attempts')]
        arguments.append(str(args.supervisor.resolve()))
        if args.boundary_probe:
            arguments.append(str(args.boundary_probe.resolve()))
        if args.negative_issued_input_assert:
            arguments.append('--negative-issued-input-assert')
    else:
        arguments = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(OUT / 'ScriptBridgeChecks.exe'), str(fixture), str(legacy)]
    result = subprocess.run(arguments, capture_output=True, text=True, timeout=60)
    print(result.stdout + result.stderr)
    (OUT / 'result.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    (OUT / 'report.json').write_text(json.dumps({'scope': 'partial-' + args.phase + '-only', 'exit_code': result.returncode,
        'not_verified': ['native scene command application', 'actual native input focus', 'native displayed frames/performance', 'multi-target rendering'],
        'binaries': {p.name: hashlib.sha256(p.read_bytes()).hexdigest() for p in OUT.iterdir() if p.suffix in ('.exe', '.dll')}}, indent=2), encoding='utf-8')
    if args.negative_issued_input_assert:
        assert result.returncode == 1 and 'synthetic-issued-input-assertion' in result.stderr, 'wrong issued-input cleanup negative'
        bootstrap = ROOT / 'Validation/SB' / OUT.name / 'bootstrap'
        assert not bootstrap.exists() or not list(bootstrap.iterdir()), 'assertion failure leaked owned bootstrap'
        print('PASS expected live-batch assertion failure cleaned actual owned process tree and bootstrap')
        return 0
    return result.returncode


if __name__ == '__main__':
    raise SystemExit(main())
