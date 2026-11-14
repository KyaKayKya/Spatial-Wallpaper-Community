"""Build universal native libraries; AVFoundation loads inside the existing Tracker."""
import argparse
from pathlib import Path
import subprocess
import sys
import shutil
from native_inputs import write_native_manifest

ROOT = Path(__file__).resolve().parents[2]

def compile_library(source, output, frameworks):
    output.parent.mkdir(parents=True, exist_ok=True)
    command = ['xcrun', 'clang', '-fobjc-arc', '-fmodules',
        '-fmodules-cache-path=/private/tmp/spatial-clang-modules', '-Wall', '-Wextra',
        '-Werror', '-Wno-unused-parameter', '-Wno-deprecated-declarations',
        '-arch', 'arm64', '-arch', 'x86_64', '-mmacosx-version-min=12.0',
        '-dynamiclib', '-install_name', '@rpath/' + output.name]
    for framework in frameworks:
        command += ['-framework', framework]
    subprocess.run([*command, str(source), '-o', str(output)], check=True)
    subprocess.run(['codesign', '--force', '--sign', '-', str(output)], check=True)

def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument('--output', type=Path, default=ROOT / 'Prototype/Assets/Plugins/macOS/libSpatialMac.dylib')
    args = parser.parse_args()
    if sys.platform != 'darwin':
        parser.error('Run native compilation on macOS with Xcode Command Line Tools')
    compile_library(ROOT / 'tools/macos/SpatialMac.m', args.output, ['AppKit', 'CoreGraphics'])
    compile_library(ROOT / 'tools/macos/SpatialCamera.m',
        ROOT / 'Validation/Desktop/native/libSpatialCamera.dylib',
        ['AVFoundation', 'Foundation', 'CoreMedia', 'CoreVideo'])
    output=ROOT/'Validation/Desktop/native'
    if args.output.resolve()!=(output/'libSpatialMac.dylib').resolve():
        shutil.copyfile(args.output,output/'libSpatialMac.dylib')
    write_native_manifest(ROOT,output)

if __name__ == '__main__':
    main()
