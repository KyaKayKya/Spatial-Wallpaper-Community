"""Check real external-native archives against original library transactions only."""
import argparse
import hashlib
import io
import json
import shutil
import subprocess
import tempfile
import uuid
import zipfile
from pathlib import Path
from check_contracts import ROOT, DATA
from windows_framework import references


def canonical(value):
    return json.dumps(value, sort_keys=True, separators=(',', ':'), ensure_ascii=False).encode('utf-8')


def identity(value):
    value.pop('build_id', None)
    value['build_id'] = hashlib.sha256(canonical(value)).hexdigest()


def archive_bytes(files):
    result = io.BytesIO()
    with zipfile.ZipFile(result, 'w', compression=zipfile.ZIP_DEFLATED) as archive:
        for name, data in files.items():
            archive.writestr(name, data)
    return result.getvalue()


def variant(source, output, kind):
    with zipfile.ZipFile(source) as archive:
        files = {name: archive.read(name) for name in archive.namelist()}
    manifest = json.loads(files['manifest.json'])
    if kind == 'update':
        manifest['revision'] += '-store-update'
    elif kind == 'conflict':
        manifest['name'] += ' conflicting bytes'
    elif kind == 'schema':
        manifest['schema'] = 4
    elif kind == 'tamper':
        files['script.json'] += b' '
    elif kind == 'no-share':
        with zipfile.ZipFile(io.BytesIO(files['assets.spatial'])) as archive:
            inner = {name: archive.read(name) for name in archive.namelist()}
        data = json.loads(inner['manifest.json'])
        data['reshare'] = False
        identity(data)
        inner['manifest.json'] = canonical(data)
        files['assets.spatial'] = archive_bytes(inner)
        manifest['assets']['build_id'] = data['build_id']
        manifest['files']['assets.spatial'] = {'size': len(files['assets.spatial']), 'sha256': hashlib.sha256(files['assets.spatial']).hexdigest()}
        manifest['revision'] += '-store-no-share'
    identity(manifest)
    files['manifest.json'] = canonical(manifest)
    with output.open('xb') as stream:
        stream.write(archive_bytes(files))


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--package', type=Path, required=True)
    parser.add_argument('--share-package', type=Path, help='Optional independently share-enabled package for public export positive check')
    parser.add_argument('--output', type=Path, required=True)
    parser.add_argument('--data-root', type=Path, help='Fresh short path for .NET Framework archive temporary names')
    args = parser.parse_args()
    out = args.output.resolve()
    out.mkdir(parents=True, exist_ok=False)
    data = args.data_root.resolve() if args.data_root else Path(tempfile.gettempdir()) / ('SpatialNativeStore-' + uuid.uuid4().hex[:12])
    if data.exists():
        raise ValueError('test data root must be fresh')
    for kind in ['update', 'conflict', 'schema', 'tamper', 'no-share']:
        variant(args.package, out / (kind + '.spatial'), kind)
    shutil.copyfile(DATA / 'Managed/Newtonsoft.Json.dll', out / 'Newtonsoft.Json.dll')
    shutil.copyfile(ROOT / 'Supervisor/Supervisor.exe.config', out / 'Checks.exe.config')
    files = [ROOT / 'tools/sdk/NativeExternalStoreChecks.cs', ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimeLimits.Generated.cs', ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Hosting/RuntimePackageInspection.cs', ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime/Capabilities/InputSnapshot.cs']
    for folder in ['Security', 'Data', 'Execution', 'Resources', 'Native']:
        files += sorted((ROOT / 'Packages/com.spatialwallpaper.sdk/Runtime' / folder).glob('*.cs'))
    files += sorted((ROOT / 'Prototype/Assets/ContentRuntime/Core').glob('*.cs'))
    command = [str(DATA / 'MonoBleedingEdge/bin/mono.exe'), str(DATA / 'MonoBleedingEdge/lib/mono/4.5/csc.exe'), '-nologo', '-langversion:latest', '-r:' + str(out / 'Newtonsoft.Json.dll'), *references(), '-r:System.Net.Http.dll', '-out:' + str(out / 'Checks.exe'), *map(str, files)]
    run = [str(out / 'Checks.exe'), str(data), str(args.package.resolve()), *[str(out / (kind + '.spatial')) for kind in ['update', 'conflict', 'schema', 'tamper', 'no-share']], str(args.share_package.resolve()) if args.share_package else '-']
    receipt = {'passed': False, 'source_commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(), 'data_root': str(data), 'sources': {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest() for p in files}, 'inputs': {str(p.resolve()): hashlib.sha256(p.read_bytes()).hexdigest() for p in [args.package, args.share_package] if p}, 'commands': [command, run], 'scope': 'Core library transactions on admitted actual archive; no Unity render, GPU, visual, sound or physical-display claim'}
    try:
        result = subprocess.run(command, capture_output=True, text=True)
        (out / 'compile.log').write_text(result.stdout + result.stderr, encoding='utf-8')
        result.check_returncode()
        result = subprocess.run(run, capture_output=True, text=True)
        (out / 'checks.log').write_text(result.stdout + result.stderr, encoding='utf-8')
        result.check_returncode()
        receipt['result'] = json.loads((data / 'result.json').read_text(encoding='utf-8-sig'))
        receipt['passed'] = True
    finally:
        (out / 'report.json').write_text(json.dumps(receipt, ensure_ascii=False, indent=2), encoding='utf-8')
    print(json.dumps({'passed': receipt['passed'], 'output': str(out), 'result': receipt.get('result')}, ensure_ascii=False))


if __name__ == '__main__':
    main()
