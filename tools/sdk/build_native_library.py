"""Generate the full original Unity asset-reference catalog in the current project."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess

ROOT = Path(__file__).resolve().parents[2]


def digest(path):
    return hashlib.sha256(path.read_bytes()).hexdigest() if path.is_file() else None


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, required=True)
    args = parser.parse_args()
    output = args.output.resolve()
    if output.exists():
        raise FileExistsError('Native library receipt output must be new')
    output.mkdir(parents=True)
    protected = json.loads((ROOT / 'Validation/Content/sdk-runtime/native-render/task3/build-receipt-snapshot-cache.json').read_text(encoding='utf-8'))['protected_sha256_unchanged']
    paths = sorted(set(protected) | {path.relative_to(ROOT).as_posix() for folder in ('Prototype/Assets', 'Packages/com.spatialwallpaper.sdk') for path in (ROOT / folder).rglob('*') if path.is_file() and path.suffix in ('.cs', '.unity', '.shader')})
    before = {path: digest(ROOT / path) for path in paths}
    command = ['D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe', '-batchmode', '-quit', '-projectPath', str(ROOT / 'Prototype'), '-executeMethod', 'SpatialWallpaper.Desktop.Editor.NativeSceneLibraryBuild.Generate', '-logFile', str(output / 'unity.log')]
    environment = dict(os.environ, SPATIAL_NATIVE_LIBRARY_OUTPUT=str(output))
    environment.setdefault('ALLUSERSPROFILE', environment.get('PROGRAMDATA', 'C:/ProgramData'))
    result = subprocess.run(command, cwd=ROOT, env=environment)
    unchanged = before == {path: digest(ROOT / path) for path in paths}
    receipt = output / 'result.json'
    report = {'command': command, 'exit_code': result.returncode, 'source_commit': subprocess.check_output(['git', 'rev-parse', 'HEAD'], cwd=ROOT, text=True).strip(), 'source_sha256': before, 'source_and_protected_unchanged': unchanged, 'result': json.loads(receipt.read_text(encoding='utf-8')) if receipt.is_file() else None, 'files_sha256': {path.name: digest(path) for path in output.iterdir() if path.is_file()}, 'scope': 'trusted native asset-reference catalog generation only; not foreign asset or final package/visual acceptance'}
    (output / 'build.json').write_text(json.dumps(report, indent=2), encoding='utf-8')
    print(json.dumps({key: value for key, value in report.items() if key not in ('command', 'source_sha256', 'files_sha256')}, indent=2))
    return 0 if result.returncode == 0 and unchanged and report['result'] is not None else 1


if __name__ == '__main__':
    raise SystemExit(main())
