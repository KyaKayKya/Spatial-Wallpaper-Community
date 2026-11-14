"""Compile the native-free bridge and run only its development contract checks."""
import subprocess
from check_contracts import ROOT, DATA, OUT, compile_checks


def main():
    if compile_checks():
        return 1
    sources = [ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs']
    sources += [ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs']
    for directory in ('Execution', 'Resources', 'Native'):
        sources += list((ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime' / directory).glob('*.cs'))
    command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'),
               '-nologo', '-langversion:latest', '-r:' + str(OUT / 'ContractChecks.exe'),
               '-r:' + str(OUT / 'Newtonsoft.Json.dll'), '-r:' + str(DATA / 'MonoBleedingEdge/lib/mono/4.5/Facades/netstandard.dll'),
               '-out:' + str(OUT / 'SceneCommandChecks.exe'), str(ROOT / 'tools/sdk/SceneCommandChecks.cs'), *map(str, sources)]
    result = subprocess.run(command, capture_output=True, text=True)
    print(result.stdout + result.stderr)
    if result.returncode:
        return result.returncode
    from fixtures import sample, archive
    fixture = OUT / 'scene-command-vm.spatial'
    fixture.write_bytes(archive(*sample()))
    return subprocess.call([str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(OUT / 'SceneCommandChecks.exe'), str(fixture)], timeout=30)


if __name__ == '__main__':
    raise SystemExit(main())
