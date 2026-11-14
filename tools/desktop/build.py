"""Build the shared desktop product for an explicit target and record its inputs."""
import argparse
import hashlib
import json
import os
from pathlib import Path
import subprocess
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'macos'))
from native_inputs import prepare_native_inputs
from package_app import finalize_app

ROOT = Path(__file__).resolve().parents[2]

def sources():
    paths = []
    for folder in ['Prototype/Assets', 'Packages/com.spatialwallpaper.sdk']:
        # Include shared sources, platform adapters and imported plugin scripts.
        paths += list((ROOT / folder).rglob('*.cs'))
        paths += list((ROOT / folder).rglob('*.shader'))
    paths += list((ROOT / 'tools/macos').glob('*.m'))
    paths += [ROOT / 'tools/desktop/Launcher.cs',ROOT / 'tools/desktop/build_launcher.py',ROOT / 'tools/desktop/build.py',ROOT / 'tools/macos/PrivacyInfo.xcprivacy']
    paths += list((ROOT / 'tools/macos').glob('*.py'))
    paths += [ROOT / 'Prototype/Packages/manifest.json', ROOT / 'Prototype/Packages/packages-lock.json',
              ROOT / 'Prototype/ProjectSettings/ProjectVersion.txt']
    return {p.relative_to(ROOT).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
            for p in paths if p.is_file()}

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--unity', type=Path, default=os.environ.get('UNITY_EDITOR',
        'D:/Unity/Editor/2022.3.62f3/Editor/Unity.exe' if sys.platform == 'win32' else None))
    parser.add_argument('--target', choices=['macos-universal', 'windows-x86_64'], default='windows-x86_64')
    parser.add_argument('--native-dir',type=Path,default=os.environ.get('SPATIAL_MAC_NATIVE_DIR'),
        help='Mac-produced Universal libraries and native-manifest.json; required on Windows for a Mac build')
    args = parser.parse_args()
    if not args.unity or not args.unity.is_file():
        parser.error('Set --unity or UNITY_EDITOR to Unity 2022.3.62f3 executable')
    native=None
    if args.target == 'macos-universal':
        if args.native_dir is None:
            if sys.platform != 'darwin':
                parser.error('Set --native-dir to Mac-produced Universal libraries and native-manifest.json')
            subprocess.run([sys.executable, str(ROOT / 'tools/macos/build_native.py')], check=True)
            args.native_dir=ROOT/'Validation/Desktop/native'
        native=prepare_native_inputs(ROOT,args.native_dir)
    output = ROOT / 'Validation/Desktop'
    output.mkdir(parents=True, exist_ok=True)
    env = dict(os.environ, SPATIAL_BUILD_TARGET=args.target)
    if sys.platform == "win32":
        env.setdefault("ALLUSERSPROFILE",env.get("PROGRAMDATA","C:/ProgramData"))
    if args.target == 'windows-x86_64':
        subprocess.run([sys.executable,str(ROOT/'tools/blueprint/build_importer.py')],check=True)
    before = sources()
    tracked_changes_before=sorted(filter(None,subprocess.check_output(
        ['git','diff','--name-only','-z','HEAD'],cwd=ROOT).decode().split('\0')))
    build_result=ROOT/'Validation/Library/build-result.txt'
    if build_result.exists():
        build_result.unlink()
    result = subprocess.run([str(args.unity), '-batchmode', '-quit', '-projectPath', str(ROOT / 'Prototype'),
        '-buildTarget', 'OSXUniversal' if args.target == 'macos-universal' else 'Win64',
        '-executeMethod', 'DesktopBuild.Build', '-logFile', str(output / (args.target + '-build.log'))], env=env)
    if before != sources():
        raise RuntimeError('Build input changed; review import changes and rebuild')
    if result.returncode:
        raise SystemExit(result.returncode)
    if not build_result.is_file() or build_result.read_text().strip()!='Succeeded errors=0':
        raise RuntimeError('Unity did not produce a fresh successful Library build result')
    if args.target == 'windows-x86_64':
        subprocess.run([sys.executable,str(ROOT / 'tools/desktop/build_launcher.py')],check=True)
    binary = ROOT / 'Prototype/Builds' / ('Library-macos' if args.target == 'macos-universal' else 'Library')
    mac_app=finalize_app(ROOT,binary/'SpatialWallpaper.app') if native is not None else None
    settings=json.loads((ROOT/'Validation/Library/build-settings.json').read_text())
    changed=subprocess.check_output(['git','diff','--name-only','-z','HEAD'],cwd=ROOT).decode().split('\0')
    untracked=subprocess.check_output(['git','ls-files','--others','--exclude-standard','-z'],cwd=ROOT).decode().split('\0')
    dirty_sources=sorted(p for p in set(changed+untracked) if p in before or p.startswith(('Prototype/Assets/','Packages/')) and p.endswith(('.cs','.shader')))
    receipt = dict(target=args.target, sources=before, native=native, mac_app=mac_app, settings=settings, dirty_sources=dirty_sources,
        tracked_changes_before=tracked_changes_before,
        commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),
        exit_code=0, files={
        p.relative_to(binary).as_posix(): hashlib.sha256(p.read_bytes()).hexdigest()
        for p in binary.rglob('*') if p.is_file()})
    if not receipt['files']:
        raise RuntimeError('Unity returned success without a Player')
    name = 'build-receipt-macos.json' if args.target == 'macos-universal' else 'build-receipt.json'
    (output / name).write_text(json.dumps(receipt, indent=2), encoding='utf-8')
    print('Player built:', binary, 'receipt:', output / name)

if __name__ == '__main__':
    main()
