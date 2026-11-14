"""Build the script-only CLR worker: no Unity Player, author DLL, or art payloads."""
import argparse
import hashlib
import json
import shutil
import subprocess
from pathlib import Path
from check_contracts import ROOT, DATA
from windows_framework import references, identity


def build(output, entry=None, role='script-worker'):
    output = output.resolve()
    if output.exists():
        raise ValueError('ScriptWorker output must be new')
    output.mkdir(parents=True)
    rules = json.loads((ROOT / 'tools/sdk/rules/native-scene-v1.json').read_text(encoding='utf-8'))
    legacy = json.loads((ROOT / 'tools/sdk/rules/v1.json').read_text(encoding='utf-8'))
    digest = hashlib.sha256(json.dumps(rules, sort_keys=True, ensure_ascii=True, separators=(',', ':')).encode()).hexdigest()
    version = {'role': role, 'sdk': rules['sdk_target'], 'vm': legacy['vm'],
               'scene_command_abi': str(rules['abi']), 'scene_catalog_digest': digest, 'program_format': 'spatial-native-script-1'}
    metadata = output / 'AssemblyMetadata.cs'
    metadata.write_text('\n'.join('[assembly:System.Reflection.AssemblyMetadata(' + json.dumps(key) + ',' + json.dumps(value) + ')]'
                                  for key, value in version.items()), encoding='utf-8')
    shutil.copyfile(DATA / 'Managed/Newtonsoft.Json.dll', output / 'Newtonsoft.Json.dll')
    shutil.copyfile(ROOT / 'Supervisor/Supervisor.exe.config', output / 'ScriptWorker.exe.config')
    files = [entry or ROOT / 'Supervisor/Script/ScriptWorkerMain.cs', metadata,
             ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs',
             ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/WindowsMachineClock.cs',
             ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs',
             ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WorkerFrameCodec.cs',
             ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WindowsWorkerBoundary.cs',
             ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/WindowsWorkerStopReader.cs']
    files += sorted((ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities').glob('*.cs'))
    files += sorted((ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/Client').glob('*.cs'))
    files.append(ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Native/Windows/NativeStorageBindings.cs')
    for folder in ('Data', 'Security', 'Execution', 'Resources', 'Native'):
        files += sorted((ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime' / folder).glob('*.cs'))
    files = list(dict.fromkeys(files))
    source_hashes = {str(path.relative_to(ROOT)) if path.is_relative_to(ROOT) else str(path): hashlib.sha256(path.read_bytes()).hexdigest()
                     for path in files}
    command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'),
               '-nologo', '-langversion:latest', '-platform:x64', '-target:exe',
               '-r:' + str(output / 'Newtonsoft.Json.dll'), *references(), '-out:' + str(output / 'ScriptWorker.exe'), *map(str, files)]
    result = subprocess.run(command, capture_output=True, text=True)
    (output / 'compile.log').write_text(result.stdout + result.stderr, encoding='utf-8')
    print(result.stdout + result.stderr)
    result.check_returncode()
    for path in files:
        key = str(path.relative_to(ROOT)) if path.is_relative_to(ROOT) else str(path)
        assert hashlib.sha256(path.read_bytes()).hexdigest() == source_hashes[key], 'script source drift'
    runtime_files = {name: hashlib.sha256((output / name).read_bytes()).hexdigest()
                     for name in ('ScriptWorker.exe', 'ScriptWorker.exe.config', 'Newtonsoft.Json.dll')}
    (output / 'build.json').write_text(json.dumps({'identity': version, 'sources': source_hashes,
                                                  'framework_references': identity(), 'runtime_files': runtime_files,
                                                  'status': 'script-worker-built-not-sandbox-or-native-acceptance'}, indent=2), encoding='utf-8')
    print(output)


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    build(args.output)


if __name__ == '__main__':
    main()
